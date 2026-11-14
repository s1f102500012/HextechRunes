#!/usr/bin/env python3
from __future__ import annotations

import json
from collections import Counter
import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SRC = REPO_ROOT / "src"
LOCALIZATION = REPO_ROOT / "assets" / "localization"
SPONSOR_LOCALIZATION = REPO_ROOT.parent / "HextechRunesSponsorPack" / "assets" / "localization"
TELEMETRY_LABELS = REPO_ROOT / "server" / "hextech-telemetry" / "labels.json"
OFFICIAL_ZHS_TITLES = REPO_ROOT / "tools" / "official_zhs_titles.json"
UNTRANSLATED_ALLOWLIST = REPO_ROOT / "tools" / "localization_untranslated_allowlist.json"
MOD_ID = "HextechRunes"
SPONSOR_PACK = "HextechRunesSponsorPack"
UNTRANSLATED_MIN_LATIN_LETTERS = 4

def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def source_files(pattern: str = "*.cs") -> list[Path]:
    return [
        path
        for path in SRC.rglob(pattern)
        if "bin" not in path.parts and "obj" not in path.parts
    ]


def source_file_named(name: str) -> Path:
    matches = [path for path in source_files(name) if path.name == name]
    if len(matches) != 1:
        raise ValueError(f"expected exactly one source file named {name}, found {len(matches)}")
    return matches[0]


def registry_source_text() -> str:
    """全部注册表都在 src/Content/ 下(HextechContentRegistry.cs 与各 *Registry.cs)。"""
    return "\n".join(read(path) for path in sorted((SRC / "Content").glob("*.cs")))


def player_rune_enum_values(enum_name: str) -> list[str]:
    """PlayerRuneFlags / PlayerRuneCharacterPool 的成员以 C# 枚举为唯一来源(去掉 None)。"""
    values = extract_enum_values(read(SRC / "Content" / "PlayerRuneRegistration.cs"), enum_name)
    return [value for value in values if value != "None"]


def lower_first(value: str) -> str:
    return value[:1].lower() + value[1:]


def model_loc_stem(type_name: str) -> str:
    """复刻运行时键规则:StringHelper.Slugify(类名) 后经 HextechAssets.ToImageFileStem 归一化。
    连续大写缩写按整段处理,如 SingularityAIRune -> singularityAiRune(而非 singularityAIRune)。"""
    slug = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1_\2", type_name)
    slug = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", slug)
    parts = [p for p in slug.lower().split("_") if p]
    return parts[0] + "".join(p[:1].upper() + p[1:] for p in parts[1:])


def model_id_entry(type_name: str) -> str:
    stem = model_loc_stem(type_name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", stem).upper()


def extract_enum_values(text: str, enum_name: str) -> list[str]:
    match = re.search(rf"enum\s+{enum_name}\s*\{{(?P<body>.*?)\n\}}", text, re.S)
    if not match:
        raise ValueError(f"enum {enum_name} not found")
    values: list[str] = []
    for line in match.group("body").splitlines():
        line = line.split("//", 1)[0].strip().rstrip(",")
        if not line:
            continue
        values.append(line.split("=", 1)[0].strip())
    return values


def extract_block(text: str, name: str) -> str:
    patterns = [
        rf"{name}\s*(?:\{{\s*get;\s*\}}\s*)?=\s*\[(?P<body>.*?)\];",
        rf"{name}\s*=\s*new\s+HashSet<[^>]+>\s*\{{(?P<body>.*?)\}};",
        rf"{name}\s*=\s*new\s+Dictionary<[^>]+>\s*\{{(?P<body>.*?)\}};",
    ]
    for pattern in patterns:
        match = re.search(pattern, text, re.S)
        if match:
            return match.group("body")
    raise ValueError(f"{name} block not found")


def extract_type_list(text: str, name: str) -> list[str]:
    return re.findall(r"typeof\((\w+)\)", extract_block(text, name))


def extract_rune_registrations(text: str) -> list[dict[str, object]]:
    registrations: list[dict[str, object]] = []
    pattern = re.compile(
        r"Rune<(?P<type>\w+)>\(\s*HextechRarityTier\.(?P<rarity>\w+)(?P<args>[^)]*)\)"
    )
    for match in pattern.finditer(text):
        args = match.group("args")
        character_pool_match = re.search(r"characterPool:\s*PlayerRuneCharacterPool\.(\w+)", args)
        character_order_match = re.search(r"characterOrder:\s*(\d+)", args)
        registrations.append(
            {
                "type": match.group("type"),
                "rarity": match.group("rarity"),
                "flags": set(re.findall(r"PlayerRuneFlags\.(\w+)", args)),
                "character_pool": character_pool_match.group(1) if character_pool_match else None,
                "character_order": int(character_order_match.group(1)) if character_order_match else 0,
                "tag_key": (re.search(r'tagKey:\s*"([^"]+)"', args) or [None, "COMPREHENSIVE"])[1],
            }
        )
    return registrations


def extract_forge_registrations(text: str) -> list[dict[str, str]]:
    pattern = re.compile(r"Forge<(?P<type>\w+)>\(\s*HextechRarityTier\.(?P<rarity>\w+)\s*\)")
    return [
        {
            "type": match.group("type"),
            "rarity": match.group("rarity"),
        }
        for match in pattern.finditer(text)
    ]


def extract_monster_hex_registrations(text: str) -> list[dict[str, object]]:
    registrations: list[dict[str, object]] = []
    pattern = re.compile(
        r"Monster<(?P<type>\w+)>\(\s*MonsterHexKind\.(?P<kind>\w+),\s*HextechRarityTier\.(?P<rarity>\w+)(?P<args>[^)]*)\)"
    )
    for match in pattern.finditer(text):
        args = match.group("args")
        registrations.append(
            {
                "type": match.group("type"),
                "kind": match.group("kind"),
                "rarity": match.group("rarity"),
                "disabled": bool(re.search(r"disabled:\s*true", args)),
            }
        )
    return registrations


def check_duplicates(errors: list[str], label: str, values: list[str]) -> None:
    duplicates = sorted(value for value, count in Counter(values).items() if count > 1)
    if duplicates:
        errors.append(f"{label} has duplicates: {', '.join(duplicates)}")


def validate_monster_hex_registry(errors: list[str], warnings: list[str]) -> None:
    types_text = read(SRC / "HextechTypes.cs")
    registry_text = registry_source_text()

    enum_values = extract_enum_values(types_text, "MonsterHexKind")
    monster_regs = extract_monster_hex_registrations(registry_text)
    if not monster_regs:
        errors.append("MonsterHexRegistrations block not found")
        return

    registry_values = [str(reg["kind"]) for reg in monster_regs]
    rarity_values = [str(reg["kind"]) for reg in monster_regs if not reg["disabled"]]
    disabled_values = [str(reg["kind"]) for reg in monster_regs if reg["disabled"]]
    check_duplicates(errors, "monster hex registry", registry_values)

    missing_from_rarity = sorted(set(enum_values) - set(rarity_values) - set(disabled_values))
    unknown_in_rarity = sorted(set(rarity_values) - set(enum_values))
    unknown_disabled = sorted(set(disabled_values) - set(enum_values))
    if missing_from_rarity:
        errors.append(f"MonsterHexKind missing from rarity or disabled registry: {', '.join(missing_from_rarity)}")
    if unknown_in_rarity:
        errors.append(f"Unknown MonsterHexKind in rarity registry: {', '.join(unknown_in_rarity)}")
    if unknown_disabled:
        errors.append(f"Unknown MonsterHexKind in disabled registry: {', '.join(unknown_disabled)}")

    icon_pairs = {str(reg["kind"]): str(reg["type"]) for reg in monster_regs}

    for locale in ("zhs", "eng"):
        loc = json.loads(read(LOCALIZATION / locale / "relics.json"))
        missing: list[str] = []
        for hex_name in enum_values:
            relic_type = icon_pairs.get(hex_name)
            if relic_type is None:
                continue
            key = f"{model_loc_stem(relic_type)}.enemyDescription"
            if key not in loc:
                missing.append(key)
        if missing:
            errors.append(f"{locale} relics.json missing enemy descriptions: {', '.join(missing)}")

    # 敌方专属图标 relic（类名 *Hex）必须同时登记在 EnemyHexIconRelicTypes，
    # 否则图标路径不被 TryGetCustomRelicIconPath 认定，游戏内显示 NOPE。
    enemy_icon_types = set(extract_type_list(registry_text, "EnemyHexIconRelicTypes"))
    hex_icon_types = {str(reg["type"]) for reg in monster_regs if str(reg["type"]).endswith("Hex")}
    missing_icon_types = sorted(hex_icon_types - enemy_icon_types)
    if missing_icon_types:
        errors.append(f"Enemy hex icon relic types missing from EnemyHexIconRelicTypes: {', '.join(missing_icon_types)}")


def validate_relic_registry(errors: list[str]) -> None:
    registry_text = registry_source_text()

    rune_regs = extract_rune_registrations(registry_text)
    forge_regs = extract_forge_registrations(registry_text)
    if not rune_regs:
        errors.append("RuneRegistrations block not found")
        return
    if not forge_regs:
        errors.append("ForgeRegistrations block not found")
        return

    all_types: list[str] = []

    for rarity in ("Silver", "Gold", "Prismatic"):
        all_types.extend(str(reg["type"]) for reg in rune_regs if reg["rarity"] == rarity)

    for rarity in ("Silver", "Gold", "Prismatic"):
        all_types.extend(str(reg["type"]) for reg in forge_regs if reg["rarity"] == rarity)

    all_types.extend(extract_type_list(registry_text, "ShopOnlyRelicTypes"))

    for character_pool in player_rune_enum_values("PlayerRuneCharacterPool"):
        orders = [str(reg["character_order"]) for reg in rune_regs if reg["character_pool"] == character_pool]
        check_duplicates(errors, f"{character_pool}RuneTypes character order", orders)
        missing_order = [str(reg["type"]) for reg in rune_regs if reg["character_pool"] == character_pool and reg["character_order"] == 0]
        if missing_order:
            errors.append(f"{character_pool}RuneTypes missing character order: {', '.join(missing_order)}")

    tag_keys = sorted({str(reg["tag_key"]) for reg in rune_regs})
    for locale in ("zhs", "eng"):
        loc = json.loads(read(LOCALIZATION / locale / "relic_collection.json"))
        missing_tags = [tag_key for tag_key in tag_keys if f"HEXTECH_TAG.{tag_key}" not in loc]
        if missing_tags:
            errors.append(f"{locale} relic_collection.json missing rune tag localization: {', '.join(missing_tags)}")

    # 各稀有度、角色池、开关只是这份清单的切片，在这里查一次重即可。
    check_duplicates(errors, "all custom relic registries", all_types)

    source_text = "\n".join(read(path) for path in source_files())
    declared_relics = set(re.findall(r"\bclass\s+(\w+)\s*:", source_text))
    missing_declarations = sorted(set(all_types) - declared_relics)
    if missing_declarations:
        errors.append(f"registered relic types not declared: {', '.join(missing_declarations)}")


def validate_rune_file_layout(errors: list[str]) -> None:
    for path in source_files():
        text = read(path)
        rune_classes = re.findall(r"^public\s+sealed\s+class\s+(\w+Rune)\b", text, re.M)
        if len(rune_classes) > 1:
            errors.append(f"{path.relative_to(REPO_ROOT)} contains multiple rune classes: {', '.join(rune_classes)}")
            continue

        if len(rune_classes) == 1 and path.stem != rune_classes[0]:
            errors.append(f"{path.relative_to(REPO_ROOT)} should be named {rune_classes[0]}.cs")


def validate_enemy_hex_effect_layout(errors: list[str]) -> None:
    registry_text = registry_source_text()
    effect_registry_text = read(SRC / "EnemyHexes" / "HextechEnemyHexEffects.cs")
    monster_regs = extract_monster_hex_registrations(registry_text)
    if not monster_regs:
        errors.append("MonsterHexRegistrations block not found for enemy hex effect layout")
        return

    for reg in monster_regs:
        kind = str(reg["kind"])
        expected_class = f"{kind}EnemyHex"
        expected_path = SRC / "EnemyHexes" / f"{expected_class}.cs"
        if not expected_path.exists():
            errors.append(f"enemy hex effect file missing: {expected_path.relative_to(REPO_ROOT)}")
            continue

        text = read(expected_path)
        # 同类效果可共用 src/EnemyHexes/*EnemyHexBase.cs 里的抽象基类（它们本身继承 HextechEnemyHexEffect）。
        class_pattern = rf"\binternal\s+sealed\s+class\s+{expected_class}\s*:\s*(?:HextechEnemyHexEffect|\w+EnemyHexBase)\b"
        if not re.search(class_pattern, text):
            errors.append(f"{expected_path.relative_to(REPO_ROOT)} should declare {expected_class} : HextechEnemyHexEffect (or an *EnemyHexBase)")

        kind_pattern = rf"\bKind\s*=>\s*MonsterHexKind\.{kind}\b"
        if not re.search(kind_pattern, text):
            errors.append(f"{expected_path.relative_to(REPO_ROOT)} should bind Kind to MonsterHexKind.{kind}")

        if f"new {expected_class}()" not in effect_registry_text:
            errors.append(f"enemy hex effect registry missing {expected_class}")


def resolve_string_constants(source: str) -> dict[str, str]:
    """解析 `const string Name = 片段 + 片段;`（字符串字面量、ModInfo.Id、同文件常量）为最终字符串。"""
    raw = dict(re.findall(r"const string (\w+)\s*=\s*([^;]+);", source))
    resolved: dict[str, str] = {}

    def evaluate(name: str, depth: int = 0) -> str | None:
        if name in resolved:
            return resolved[name]
        if depth > 16 or name not in raw:
            return None
        parts: list[str] = []
        for token in (part.strip() for part in raw[name].split("+")):
            if token.startswith('"') and token.endswith('"'):
                parts.append(token[1:-1])
            elif token == "ModInfo.Id":
                parts.append(MOD_ID)
            else:
                value = evaluate(token, depth + 1)
                if value is None:
                    return None
                parts.append(value)
        resolved[name] = "".join(parts)
        return resolved[name]

    for constant in raw:
        evaluate(constant)
    return resolved


def referenced_asset_paths(source: str) -> set[str]:
    """源码中引用的 res://<模组>/images/… 路径：字面量、常量值、`常量 + "文件名"` 表达式。"""
    constants = resolve_string_constants(source)
    candidates = set(re.findall(r'"(res://[^"]+)"', source))
    candidates.update(constants.values())
    for name, file_name in re.findall(r'\b(\w+)\s*\+\s*"([^"]+\.(?:png|jpg))"', source):
        if name in constants:
            candidates.add(constants[name] + file_name)
    prefix = f"res://{MOD_ID}/"
    return {
        path[len(prefix):]
        for path in candidates
        if path.startswith(prefix + "images/") and re.search(r"\.(?:png|jpg)$", path)
    }


def shared_relic_icon_stems() -> dict[str, str]:
    """从 HextechAssets.TryGetCustomRelicIconPath 解析"复用其他模型图标"的分支:模型图标名 -> 实际贴图名。"""
    source = read(source_file_named("HextechAssets.cs"))
    body = re.search(r"TryGetCustomRelicIconPath\(RelicModel relic\)\s*\{(?P<body>.*?)\n\t\}", source, re.S)
    if body is None:
        raise ValueError("HextechAssets.TryGetCustomRelicIconPath not found")
    stems: dict[str, str] = {}
    for types, stem in re.findall(
        r"if \(relic is (?P<types>\w+(?:\s+or\s+\w+)*)\)\s*\{\s*"
        r"return (?:\$\"res://\{ModInfo\.Id\}/images/relics/|RelicImages \+ \")(?P<stem>\w+)\.png\";",
        body.group("body"),
    ):
        for type_name in re.split(r"\s+or\s+", types):
            stems[model_loc_stem(type_name)] = stem
    return stems


def validate_icon_assets(errors: list[str], warnings: list[str]) -> None:
    """注册模型的图标 png 必须存在于 assets(缺图运行期只会静默 NOPE 占位,这里提前到构建期报错);
    同时对 relics 目录做孤儿资源告警。路径规则复刻 HextechAssets.TryGetCustomRelicIconPath。"""
    relics_dir = REPO_ROOT / "assets" / "images" / "relics"
    registry_text = registry_source_text()
    shared_icon_stems = shared_relic_icon_stems()

    expected_stems: set[str] = set()
    # 这些事件遗物通过原版 IconBaseName 复用 atlas/大图，不应要求模组再复制 PNG。
    vanilla_icon_types = {
        match for path in (SRC / "Relics" / "Orobas").glob("*.cs")
        for match in re.findall(r"class\s+(\w+)\s*:\s*OrobasPlusRelicBase", read(path))
    }
    rune_regs = extract_rune_registrations(registry_text)
    for reg in rune_regs:
        expected_stems.add(model_loc_stem(str(reg["type"])))
    for values_list in ("EnemyHexIconRelicTypes", "EventRelicTypes"):
        for type_name in extract_type_list(registry_text, values_list):
            if type_name not in vanilla_icon_types:
                expected_stems.add(model_loc_stem(type_name))

    expected_stems.update(shared_icon_stems.values())

    run_modifiers_dir = SRC / "RunModifiers"
    for source_path in run_modifiers_dir.glob("*.cs"):
        expected_stems.update(
            re.findall(r'images/relics/([^"/]+)\.png', read(source_path))
        )

    missing = sorted(
        stem
        for stem in expected_stems
        if not (relics_dir / f"{shared_icon_stems.get(stem, stem)}.png").exists()
    )
    if missing:
        errors.append(f"registered relic icon png missing under assets/images/relics: {', '.join(missing)}")

    # 锻造器三档与商店占位图标是共享固定名。
    for shared in ("silverForge", "goldForge", "prismaticForge"):
        if not (relics_dir / f"{shared}.png").exists():
            errors.append(f"shared forge icon missing: assets/images/relics/{shared}.png")
        expected_stems.add(shared)

    orphans = sorted(
        path.name
        for path in relics_dir.glob("*.png")
        if path.stem not in expected_stems
    )
    if orphans:
        warnings.append(f"orphan relic icon png (no registered model references them): {', '.join(orphans)}")

    # HextechAssets/HextechAssetHooks 里显式书写的 res:// 路径逐一核对存在性(卡牌立绘/能力图标/特效贴图)。
    assets_root = REPO_ROOT / "assets"
    referenced = set()
    for name in ("HextechAssets.cs", "HextechAssetHooks.cs", "HextechRuneSelectionScreen.Metrics.cs"):
        referenced.update(referenced_asset_paths(read(source_file_named(name))))
    missing_refs = sorted(ref for ref in referenced if not (assets_root / ref).exists())
    if missing_refs:
        errors.append(f"hardcoded asset path missing under assets/: {', '.join(missing_refs)}")


def localization_roots() -> list[tuple[str, Path]]:
    """本体必查；拓展包目录存在时一并检查（单独拷出本体时跳过）。"""
    roots = [(MOD_ID, LOCALIZATION)]
    if SPONSOR_LOCALIZATION.is_dir():
        roots.append((SPONSOR_PACK, SPONSOR_LOCALIZATION))
    return roots


def localization_label(pack: str, relative: str) -> str:
    return relative if pack == MOD_ID else f"{pack}:{relative}"


def validate_localization_key_parity(errors: list[str], root: Path | None = None, pack: str = MOD_ID) -> None:
    """9 语言逐文件键集一致性(以 eng 为基准):漏译键会静默回退,这里提前到构建期报出。"""
    root = LOCALIZATION if root is None else root
    baseline_dir = root / "eng"
    if not baseline_dir.exists():
        errors.append(localization_label(pack, "localization baseline directory eng missing"))
        return

    baseline = {
        path.name: set(json.loads(read(path)).keys())
        for path in sorted(baseline_dir.glob("*.json"))
    }
    for locale_dir in sorted(root.iterdir()):
        if not locale_dir.is_dir() or locale_dir.name == "eng":
            continue

        for file_name, baseline_keys in baseline.items():
            locale_file = locale_dir / file_name
            label = localization_label(pack, f"{locale_dir.name}/{file_name}")
            if not locale_file.exists():
                errors.append(f"{localization_label(pack, locale_dir.name)} missing localization file {file_name}")
                continue

            locale_keys = set(json.loads(read(locale_file)).keys())
            missing = sorted(baseline_keys - locale_keys)
            extra = sorted(locale_keys - baseline_keys)
            if missing:
                errors.append(f"{label} missing keys vs eng: {', '.join(missing[:8])}{'…' if len(missing) > 8 else ''}")
            if extra:
                errors.append(f"{label} extra keys vs eng: {', '.join(extra[:8])}{'…' if len(extra) > 8 else ''}")


def validate_telemetry_labels(errors: list[str]) -> None:
    """统计服务使用稳定模型 ID 与敌方枚举名，中文名必须和简中标题保持同步。"""
    if not TELEMETRY_LABELS.exists():
        errors.append(f"telemetry labels missing: {TELEMETRY_LABELS.relative_to(REPO_ROOT)}")
        return

    labels = json.loads(read(TELEMETRY_LABELS))
    rune_labels = labels.get("runes", {})
    monster_labels = labels.get("monsterHexes", {})
    zhs_relics = json.loads(read(LOCALIZATION / "zhs" / "relics.json"))
    registry_text = registry_source_text()

    for registration in extract_rune_registrations(registry_text):
        model_id = model_id_entry(str(registration["type"]))
        title_key = f"{model_id}.title"
        expected = zhs_relics.get(title_key)
        actual = rune_labels.get(model_id)
        if expected is None:
            errors.append(f"zhs relic title missing for telemetry rune: {title_key}")
        elif actual != expected:
            errors.append(f"telemetry rune label mismatch for {model_id}: expected {expected!r}, got {actual!r}")

    for registration in extract_monster_hex_registrations(registry_text):
        kind = str(registration["kind"])
        title_key = f"{model_id_entry(str(registration['type']))}.title"
        expected = zhs_relics.get(title_key)
        actual = monster_labels.get(kind)
        if expected is None:
            errors.append(f"zhs relic title missing for telemetry monster hex: {title_key}")
        elif actual != expected:
            errors.append(f"telemetry monster label mismatch for {kind}: expected {expected!r}, got {actual!r}")


def validate_official_name_references(errors: list[str]) -> None:
    """校对源码绑定的升级卡名和高亮引用；普通强调词不是原版模型名。"""
    snapshot = json.loads(read(OFFICIAL_ZHS_TITLES))
    tables = {path.stem: json.loads(read(path)) for path in (LOCALIZATION / "zhs").glob("*.json")}
    official = set(snapshot["cards"].values()) | set(snapshot["powers"].values())
    official.update(snapshot["other_official_terms"].values())
    custom = {value for entries in tables.values() for key, value in entries.items()
              if key.endswith(".title") and isinstance(value, str)}
    # 分类/公式/动作强调、组合术语及自有升级牌名；不得用这个表豁免错写的原版名称。
    emphasis = {
        "稀有", "罕见", "X", "减半", "翻倍", "诅咒", "手牌", "状态牌", "龙魂卡牌",
        "临时力量", "闪电充能球", "灼热攻击+1", "牌组", "附魔",
        "海克斯：", "属性锻造器：", "铁甲战士海克斯：", "静默猎手海克斯：",
        "储君海克斯：", "故障机器人海克斯：", "亡灵契约师海克斯：", "愚者海克斯：",
        # 0.113.0 原版描述里以金色高亮、但没有独立标题键的愚者术语
        # （见 static_hover_tips:ARROW.missing.description、LOST_LUTE_POWER、cards:HEAVY_SHOT/COVERING_FIRE 的“发射”）。
        "松脱", "旋律", "发射",
    }
    enchantment_titles = {value for key, value in snapshot["other_official_terms"].items() if key.startswith("enchantments:")}
    enchantment_titles.update(value for key, value in tables.get("enchantments", {}).items() if key.endswith(".title"))
    for table, entries in tables.items():
        for key, value in entries.items():
            if not isinstance(value, str) or key.endswith(".flavor"):
                continue
            for term in re.findall(r"\[gold\]([^\[\]{}]+)\[/gold\]", value):
                if term not in official | custom | emphasis:
                    errors.append(f"zhs/{table}.{key}: unrecognized model reference {term!r}; check official_zhs_titles.json or the custom glossary")
            # 原版遗物文案里附魔名用紫色：只能是官方或自有的附魔标题。
            for term in re.findall(r"\[purple\]([^\[\]{}]+)\[/purple\]", value):
                if term not in enchantment_titles:
                    errors.append(f"zhs/{table}.{key}: [purple] must name an enchantment, got {term!r}")
    relics = tables["relics"]
    for path in sorted((SRC / "Runes").glob("*.cs")):
        for rune, card in re.findall(r"\bclass\s+(\w+)\s*:\s*CardUpgradeRuneBase<(\w+)>", read(path)):
            expected_card = snapshot["cards"].get(model_id_entry(card))
            key = f"{model_id_entry(rune)}.title"
            if expected_card is None:
                errors.append(f"{path.relative_to(REPO_ROOT)}: official card title missing from snapshot for {card}")
            elif relics.get(key) != f"升级：{expected_card}":
                errors.append(f"zhs/relics.{key}: expected official card name 升级：{expected_card!s}, got {relics.get(key)!r}")


def localization_format(text: str) -> tuple[set[str], Counter, list[str]]:
    # 比较变量身份，不比较各语言的 plural/diff 等格式；嵌套格式里的变量也计入。
    variables = set(re.findall(r"(?<!\{)\{([A-Za-z_]\w*|\d+)(?=[:.}])", text))
    tags: Counter = Counter()
    stack: list[str] = []
    problems: list[str] = []
    for match in re.finditer(r"\[(/?)([A-Za-z][A-Za-z0-9_]*)(?:[= ][^\]]*)?\]", text):
        closing, name = match.groups()
        tags[closing + name] += 1
        if closing:
            if not stack or stack[-1] != name:
                problems.append(f"unexpected [/{name}]")
            else:
                stack.pop()
        else:
            stack.append(name)
    if stack:
        problems.append("unclosed " + ", ".join(stack))
    return variables, tags, problems


def validate_localization_format_parity(errors: list[str], root: Path | None = None, pack: str = MOD_ID) -> None:
    """九语逐键：占位符集合一致，BBCode 嵌套配平。"""
    root = LOCALIZATION if root is None else root
    languages = ("zhs", "eng", "esp", "spa", "jpn", "kor", "ptb", "rus", "tha")
    for language in languages:
        if not (root / language).is_dir():
            errors.append(localization_label(pack, f"required localization directory missing: {language}"))
    for baseline_path in sorted((root / "zhs").glob("*.json")):
        baseline = json.loads(read(baseline_path))
        for language in languages:
            path = root / language / baseline_path.name
            if not path.exists():
                errors.append(localization_label(pack, f"required localization file missing: {language}/{baseline_path.name}"))
                continue
            entries = json.loads(read(path))
            for key, value in entries.items():
                if not isinstance(value, str) or key not in baseline or not isinstance(baseline[key], str):
                    continue
                variables, tags, problems = localization_format(value)
                expected_variables, expected_tags, _ = localization_format(baseline[key])
                label = localization_label(pack, f"{language}/{baseline_path.name}:{key}")
                if problems:
                    errors.append(f"{label}: BBCode unbalanced: {'; '.join(problems)}")
                if variables != expected_variables:
                    errors.append(f"{label}: placeholders {sorted(variables)} != zhs {sorted(expected_variables)}")
                # 各语言高亮哪些词由译文决定，标签数量不要求与中文一致；只要求配平。


def strip_localization_markup(text: str) -> str:
    """去掉 SmartFormat 占位符（含嵌套的 {A:plural:{B}|c}）与 BBCode 标签，只留可见正文。"""
    previous = None
    while previous != text:
        previous = text
        text = re.sub(r"\{[^{}]*\}", " ", text)
    return re.sub(r"\[/?[A-Za-z][A-Za-z0-9_]*(?:[= ][^\]]*)?\]", " ", text)


def looks_untranslated(value: str, eng_value: object) -> bool:
    """非 eng 的值与 eng 逐字相同，且去掉占位符和标签后仍有足够的拉丁字母，视为疑似未翻译。"""
    if not isinstance(eng_value, str) or value != eng_value:
        return False
    return len(re.findall(r"[A-Za-z]", strip_localization_markup(value))) >= UNTRANSLATED_MIN_LATIN_LETTERS


def load_untranslated_allowlist(errors: list[str], path: Path | None = None) -> dict[tuple[str, str, str, str], str]:
    """白名单：{"entries": [{"pack", "language", "table", "key", "reason"}]}，理由必填。"""
    path = UNTRANSLATED_ALLOWLIST if path is None else path
    if not path.exists():
        return {}
    allowlist: dict[tuple[str, str, str, str], str] = {}
    for index, entry in enumerate(json.loads(read(path)).get("entries", [])):
        fields = tuple(str(entry.get(name, "")).strip() for name in ("pack", "language", "table", "key"))
        reason = str(entry.get("reason", "")).strip()
        if not all(fields) or not reason:
            errors.append(f"{path.name} entry {index} needs pack, language, table, key and reason")
            continue
        if fields in allowlist:
            errors.append(f"{path.name} duplicate entry: {'/'.join(fields)}")
        allowlist[fields] = reason
    return allowlist


def validate_untranslated_values(
    errors: list[str],
    root: Path | None = None,
    pack: str = MOD_ID,
    allowlist: dict[tuple[str, str, str, str], str] | None = None,
) -> None:
    """非 eng 语言的值与 eng 完全相同时报疑似漏译；品牌名、专有名词等写进白名单并说明理由。
    过期的白名单条目（键已删除或已不再与 eng 相同）同样报错，避免白名单悄悄失效。"""
    root = LOCALIZATION if root is None else root
    allowlist = {} if allowlist is None else allowlist
    eng_dir = root / "eng"
    if not eng_dir.is_dir():
        return
    used: set[tuple[str, str, str, str]] = set()
    for eng_path in sorted(eng_dir.glob("*.json")):
        eng_entries = json.loads(read(eng_path))
        for locale_dir in sorted(path for path in root.iterdir() if path.is_dir() and path.name != "eng"):
            locale_path = locale_dir / eng_path.name
            if not locale_path.exists():
                continue
            for key, value in json.loads(read(locale_path)).items():
                if not isinstance(value, str) or not looks_untranslated(value, eng_entries.get(key)):
                    continue
                allow_key = (pack, locale_dir.name, eng_path.name, key)
                if allow_key in allowlist:
                    used.add(allow_key)
                    continue
                errors.append(
                    f"{localization_label(pack, f'{locale_dir.name}/{eng_path.name}:{key}')}: "
                    f"identical to eng {value[:60]!r}; translate it or add it to {UNTRANSLATED_ALLOWLIST.name} with a reason"
                )
    for stale in sorted(key for key in allowlist if key[0] == pack and key not in used):
        errors.append(f"{UNTRANSLATED_ALLOWLIST.name} stale entry (no longer identical to eng): {'/'.join(stale)}")


def main() -> int:
    errors: list[str] = []
    warnings: list[str] = []
    validate_monster_hex_registry(errors, warnings)
    validate_relic_registry(errors)
    validate_rune_file_layout(errors)
    validate_enemy_hex_effect_layout(errors)
    validate_icon_assets(errors, warnings)
    validate_telemetry_labels(errors)
    validate_official_name_references(errors)
    allowlist = load_untranslated_allowlist(errors)
    for pack, root in localization_roots():
        validate_localization_key_parity(errors, root, pack)
        validate_localization_format_parity(errors, root, pack)
        validate_untranslated_values(errors, root, pack, allowlist)

    if errors:
        print("Hextech content validation failed:")
        for error in errors:
            print(f"- {error}")
        return 1

    if warnings:
        print("Hextech content validation warnings:")
        for warning in warnings:
            print(f"- {warning}")
    print("Hextech content validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
