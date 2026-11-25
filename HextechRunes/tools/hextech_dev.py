#!/usr/bin/env python3
"""海克斯维护入口：内容定位、显式文案同步、精确名称的定向测试。默认不写文件或构建。"""
from __future__ import annotations

import argparse
import difflib
import json
from pathlib import Path
import re
import shlex
import subprocess
import sys
import xml.etree.ElementTree as ET

from validate_hextech_content import (
    extract_forge_registrations,
    extract_monster_hex_registrations,
    extract_rune_registrations,
    model_id_entry,
    model_loc_stem,
    registry_source_text,
)

ROOT = Path(__file__).resolve().parents[1]


def read_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def find_content(query: str, limit: int) -> int:
    registry = registry_source_text()
    entries = [
        (group, entry)
        for group, parser in (("我方", extract_rune_registrations),
                              ("敌方", extract_monster_hex_registrations),
                              ("锻造", extract_forge_registrations))
        for entry in parser(registry)
    ]
    loc = read_json(ROOT / "assets/localization/zhs/relics.json")
    query = query.casefold()
    matches = []
    for group, entry in entries:
        name = entry["type"]
        key = model_id_entry(name)
        title = loc.get(key + ".title", "")
        if any(query in str(value).casefold()
               for value in (name, key, title, entry.get("kind", ""),
                             str(entry.get("kind", "")) + "EnemyHex")):
            matches.append((group, entry, key, title))
    # 只读源码和测试；不把 dist/缓存中的同名类当成实现。
    sources = [p for folder in (ROOT / "src", ROOT / "tests/HextechRunes.Tests")
               for p in sorted(folder.rglob("*.cs"))
               if not {"bin", "obj"}.intersection(p.parts)]
    texts = {p: p.read_text(encoding="utf-8") for p in sources} if matches else {}
    for group, entry, key, title in matches[:limit]:
        name = entry["type"]
        print(f"\n[{group}] {title} | {name} | {entry['rarity']}")
        print("  注册: " + json.dumps(entry, ensure_ascii=False, default=sorted))
        for loc_key in (key + ".description", model_loc_stem(name) + ".enemyDescription"):
            if loc_key in loc:
                print(f"  {loc_key}: {loc[loc_key]}")
        symbols = [name]
        if group == "敌方":
            symbols += [str(entry["kind"]) + "EnemyHex", "MonsterHexKind." + str(entry["kind"])]
        pattern = re.compile(r"(?<!\w)(?:" + "|".join(map(re.escape, symbols)) + r")(?!\w)")
        for path, source in texts.items():
            lines = [str(i) for i, line in enumerate(source.splitlines(), 1) if pattern.search(line)]
            if lines:
                print(f"  {path}:{','.join(lines[:6])}" + (" …" if len(lines) > 6 else ""))
        icon = ROOT / "assets/images/relics" / (model_loc_stem(name) + ".png")
        if icon.is_file():
            print(f"  贴图: {icon}")
    print(f"\n匹配 {len(matches)} 项，显示 {min(len(matches), limit)} 项。")
    if not matches:
        print("此命令只索引注册的符文/敌方海克斯/锻造器；卡牌和未注册内容请在 src 中用 rg 查询。")
    return 0 if matches else 1


def localization_changes(root: Path, table: str, source_key: str, target_key: str,
                         locales: list[str] | None) -> list[tuple[Path, str, str]]:
    """先校验所有所选语言，避免某语言缺键时已改写前面的文件。保留其它行原样。"""
    base = root / "assets/localization"
    paths = ([base / locale / f"{table}.json" for locale in locales] if locales
             else sorted(base.glob(f"*/{table}.json")))
    if not paths:
        raise ValueError(f"没有找到 {table}.json")
    changes = []
    for path in paths:
        before = path.read_text(encoding="utf-8")
        data = json.loads(before)
        for key in (source_key, target_key):
            if not isinstance(data.get(key), str):
                raise ValueError(f"{path}: 缺少字符串键 {key}")
        if data[source_key] == data[target_key]:
            continue
        # 只替换指定 JSON 字符串的值；不重排整个文件，也不改同内容的其它键。
        pattern = re.compile(r'(?m)^(\s*' + re.escape(json.dumps(target_key, ensure_ascii=False))
                             + r'\s*:\s*)"(?:[^"\\]|\\.)*"')
        after, count = pattern.subn(
            lambda match: match[1] + json.dumps(data[source_key], ensure_ascii=False), before)
        if count != 1:
            raise ValueError(f"{path}: 目标键需为独立一行，找到 {count} 处；请手动编辑")
        expected = dict(data)
        expected[target_key] = data[source_key]
        if json.loads(after) != expected:
            raise ValueError(f"{path}: 替换影响了其它 JSON 内容")
        changes.append((path, before, after))
    return changes


def sync_localization(args: argparse.Namespace) -> int:
    changes = localization_changes(ROOT, args.table, args.source, args.destination, args.locale)
    for path, before, after in changes:
        print("".join(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                         fromfile=str(path), tofile=str(path))), end="")
    if args.apply:
        for path, _, after in changes:
            path.write_text(after, encoding="utf-8")
    print(f"{'已写入' if args.apply else '预览'} {len(changes)} 个文件；仅同步指定键，不翻译也不修改 TXT/PCK。")
    return 1 if args.check and changes else 0


TEST_METHOD = re.compile(r"\[HextechTest\]\s*(?:private\s+|internal\s+|public\s+)?static\s+void\s+(\w+)\s*\(")


def registered_tests() -> list[str]:
    """测试程序自动收集 Program 上标了 [HextechTest] 的方法并按名称 Ordinal 排序；这里按同一规则静态列出。"""
    folder = ROOT / "tests/HextechRunes.Tests"
    names = {name for path in folder.glob("*.cs")
             for name in TEST_METHOD.findall(path.read_text(encoding="utf-8"))}
    return sorted(names)


def targets() -> list[str]:
    """维护中的目标 = 仓库根 Directory.Build.targets 里 HextechValidateTarget 放行的目标（最低目标不定义版本符号）。"""
    project = ET.parse(ROOT.parent / "Directory.Build.targets")
    validator = next(target for target in project.findall("Target")
                     if target.get("Name") == "HextechValidateTarget")
    return re.findall(r"'\$\(HextechVariantTarget\)' != '([^']+)'", validator.get("Condition", ""))


def focused_tests(args: argparse.Namespace) -> int:
    known = registered_tests()
    if args.list:
        if args.name or args.run or args.target:
            raise ValueError("--list 不与 --name/--target/--run 混用")
        print("\n".join(name for name in known if args.match.casefold() in name.casefold()))
        return 0
    if not args.name or not args.target:
        raise ValueError("定向测试需要 --target 和至少一个 --name；先用 tests --list 查名称")
    if args.match:
        raise ValueError("--match 只用于 --list；执行必须使用精确 --name")
    unknown = set(args.name) - set(known)
    if unknown:
        raise ValueError("未知测试名: " + ", ".join(sorted(unknown)))
    if args.target not in targets():
        raise ValueError("未维护的目标；可用值: " + ", ".join(targets()))
    refs = ROOT / "versioned-dll-backups" / args.target / "game-refs"
    if args.run and not (refs / "sts2.dll").is_file():
        raise ValueError(f"缺少目标引用: {refs}")
    # 构建参数与测试程序路径只在 run_tests.sh 里维护,这里只转交目标与测试名。
    command = ["bash", str(ROOT / "tools/run_tests.sh"), "--target", args.target, *dict.fromkeys(args.name)]
    print(shlex.join(command), flush=True)
    if args.run:
        subprocess.run(command, cwd=ROOT, check=True)
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    find = sub.add_parser("find", help="按中文名/类名/模型键/敌方 kind 定位注册及引用")
    find.add_argument("query")
    find.add_argument("--limit", type=int, default=6)
    loc = sub.add_parser("loc-copy", help="预览或显式同步同语义的两个本地化键（所有语言）")
    loc.add_argument("source")
    loc.add_argument("destination")
    loc.add_argument("--table", choices=["relics", "relic_collection", "cards", "powers"], default="relics")
    loc.add_argument("--locale", action="append", choices=sorted(
        path.name for path in (ROOT / "assets/localization").iterdir() if path.is_dir()))
    mode = loc.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="写入展示的差异")
    mode.add_argument("--check", action="store_true", help="有差异时返回 1，不写文件")
    tests = sub.add_parser("tests", help="列测试，或预览/执行定向测试（不部署）")
    tests.add_argument("--list", action="store_true")
    tests.add_argument("--match", default="")
    tests.add_argument("--name", action="append")
    tests.add_argument("--target")
    tests.add_argument("--run", action="store_true")
    args = parser.parse_args()
    try:
        if args.command == "find":
            if not args.query.strip() or args.limit < 1:
                raise ValueError("查询不能为空，--limit 必须大于 0")
            return find_content(args.query, args.limit)
        if args.command == "loc-copy":
            return sync_localization(args)
        return focused_tests(args)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"错误: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
