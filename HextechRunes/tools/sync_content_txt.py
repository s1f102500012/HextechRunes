#!/usr/bin/env python3
"""同步三个内容 txt 与注册表/本地化真值。

真值来源:
- 玩家符文: src/Content/HextechPlayerRuneRegistry.cs + 拓展包 SponsorCatalog.cs 的 PlayerRunes 表
- 敌方海克斯: src/Content/HextechMonsterHexRegistry.cs
- 锻造器: src/Content/HextechForgeRegistry.cs + 拓展包 SponsorCatalog.cs 的 Forges 表
- 事件遗物: 拓展包 SponsorCatalog.cs 的 EventRelics 表
- 标题/flavor/敌方描述: assets/localization/zhs/relics.json(本体+拓展包)
- 标签中文: assets/localization/zhs/relic_collection.json 的 HEXTECH_TAG.*

目标文件与策略:
- hextech_rune_tags.txt        纯生成物,全量重生成(保留既有行序,新条目插到注册表邻位)。
- hextech_relic_flavors.txt    纯生成物,全量重生成,flavor 一律取 zhs JSON。
- hextech_relics_summary.txt   混合物,只做增量:补缺失条目、修 #禁用前缀/品级前缀;
                               描述永不覆盖(单条采纳用 --accept-json);删除需 --prune。

模式:
- 默认只读预览: 报告差异;存在需落盘的差异时 exit 1。
- --apply: 写回三个 txt。
- --apply --prune: 同时删除 summary 中已从注册表移除的条目。
- --accept-json "锚": 单条采纳 JSON 描述覆盖 summary 条目,锚格式见 --help 示例,
  如 "怪物:棱彩:金铲铲"、"[我方 / 通用]:黄金:大法师"、"卡牌:白洞"。

类名 -> 本地化键不手写驼峰规则:对 JSON 键去下划线 casefold 反查
(SingularityAIRune 等连续大写由 JSON 键侧真值决定)。
"""
from __future__ import annotations

import argparse
import ast
from decimal import Decimal
import json
import re
import sys
from pathlib import Path

from validate_hextech_content import (
    extract_forge_registrations,
    extract_monster_hex_registrations,
    extract_rune_registrations,
)

ROOT = Path(__file__).resolve().parents[1]
SPONSOR = ROOT.parent / "HextechRunesSponsorPack"

TAGS_TXT = ROOT / "hextech_rune_tags.txt"
FLAVORS_TXT = ROOT / "hextech_relic_flavors.txt"
SUMMARY_TXT = ROOT / "hextech_relics_summary.txt"

RARITY_ZH = {"Silver": "白银", "Gold": "黄金", "Prismatic": "棱彩"}
POOL_SECTION = {
    None: "[我方 / 通用]",
    "Ironclad": "[我方 / 铁甲战士]",
    "Silent": "[我方 / 静默猎手]",
    "Regent": "[我方 / 储君]",
    "Defect": "[我方 / 故障机器人]",
    "Necrobinder": "[我方 / 亡灵契约师]",
}
TAG_SECTION_ORDER = [
    "[我方 / 通用]",
    "[我方 / 铁甲战士]",
    "[我方 / 静默猎手]",
    "[我方 / 储君]",
    "[我方 / 故障机器人]",
    "[我方 / 亡灵契约师]",
]

# 拓展包锻造器/新增拓展包符文条目在 flavors 属性锻造器区使用的后缀约定,
# 与 summary 一致(仅用于新生成条目;既有条目文本由 JSON 决定)。
SPONSOR_SUFFIX = "（赞助者拓展包）"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def strip_markup(text: str) -> str:
    text = re.sub(r"\[/?[A-Za-z][A-Za-z0-9_]*(?:[= ][^\]]*)?\]", "", text)
    text = re.sub(r"<br\s*/?>", "", text, flags=re.I)
    return text.replace("\n", "")


def class_sources(roots: tuple[Path, ...]) -> dict[str, str]:
    """按类边界读取，避免同文件的另一张卡覆盖本类变量。"""
    result: dict[str, str] = {}
    for root in roots:
        for path in sorted(root.rglob("*.cs")):
            source = read(path)
            # 保留偏移；字符串/注释里的花括号不参与 C# 类边界计数。
            masked = re.sub(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"',
                            lambda match: " " * len(match.group()), source, flags=re.S)
            for match in re.finditer(r"\bclass\s+(\w+)[^;{]*\{", masked):
                depth, end = 1, match.end()
                while end < len(masked) and depth:
                    depth += (masked[end] == "{") - (masked[end] == "}")
                    end += 1
                result.setdefault(match.group(1).replace("_", "").casefold(), source[match.start():end])
    return result


def canonical_values(source: str, inherited: tuple[str, ...] = ()) -> dict[str, str]:
    """只解释声明里的数值/常量算式，不执行 C#，也不猜测运行时变量。

    inherited 是基类源码（由近到远）：本类没有 CanonicalVars 时用最近一个声明了它的基类；
    子类里 `override int X => 2;` 这类表达式体属性和 const 一样参与求值（子类优先）。"""
    constants: dict[str, str] = {}
    for text in reversed((source, *inherited)):
        constants.update(re.findall(r"\bconst\s+\w+\s+(\w+)\s*=\s*([^;]+);", text))
        constants.update(re.findall(r"\boverride\s+(?:int|decimal)\s+(\w+)\s*=>\s*([^;]+);", text))

    def number(expression: str, resolving: frozenset[str] = frozenset()) -> Decimal:
        expression = expression.strip()
        if expression in constants:
            if expression in resolving:
                raise ValueError("循环常量")
            return number(constants[expression], resolving | {expression})
        expression = re.sub(r"(?<=\d)[mMfFdD]\b", "", expression)
        tree = ast.parse(expression, mode="eval").body

        def evaluate(node: ast.AST) -> Decimal:
            if isinstance(node, ast.Constant) and type(node.value) in (int, float):
                return Decimal(str(node.value))
            if isinstance(node, ast.Name):
                if node.id not in constants:
                    raise ValueError(f"非静态常量: {node.id}")
                return number(node.id, resolving)
            if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
                return evaluate(node.operand) * (-1 if isinstance(node.op, ast.USub) else 1)
            if isinstance(node, ast.BinOp):
                left, right = evaluate(node.left), evaluate(node.right)
                if isinstance(node.op, ast.Add):
                    return left + right
                if isinstance(node.op, ast.Sub):
                    return left - right
                if isinstance(node.op, ast.Mult):
                    return left * right
                # C# 整型除法与 decimal 除法不同；未保留完整类型系统时禁止猜值。
            raise ValueError(f"不支持静态求值: {expression}")

        return evaluate(tree)

    values: dict[str, str] = {}
    declarations = None
    for text in (source, *inherited):
        literal = re.search(r"\bCanonicalVars\s*=>\s*\[(.*?)\];", text, re.S)
        if literal is not None:
            declarations = literal.group(1)
            break
        # `CanonicalVars => CreateXxxVars(7.5m);`：到基类里找这个静态辅助，把实参代入它返回的数组。
        call = re.search(r"\bCanonicalVars\s*=>\s*(\w+)\(([^()]*)\);", text)
        if call is not None:
            helper_name, arguments = call.groups()
            for base in (text, *inherited):
                helper = re.search(
                    rf"\bstatic\s+IEnumerable<DynamicVar>\s+{helper_name}\s*\(([^()]*)\)\s*\{{\s*return\s*\[(.*?)\];",
                    base, re.S)
                if helper is not None:
                    parameters = [part.split()[-1] for part in helper.group(1).split(",") if part.strip()]
                    constants.update(zip(parameters, (arg.strip() for arg in arguments.split(","))))
                    declarations = helper.group(2)
                    break
            break
    if declarations is None:
        return values
    for match in re.finditer(r"new\s+(\w+Var)(?:<(\w+)>)?\s*\(((?:[^()]|\([^()]*\))*)\)", declarations):
        kind, power, arguments = match.groups()
        args = [arg.strip() for arg in arguments.split(",")]
        first_argument = constants.get(args[0], args[0]).strip()
        if kind == "DynamicVar" or re.fullmatch(r'"[^\"]+"', first_argument):
            name = constants.get(args[0], args[0]).strip().strip('"')
            expression = args[1]
        else:
            name = power if kind == "PowerVar" else kind.removesuffix("Var")
            expression = args[0]
        try:
            value = format(number(expression), "f")
        except (ValueError, SyntaxError, ArithmeticError):
            continue  # 真正使用了无法求值的占位符时，由渲染入口报错，禁止裸变量进入 TXT。
        values[name] = value.rstrip("0").rstrip(".") if "." in value else value
    return values


# ---------------------------------------------------------------------------
# 真值装载


def sponsor_table(sponsor_src: str, name: str) -> str:
    match = re.search(rf"{name}\s*=\s*\[(.*?)\];", sponsor_src, re.S)
    if match is None:
        raise SystemExit(f"SponsorCatalog.cs 里找不到 {name} 表,注册表结构变了,先更新本脚本。")
    return match.group(1)


class Loc:
    """本体+拓展包 zhs relics.json;类名按字段做 casefold 反查。"""

    def __init__(self) -> None:
        self.sources = [
            json.loads(read(ROOT / "assets" / "localization" / "zhs" / "relics.json")),
            json.loads(read(SPONSOR / "assets" / "localization" / "zhs" / "relics.json")),
        ]
        self.fold: dict[str, dict[str, tuple[dict, str]]] = {}
        for loc in self.sources:
            for key in loc:
                stem, _, field = key.rpartition(".")
                folded = stem.replace("_", "").casefold()
                self.fold.setdefault(field, {}).setdefault(folded, (loc, stem))

    def get(self, class_name: str, field: str) -> str | None:
        entry = self.fold.get(field, {}).get(class_name.replace("_", "").casefold())
        if entry is None:
            return None
        loc, stem = entry
        return loc.get(f"{stem}.{field}")


class Truth:
    def __init__(self) -> None:
        self.loc = Loc()
        tags_loc = json.loads(
            read(ROOT / "assets" / "localization" / "zhs" / "relic_collection.json")
        )
        self.tag_zh = {
            key.split(".", 1)[1]: value
            for key, value in tags_loc.items()
            if key.startswith("HEXTECH_TAG.")
        }
        # 拓展包的注册在 SponsorCatalog 的只读表里(元组表,行序即注册序)。
        sponsor_src = read(SPONSOR / "src" / "Content" / "SponsorCatalog.cs")
        self.player = self._load_player_runes(sponsor_src)
        self.monster = self._load_monster_hexes()
        self.forge = self._load_forges(sponsor_src)
        # 事件遗物(排除选择界面用的 *ChoiceRelic:UI 伪遗物,不入清单)。
        self.event_relics = [
            match.group(1)
            for match in re.finditer(r"typeof\((\w+)\)", sponsor_table(sponsor_src, "EventRelics"))
            if not match.group(1).endswith("ChoiceRelic")
        ]
        # 卡牌(本体 cards.json 全部 .title)。
        cards = json.loads(read(ROOT / "assets" / "localization" / "zhs" / "cards.json"))
        self.cards: dict[str, dict[str, str]] = {}
        for key, value in cards.items():
            stem, _, field = key.rpartition(".")
            self.cards.setdefault(stem, {})[field] = value

    @staticmethod
    def _load_player_runes(sponsor_src: str) -> list[dict]:
        """玩家符文(注册表顺序 = 真值顺序;本体在前,拓展包在后)。注册解析与 validate_hextech_content 共用。"""
        registry = read(ROOT / "src" / "Content" / "HextechPlayerRuneRegistry.cs")
        player = [
            {
                "class": reg["type"],
                "rarity": reg["rarity"],
                "pool": reg["character_pool"],
                "tag_key": reg["tag_key"],
                "disabled": "Disabled" in reg["flags"],
                "source": "main",
            }
            for reg in extract_rune_registrations(registry)
        ]
        for match in re.finditer(
            r"\(\s*typeof\((\w+)\),\s*HextechRarityTier\.(\w+),\s*\"(\w+)\"\s*\)",
            sponsor_table(sponsor_src, "PlayerRunes"),
        ):
            player.append(
                {
                    "class": match.group(1),
                    "rarity": match.group(2),
                    "pool": None,
                    "tag_key": match.group(3),
                    "disabled": False,
                    "source": "sponsor",
                }
            )
        return player

    @staticmethod
    def _load_monster_hexes() -> list[dict]:
        """敌方海克斯;配置里默认禁用的 kind 也按禁用处理。"""
        monster_src = read(ROOT / "src" / "Content" / "HextechMonsterHexRegistry.cs")
        config_src = read(ROOT / "src" / "Config" / "HextechRuneConfiguration.cs")
        config_default_disabled = set(re.findall(r"MonsterHexKind\.(\w+)", config_src))
        return [
            {
                "class": reg["type"],
                "kind": reg["kind"],
                "rarity": reg["rarity"],
                "disabled": reg["disabled"] or reg["kind"] in config_default_disabled,
            }
            for reg in extract_monster_hex_registrations(monster_src)
        ]

    @staticmethod
    def _load_forges(sponsor_src: str) -> list[dict]:
        """锻造器(本体+拓展包)。"""
        forge_src = read(ROOT / "src" / "Content" / "HextechForgeRegistry.cs")
        forge = [
            {"class": reg["type"], "rarity": reg["rarity"], "source": "main"}
            for reg in extract_forge_registrations(forge_src)
        ]
        for match in re.finditer(
            r"\(\s*typeof\((\w+)\),\s*HextechRarityTier\.(\w+)\s*\)", sponsor_table(sponsor_src, "Forges")
        ):
            forge.append({"class": match.group(1), "rarity": match.group(2), "source": "sponsor"})
        return forge

    def resolve_placeholders(self, cls: str, text: str) -> str:
        """离线说明使用未升级的 CanonicalVars；依赖对局的数值显示公式。"""
        if "{" not in text:
            return strip_markup(text)
        if not hasattr(self, "_class_sources"):
            self._class_sources = class_sources((ROOT / "src", SPONSOR / "src"))
        source = self._class_sources.get(cls.replace("_", "").casefold(), "")
        values = canonical_values(source, self._base_sources(source))
        if cls == "FlyingKickRune":
            # 当前 ExecutePercent 在获得玩家实例后刷新，离线不能冒充玩家实际阈值。
            values["ExecutePercent"] = (
                f"（{values['BaseExecutePercent']}+自身最大生命值×"
                f"{values['OwnerMaxHpToExecutePercent']}%）"
            )
        return self.render_placeholders(cls, text, values)

    def _base_sources(self, source: str) -> tuple[str, ...]:
        """沿 `class X : Base` 找本仓库里的基类源码；到原版或外部类型为止。"""
        bases: list[str] = []
        seen: set[str] = set()
        while (match := re.search(r"\bclass\s+\w+\s*:\s*(\w+)", source)) is not None:
            key = match.group(1).replace("_", "").casefold()
            if key in seen or key not in self._class_sources:
                break
            seen.add(key)
            source = self._class_sources[key]
            bases.append(source)
        return tuple(bases)

    @staticmethod
    def render_placeholders(cls: str, text: str, values: dict[str, str]) -> str:
        def replace(match: re.Match) -> str:
            name, formatter = match.group(1), match.group(2)
            if name not in values:
                raise ValueError(f"{cls}: TXT 无法解析 {{{name}}}，请补充其离线数值来源。")
            value = values[name]
            if formatter == "energyIcons()":
                return f"{value}点能量"
            if formatter not in (None, "diff()"):
                raise ValueError(f"{cls}: TXT 尚未支持格式 {formatter}")
            return value

        rendered = re.sub(r"\{(\w+)(?::([^{}]+))?\}", replace, text)
        if "{" in rendered or "}" in rendered:
            raise ValueError(f"{cls}: TXT 尚未支持的复合占位符: {rendered}")
        return strip_markup(rendered)

    def enemy_summary(self, cls: str, kind: str) -> str:
        text = self.enemy_description(cls) or ""
        if "{" not in text:
            return strip_markup(text)
        # 这是敌方 LocString 真正使用的参数表，不能套用同名玩家符文的变量。
        catalog = read(ROOT / "src" / "EnemyHexes" / "MonsterHexCatalog.cs")
        values: dict[str, str] = {}
        for entry in re.finditer(rf"\[MonsterHexKind\.{re.escape(kind)}\]\s*=\s*([^\n]+)", catalog):
            for name, base in re.findall(r'\("(\w+)",\s*(\d+)\)', entry.group(1)):
                values[name] = "N" if base == "1" else f"{base}N"
        rendered = self.render_placeholders(cls, text, values)
        return rendered + "（N为玩家人数）"

    def title(self, cls: str) -> str | None:
        return self.loc.get(cls, "title")

    def flavor(self, cls: str) -> str | None:
        return self.loc.get(cls, "flavor")

    def enemy_description(self, cls: str) -> str | None:
        return self.loc.get(cls, "enemyDescription")


# ---------------------------------------------------------------------------
# 通用: 「标题：正文」拆分(标题可能含全角冒号,如「升级：放血」)


def split_titled(body: str, known_titles: set[str]) -> tuple[str, str]:
    parts = body.split("：")
    for cut in range(len(parts) - 1, 0, -1):
        candidate = "：".join(parts[:cut])
        if candidate in known_titles:
            return candidate, "：".join(parts[cut:])
    if body.startswith(("升级：", "质变：")) and body.count("：") >= 2:
        cut = body.index("：", body.index("：") + 1)
        return body[:cut], body[cut + 1 :]
    cut = body.index("：")
    return body[:cut], body[cut + 1 :]


def insert_position(existing: list[str], anchor_order: list[str], item: str) -> int:
    """按真值顺序把 item 插到既有序列中:排在真值序中最近的、已存在的前驱之后。"""
    if item not in anchor_order:
        return len(existing)
    idx = anchor_order.index(item)
    for prev in reversed(anchor_order[:idx]):
        if prev in existing:
            return existing.index(prev) + 1
    return 0


# ---------------------------------------------------------------------------
# 1) hextech_rune_tags.txt —— 全量重生成


def generate_tags(truth: Truth, current_text: str, report: list[str]) -> str:
    known = {reg["class"]: reg for reg in truth.player}

    # 现有顺序(粘行按“我方\t”再切一次)。
    section = None
    order: dict[str, list[str]] = {name: [] for name in TAG_SECTION_ORDER}
    seen: set[str] = set()
    for raw in current_text.splitlines():
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            section = line
            continue
        for match in re.finditer(r"#?我方\t(\w+)\t", raw):
            cls = match.group(1)
            if section in order and cls not in seen:
                order[section].append(cls)
                seen.add(cls)

    ghosts = sorted(seen - set(known))
    if ghosts:
        report.append(f"tags: 移除幽灵条目 {len(ghosts)} 条: {', '.join(ghosts)}")

    # 目标归属与真值顺序。
    target: dict[str, list[str]] = {name: [] for name in TAG_SECTION_ORDER}
    for reg in truth.player:
        target[POOL_SECTION[reg["pool"]]].append(reg["class"])

    blocks: list[str] = []
    added: list[str] = []
    for name in TAG_SECTION_ORDER:
        existing = [cls for cls in order[name] if cls in known and POOL_SECTION[known[cls]["pool"]] == name]
        # 跨节漂移的既有条目也按真值节归位。
        for other in TAG_SECTION_ORDER:
            if other == name:
                continue
            for cls in order[other]:
                if cls in known and POOL_SECTION[known[cls]["pool"]] == name and cls not in existing:
                    existing.insert(insert_position(existing, target[name], cls), cls)
                    report.append(f"tags: {cls} 从 {other} 移至 {name}")
        for cls in target[name]:
            if cls not in existing:
                existing.insert(insert_position(existing, target[name], cls), cls)
                added.append(cls)
        lines = [name]
        for cls in existing:
            reg = known[cls]
            title = truth.title(cls)
            if title is None:
                report.append(f"tags: {cls} 在 zhs relics.json 中找不到标题,跳过")
                continue
            prefix = "#" if reg["disabled"] else ""
            tag = truth.tag_zh.get(reg["tag_key"], reg["tag_key"])
            lines.append(f"{prefix}我方\t{cls}\t{title}\t标签={tag}")
        blocks.append("\n".join(lines))
    if added:
        report.append(f"tags: 新增缺失条目 {len(added)} 条: {', '.join(added)}")
    return "\n\n".join(blocks) + "\n"


# ---------------------------------------------------------------------------
# 2) hextech_relic_flavors.txt —— 全量重生成


def flavor_truth_entries(truth: Truth) -> dict[str, list[tuple[str | None, str, str]]]:
    """章节 -> [(品级中文或 None, 标题, flavor)],顺序 = 注册表真值顺序。"""
    sections: dict[str, list[tuple[str | None, str, str]]] = {
        "玩家海克斯": [],
        "敌方海克斯": [],
        "属性锻造器": [],
        "商店": [],
        "事件遗物": [],
    }
    for reg in truth.player:
        title, flavor = truth.title(reg["class"]), truth.flavor(reg["class"])
        if title and flavor:
            sections["玩家海克斯"].append((RARITY_ZH[reg["rarity"]], title, flavor))
    for reg in truth.monster:
        title, flavor = truth.title(reg["class"]), truth.flavor(reg["class"])
        if title and flavor:
            sections["敌方海克斯"].append((RARITY_ZH[reg["rarity"]], title, flavor))
    for reg in truth.forge:
        title, flavor = truth.title(reg["class"]), truth.flavor(reg["class"])
        if title and flavor:
            sections["属性锻造器"].append((RARITY_ZH[reg["rarity"]], title, flavor))
    title = truth.title("RandomForgeShopRelic")
    flavor = truth.flavor("RandomForgeShopRelic")
    if title and flavor:
        sections["商店"].append((None, title, flavor))
    for cls in truth.event_relics:
        title, flavor = truth.title(cls), truth.flavor(cls)
        if title and flavor:
            sections["事件遗物"].append((None, title, flavor))
    return sections


FlavorKey = tuple[str, "str | None", str]


def parse_flavor_entries(lines: list[str], all_titles: set[str]) -> dict[FlavorKey, tuple[int, str]]:
    """现有条目顺序与现值: (章节, 品级, 标题) -> (顺位, txt flavor)。第一个章节标题之前的头部不计入。"""
    section = sub = None
    current: dict[FlavorKey, tuple[int, str]] = {}
    for line in lines:
        stripped = line.strip()
        if section is None and not stripped.startswith("- ") and not stripped.endswith("："):
            continue
        if stripped.endswith("：") and not stripped.startswith("- "):
            name = stripped[:-1]
            if name in ("白银", "黄金", "棱彩"):
                sub = name
            else:
                section, sub = name, None
            continue
        if stripped.startswith("- ") and section:
            title, flavor = split_titled(stripped[2:], all_titles)
            key = (section, sub, title)
            if key not in current:
                current[key] = (len(current), flavor)
    return current


class FlavorEmitter:
    """按真值生成一个品级块:既有条目保持旧顺位,新条目插到真值邻位,flavor 取 JSON。"""

    def __init__(self, current: dict[FlavorKey, tuple[int, str]]) -> None:
        self.current = current
        self.consumed: set[FlavorKey] = set()
        self.added: list[str] = []

    def emit(self, section_name: str, rarity: str | None, items: list[tuple[str | None, str, str]]) -> list[str]:
        picked = [(t, f) for r, t, f in items if r == rarity]
        anchor_order = [t for t, _ in picked]
        ordered = sorted(
            (t for t, _ in picked if (section_name, rarity, t) in self.current),
            key=lambda t: self.current[(section_name, rarity, t)][0],
        )
        for t, _ in picked:
            if t not in ordered:
                ordered.insert(insert_position(ordered, anchor_order, t), t)
                if not any(k[0] == section_name and k[2] == t for k in self.current):
                    self.added.append(f"[{section_name}/{rarity or '-'}] {t}")
        flavor_of = dict(picked)
        result = []
        for t in ordered:
            self.consumed.add((section_name, rarity, t))
            result.append(f"- {t}：{flavor_of[t]}")
        return result


def classify_leftover_flavors(
    current: dict[FlavorKey, tuple[int, str]],
    consumed: set[FlavorKey],
    entries: dict[str, list[tuple[str | None, str, str]]],
) -> tuple[list[str], list[str]]:
    """未被真值消费的旧条目:同名条目挂在别的品级(且旧文件该品级下没有同名行)算品级归位,否则是真移除。"""
    truth_keys = {
        (section_name, rarity, title)
        for section_name, items in entries.items()
        for rarity, title, _ in items
    }
    moved: list[str] = []
    removed: list[str] = []
    for key in current:
        if key in consumed:
            continue
        relocated = any(
            truth_key[0] == key[0]
            and truth_key[2] == key[2]
            and truth_key[1] != key[1]
            and truth_key not in current
            for truth_key in truth_keys
        )
        if relocated:
            moved.append(f"[{key[0]}] {key[2]}: {key[1]} -> 真值品级")
        else:
            removed.append(f"[{key[0]}/{key[1] or '-'}] {key[2]}")
    return moved, removed


def flavor_totals_line(truth: Truth) -> str:
    forge_main = sum(1 for reg in truth.forge if reg["source"] == "main")
    forge_sponsor = sum(1 for reg in truth.forge if reg["source"] == "sponsor")
    forge_by_rarity = {
        rarity: sum(1 for reg in truth.forge if reg["rarity"] == rarity)
        for rarity in ("Silver", "Gold", "Prismatic")
    }
    return (
        "总计：玩家海克斯 {} 个（可选 {} 个）；敌方海克斯 {} 个（可选 {} 个）；"
        "属性锻造器 {} 个（本体 {} + 赞助者拓展包 {}；白银 {}、黄金 {}、棱彩 {}）；"
        "商店锻造器 1 个。".format(
            len(truth.player),
            sum(1 for reg in truth.player if not reg["disabled"]),
            len(truth.monster),
            sum(1 for reg in truth.monster if not reg["disabled"]),
            forge_main + forge_sponsor,
            forge_main,
            forge_sponsor,
            forge_by_rarity["Silver"],
            forge_by_rarity["Gold"],
            forge_by_rarity["Prismatic"],
        )
    )


def generate_flavors(truth: Truth, current_text: str, report: list[str]) -> str:
    entries = flavor_truth_entries(truth)
    all_titles = {title for items in entries.values() for _, title, _ in items}
    lines = current_text.splitlines()
    current = parse_flavor_entries(lines, all_titles)

    # 头部(到第一个章节标题前)原样保留。
    first_section_idx = next(i for i, line in enumerate(lines) if line.strip() == "玩家海克斯：")
    out: list[str] = list(lines[:first_section_idx])
    emitter = FlavorEmitter(current)
    for section_name in ("玩家海克斯", "敌方海克斯", "属性锻造器"):
        out.append(f"{section_name}：")
        out.append("")
        for rarity in ("白银", "黄金", "棱彩"):
            out.append(f"{rarity}：")
            out.extend(emitter.emit(section_name, rarity, entries[section_name]))
            out.append("")
    for section_name in ("商店", "事件遗物"):
        out.append(f"{section_name}：")
        out.extend(emitter.emit(section_name, None, entries[section_name]))
        out.append("")

    moved, removed = classify_leftover_flavors(current, emitter.consumed, entries)
    out.append(flavor_totals_line(truth))

    if emitter.added:
        report.append(f"flavors: 新增缺失条目 {len(emitter.added)} 条: " + "; ".join(emitter.added))
    if moved:
        report.append(f"flavors: 品级归位 {len(moved)} 条: " + "; ".join(sorted(moved)))
    if removed:
        report.append(f"flavors: 移除已失效条目 {len(removed)} 条: " + "; ".join(sorted(removed)))
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------------------
# 3) hextech_relics_summary.txt —— 增量同步


def summary_truth(truth: Truth) -> dict[str, list[dict]]:
    sections: dict[str, list[dict]] = {name: [] for name in TAG_SECTION_ORDER}
    sections["怪物"] = []
    sections["属性锻造器"] = []
    sections["卡牌"] = []
    sections["事件遗物"] = []
    for reg in truth.player:
        title = truth.title(reg["class"])
        if title is None:
            continue
        sections[POOL_SECTION[reg["pool"]]].append(
            {
                "rarity": RARITY_ZH[reg["rarity"]],
                "title": title,
                "disabled": reg["disabled"],
                "desc": truth.resolve_placeholders(
                    reg["class"], strip_markup(truth.loc.get(reg["class"], "description") or "")
                ),
                "suffix": SPONSOR_SUFFIX if reg["source"] == "sponsor" else "",
            }
        )
    for reg in truth.monster:
        title = truth.title(reg["class"])
        if title is None:
            continue
        sections["怪物"].append(
            {
                "rarity": RARITY_ZH[reg["rarity"]],
                "title": title,
                "disabled": reg["disabled"],
                "desc": truth.enemy_summary(reg["class"], reg["kind"]),
                "suffix": "",
            }
        )
    for reg in truth.forge:
        title = truth.title(reg["class"])
        if title is None:
            continue
        sections["属性锻造器"].append(
            {
                "rarity": RARITY_ZH[reg["rarity"]],
                "title": title,
                "disabled": False,
                "desc": truth.resolve_placeholders(
                    reg["class"], strip_markup(truth.loc.get(reg["class"], "description") or "")
                ),
                "suffix": SPONSOR_SUFFIX if reg["source"] == "sponsor" else "",
            }
        )
    for stem, fields in truth.cards.items():
        title = fields.get("title")
        if not title:
            continue
        desc = fields.get("hoverTip") or fields.get("description") or ""
        sections["卡牌"].append(
            {"rarity": None, "title": title, "disabled": False,
             "desc": truth.resolve_placeholders(stem, desc), "suffix": ""}
        )
    for cls in truth.event_relics:
        title = truth.title(cls)
        if title is None:
            continue
        sections["事件遗物"].append(
            {"rarity": None, "title": title, "disabled": False,
             "desc": truth.resolve_placeholders(cls, truth.loc.get(cls, "description") or ""), "suffix": ""}
        )
    return sections


SUMMARY_PLAIN_SECTIONS = ("卡牌：", "怪物：", "属性锻造器：", "事件遗物：", "特定规则：")
SUMMARY_RARITY_ENTRY = re.compile(r"^(#?)(白银|黄金|棱彩)：(.+)$")


def summary_section_bounds(lines: list[str]) -> dict[str, tuple[int, int]]:
    """段名 -> (起始行号, 结束行号开区间)。"""
    marks: list[tuple[int, str]] = []
    for i, line in enumerate(lines):
        stripped = line.strip()
        if stripped in POOL_SECTION.values():
            marks.append((i, stripped))
        elif stripped in SUMMARY_PLAIN_SECTIONS:
            marks.append((i, stripped.rstrip("：")))
    bounds: dict[str, tuple[int, int]] = {}
    for idx, (start, name) in enumerate(marks):
        end = marks[idx + 1][0] if idx + 1 < len(marks) else len(lines)
        bounds[name] = (start, end)
    return bounds


class SummarySync:
    """summary 的增量同步:只补缺失条目、修前缀、按 --accept-json 采纳;手写描述不覆盖,删除需 --prune。"""

    def __init__(self, sections: dict[str, list[dict]], lines: list[str], accepts: set[str]) -> None:
        self.sections = sections
        self.lines = lines
        self.accepts = accepts
        self.known_titles = {entry["title"] for items in sections.values() for entry in items}
        self.bounds = summary_section_bounds(lines)
        self.edits: dict[int, str | None] = {}  # 行号 -> 替换内容(None=删除)
        self.inserts: dict[int, list[str]] = {}  # 在该行号之前插入
        self.stale: list[dict] = []  # {anchor, rarity, title, desc, line}
        self.missing: list[dict] = []  # {anchor, entry, pos, use_rarity}
        self.prefix_fixed: list[str] = []
        self.accepted: list[str] = []
        self.unknown_accepts = set(accepts)
        self.moved_notes: list[str] = []
        self.added: list[str] = []

    def parse_section(self, start: int, end: int, use_rarity: bool) -> tuple[list[tuple], set[object]]:
        """预扫: 收集本节已存在的锚,避免把“品级漂移”误判到已有同名条目的品级上。"""
        present: set[object] = set()
        parsed: list[tuple[int, bool, str | None, str, str, str]] = []
        for i in range(start + 1, end):
            stripped = self.lines[i].strip()
            if not stripped:
                continue
            if use_rarity:
                match = SUMMARY_RARITY_ENTRY.match(stripped)
                if not match:
                    continue  # 章节头部说明行等,原样保留
                has_hash, rarity, body = match.group(1) == "#", match.group(2), match.group(3)
                title, desc = split_titled(body, self.known_titles)
                key = (rarity, title)
            else:
                if "：" not in stripped:
                    continue
                has_hash, rarity = False, None
                title, desc = split_titled(stripped, self.known_titles)
                body = stripped
                key = title
            parsed.append((i, has_hash, rarity, title, desc, body))
            present.add(key)
        return parsed, present

    def handle_section(self, name: str, use_rarity: bool, report: list[str]) -> None:
        if name not in self.bounds:
            report.append(f"summary: 找不到章节 {name}")
            return
        start, end = self.bounds[name]
        want = {
            ((entry["rarity"], entry["title"]) if use_rarity else entry["title"]): entry
            for entry in self.sections[name]
        }
        parsed, present = self.parse_section(start, end, use_rarity)
        seen, last_line_of_rarity = self.match_existing(name, use_rarity, want, parsed, present)
        self.collect_missing(name, use_rarity, want, seen, last_line_of_rarity, start, end)

    def match_existing(self, name: str, use_rarity: bool, want: dict, parsed: list[tuple],
                       present: set[object]) -> tuple[dict[object, int], dict[str | None, int]]:
        seen: dict[object, int] = {}
        last_line_of_rarity: dict[str | None, int] = {}
        for i, has_hash, rarity, title, desc, body in parsed:
            if not use_rarity:
                # 卡牌升级行(标题+…)归属基础卡锚,不单独建锚。
                base_title = re.sub(r"\+.*$", "", title)
                key = base_title if base_title in want else title
                if key != title:
                    last_line_of_rarity[None] = i
                    seen.setdefault(key, i)
                    continue
            else:
                key = (rarity, title)
            last_line_of_rarity[rarity] = i
            anchor = f"{name}:{rarity}:{title}" if use_rarity else f"{name}:{title}"
            if key in want:
                seen.setdefault(key, i)
                self.update_known_entry(i, want[key], anchor, use_rarity, has_hash, rarity, title, body)
                continue

            drift = self.find_rarity_drift(name, title, present) if use_rarity else None
            if drift is not None:
                new_prefix = "#" if drift["disabled"] else ""
                self.edits[i] = f"{new_prefix}{drift['rarity']}：{title}：{desc}"
                self.prefix_fixed.append(f"{name}:{rarity}:{title} 品级改为 {drift['rarity']}")
                seen.setdefault((drift["rarity"], title), i)
            else:
                self.stale.append({"anchor": anchor, "rarity": rarity, "title": title, "desc": desc, "line": i})
        return seen, last_line_of_rarity

    def update_known_entry(self, i: int, entry: dict, anchor: str, use_rarity: bool, has_hash: bool,
                           rarity: str | None, title: str, body: str) -> None:
        if use_rarity and has_hash != entry["disabled"]:
            new_prefix = "#" if entry["disabled"] else ""
            self.edits[i] = f"{new_prefix}{rarity}：{body}"
            self.prefix_fixed.append(anchor)
        if anchor in self.accepts:
            self.unknown_accepts.discard(anchor)
            prefix = "#" if entry["disabled"] and use_rarity else ""
            head = f"{prefix}{rarity}：" if use_rarity else ""
            self.edits[i] = f"{head}{title}：{entry['desc']}{entry['suffix']}"
            self.accepted.append(anchor)

    def find_rarity_drift(self, name: str, title: str, present: set[object]) -> dict | None:
        """品级漂移: 同名条目真值挂在本节别的品级,且该品级下尚无同名条目。"""
        for entry in self.sections[name]:
            if entry["title"] == title and (entry["rarity"], title) not in present:
                return entry
        return None

    def collect_missing(self, name: str, use_rarity: bool, want: dict, seen: dict[object, int],
                        last_line_of_rarity: dict[str | None, int], start: int, end: int) -> None:
        """缺失条目: 记录插入点(对应品级块末尾;无该品级块则章节末尾)。"""
        for key, entry in want.items():
            if key in seen:
                continue
            pos = last_line_of_rarity.get(entry["rarity"] if use_rarity else None)
            if pos is None:
                pos = end - 1
                while pos > start and not self.lines[pos].strip():
                    pos -= 1
            anchor = f"{name}:{entry['rarity']}:{entry['title']}" if use_rarity else f"{name}:{entry['title']}"
            self.missing.append({"anchor": anchor, "entry": entry, "pos": pos + 1, "use_rarity": use_rarity})

    def pair_cross_section_moves(self) -> None:
        """跨节漂移: 缺失条目与已移除条目按(品级,标题)配对 -> 移动既有手写行,不重写描述。"""
        for miss in self.missing:
            entry = miss["entry"]
            match = next(
                (item for item in self.stale if item["rarity"] == entry["rarity"] and item["title"] == entry["title"]),
                None,
            )
            if match is None:
                continue
            miss["move_desc"] = match["desc"]
            self.stale.remove(match)
            self.edits[match["line"]] = None
            self.moved_notes.append(f"{match['anchor']} -> {miss['anchor']}")

    def schedule_inserts(self, prune: bool) -> None:
        for miss in self.missing:
            entry = miss["entry"]
            prefix = "#" if entry["disabled"] and miss["use_rarity"] else ""
            head = f"{prefix}{entry['rarity']}：" if miss["use_rarity"] else ""
            desc = miss.get("move_desc", f"{entry['desc']}{entry['suffix']}")
            self.inserts.setdefault(miss["pos"], []).append(f"{head}{entry['title']}：{desc}")
            self.added.append(miss["anchor"])
        if prune:
            for item in self.stale:
                self.edits[item["line"]] = None

    def render(self) -> str:
        out: list[str] = []
        for i, line in enumerate(self.lines):
            if i in self.inserts:
                out.extend(self.inserts[i])
            if i in self.edits:
                if self.edits[i] is not None:
                    out.append(self.edits[i])
            else:
                out.append(line)
        if len(self.lines) in self.inserts:
            out.extend(self.inserts[len(self.lines)])
        return "\n".join(out) + "\n"

    def report_changes(self, report: list[str], prune: bool) -> None:
        if self.added:
            report.append(f"summary: 新增缺失条目 {len(self.added)} 条: " + "; ".join(self.added))
        if self.moved_notes:
            report.append(f"summary: 跨节移动 {len(self.moved_notes)} 条(保留原描述): " + "; ".join(self.moved_notes))
        if self.prefix_fixed:
            report.append(f"summary: 修正禁用/品级前缀 {len(self.prefix_fixed)} 条: " + "; ".join(self.prefix_fixed))
        if self.stale:
            action = "已删除" if prune else "保留原位(--prune 才删除)"
            report.append(
                f"summary: 已从注册表移除的条目 {len(self.stale)} 条,{action}: "
                + "; ".join(item["anchor"] for item in self.stale)
            )
        if self.accepted:
            report.append(f"summary: 按 --accept-json 采纳 JSON 描述 {len(self.accepted)} 条: " + "; ".join(self.accepted))
        if self.unknown_accepts:
            report.append(f"summary: --accept-json 未匹配到锚: {', '.join(sorted(self.unknown_accepts))}")


def sync_summary(
    truth: Truth,
    current_text: str,
    report: list[str],
    prune: bool,
    accepts: set[str],
) -> str:
    sync = SummarySync(summary_truth(truth), current_text.splitlines(), accepts)
    for name in TAG_SECTION_ORDER:
        sync.handle_section(name, True, report)
    sync.handle_section("怪物", True, report)
    sync.handle_section("属性锻造器", True, report)
    sync.handle_section("卡牌", False, report)
    sync.handle_section("事件遗物", False, report)
    sync.pair_cross_section_moves()
    sync.schedule_inserts(prune)
    text = sync.render()
    sync.report_changes(report, prune)
    return text


# ---------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--apply", action="store_true", help="写回三个 txt(默认只检查)")
    parser.add_argument("--prune", action="store_true", help="配合 --apply,删除 summary 中已移除条目")
    parser.add_argument(
        "--accept-json",
        action="append",
        default=[],
        metavar="锚",
        help='单条采纳 JSON 描述覆盖 summary 条目,如 "怪物:棱彩:金铲铲" 或 "卡牌:白洞"',
    )
    args = parser.parse_args()

    truth = Truth()
    report: list[str] = []

    tags_new = generate_tags(truth, read(TAGS_TXT), report)
    flavors_new = generate_flavors(truth, read(FLAVORS_TXT), report)
    summary_new = sync_summary(
        truth, read(SUMMARY_TXT), report, prune=args.prune, accepts=set(args.accept_json)
    )

    changed = {
        path.name: new != read(path)
        for path, new in (
            (TAGS_TXT, tags_new),
            (FLAVORS_TXT, flavors_new),
            (SUMMARY_TXT, summary_new),
        )
    }

    for line in report:
        print(line)

    if args.apply:
        for path, new in ((TAGS_TXT, tags_new), (FLAVORS_TXT, flavors_new), (SUMMARY_TXT, summary_new)):
            if new != read(path):
                path.write_text(new, encoding="utf-8")
                print(f"已写入 {path.name}")
            else:
                print(f"{path.name} 无变化")
        return 0

    dirty = [name for name, flag in changed.items() if flag]
    if dirty:
        print(f"需要同步(--apply): {', '.join(dirty)}")
        return 1
    print("三个 txt 与真值一致。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
