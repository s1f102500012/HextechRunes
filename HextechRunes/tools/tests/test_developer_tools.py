#!/usr/bin/env python3
"""只检查维护工具的关键边界；使用临时文件，不触碰游戏或现有发行包。"""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import hextech_dev as dev
import package_release as packaging
import sync_content_txt as content
import validate_hextech_content as validation


class DeveloperToolTests(unittest.TestCase):
    def test_localization_guards_cover_plain_names_missing_languages_and_formatting(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "src/Runes").mkdir(parents=True)
            snapshot = root / "titles.json"
            snapshot.write_text(json.dumps({"cards": {}, "powers": {"STRENGTH_POWER": "力量"},
                "other_official_terms": {}}, ensure_ascii=False))
            text = {"SLAP_RUNE.description": "获得[blue]{Amount}[/blue]点[gold]力量[/gold]。"}
            for language in ("zhs", "eng", "esp", "spa", "jpn", "kor", "ptb", "rus", "tha"):
                directory = root / "localization" / language
                directory.mkdir(parents=True)
                (directory / "relics.json").write_text(json.dumps(text, ensure_ascii=False))
            with patch.object(validation, "SRC", root / "src"), \
                 patch.object(validation, "LOCALIZATION", root / "localization"), \
                 patch.object(validation, "OFFICIAL_ZHS_TITLES", snapshot):
                errors = []
                validation.validate_official_name_references(errors)
                validation.validate_localization_format_parity(errors)
                self.assertEqual(errors, [])
                zhs = root / "localization/zhs/relics.json"
                zhs.write_text(zhs.read_text().replace("力量", "力靓"))
                validation.validate_official_name_references(errors)
                self.assertTrue(any("力靓" in error for error in errors))
                (root / "localization/tha/relics.json").unlink()
                (root / "localization/tha").rmdir()
                errors = []
                validation.validate_localization_format_parity(errors)
                self.assertTrue(any("directory missing: tha" in error for error in errors))
            self.assertEqual(validation.localization_format("{Cards:plural:card|cards}")[0], {"Cards"})
            self.assertTrue(validation.localization_format("[blue][gold]x[/blue][/gold]")[2])

    def test_content_txt_resolves_model_variables_without_crossing_class_boundaries(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "Cards.cs").write_text('''
class FirstCard : CardModel
{
    const string Name = "Hits";
    const int BaseHits = 3;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [new CardsVar(2), new EnergyVar(1), new PowerVar<BufferPower>(1m), new ForgeVar("ForgeAmount", 3),
     new DynamicVar(Name, BaseHits * 2)];
    string Description = "{ ignored brace }";
}
class SecondCard : CardModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(99)];
}
''', encoding="utf-8")
            models = content.class_sources((root,))
            values = content.canonical_values(models["firstcard"])
            rendered = content.Truth.render_placeholders("FirstCard",
                "抽[blue]{Cards}[/blue]张，获得{Energy:energyIcons()}、{BufferPower}缓冲，{Hits:diff()}次。", values)
            self.assertEqual(rendered, "抽2张，获得1点能量、1缓冲，6次。")
            self.assertEqual(values["ForgeAmount"], "3")
            self.assertEqual(content.canonical_values(models["secondcard"])["Cards"], "99")
            with self.assertRaisesRegex(ValueError, "TXT 无法解析"):
                content.Truth.render_placeholders("FirstCard", "{Unknown}", values)
            integer_division = content.canonical_values('''
                const int Threshold = 7 / 2;
                protected override IEnumerable<DynamicVar> CanonicalVars =>
                [new DynamicVar("Threshold", Threshold)];
            ''')
            with self.assertRaisesRegex(ValueError, "TXT 无法解析"):
                content.Truth.render_placeholders("IntegerDivisionCard", "{Threshold}", integer_division)

    def test_content_txt_strips_rich_text_and_preserves_literal_chinese_brackets(self):
        self.assertEqual(content.strip_markup(
            '[gold]金币[/gold][color=#fff]10[/color]<br/>[font_size=20]点[/font_size]\n[说明]'),
            "金币10点[说明]")

    def test_content_summary_keeps_manual_text_until_anchor_is_accepted(self):
        sections = {name: [] for name in (*content.TAG_SECTION_ORDER, "怪物", "属性锻造器", "卡牌", "事件遗物")}
        section = content.TAG_SECTION_ORDER[0]
        sections[section] = [{"rarity": "白银", "title": "示例", "disabled": False,
                              "desc": "解析后的新说明", "suffix": ""}]
        original = section + "\n白银：示例：保留手写说明\n"
        with patch.object(content, "summary_truth", return_value=sections):
            kept = content.sync_summary(None, original, [], False, set())
            accepted = content.sync_summary(None, original, [], False, {f"{section}:白银:示例"})
        self.assertIn("白银：示例：保留手写说明", kept)
        self.assertIn("白银：示例：解析后的新说明", accepted)

    def test_content_summary_layout_groups_rarities_and_keeps_manual_lines(self):
        sections = {name: [] for name in (*content.TAG_SECTION_ORDER, "怪物", "属性锻造器", "卡牌", "事件遗物")}
        sections["怪物"] = [
            {"rarity": "白银", "title": "甲", "disabled": False, "desc": "新甲", "suffix": ""},
            {"rarity": "棱彩", "title": "乙", "disabled": True, "desc": "新乙", "suffix": ""},
            {"rarity": "白银", "title": "丙", "disabled": False, "desc": "新丙", "suffix": ""},
        ]
        original = (
            "怪物：\n说明行\n#棱彩：乙：手写乙\n\n\n白银：丙：手写丙\n白银：甲：手写甲\n"
            "特定规则：\n\n规则一\n\n\n规则二\n"
        )
        with patch.object(content, "summary_truth", return_value=sections):
            text = content.sync_summary(None, original, [], False, set())
        self.assertEqual(
            "[敌方海克斯]\n说明行\n\n白银：甲：手写甲\n白银：丙：手写丙\n\n#棱彩：乙：手写乙\n\n"
            "[特定规则]\n规则一\n\n规则二\n",
            text,
        )

    def test_localization_copy_preserves_unrelated_values_and_formatting(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = root / "assets/localization/zhs/relics.json"
            path.parent.mkdir(parents=True)
            text = '{\n  "from": "[blue]{Stacks}[/blue] \\"引号\\"",\n  "to" : "旧",\n  "keep": "旧"\n}\n'
            path.write_text(text, encoding="utf-8")
            [(changed, before, after)] = dev.localization_changes(root, "relics", "from", "to", None)
            self.assertEqual(changed, path)
            self.assertEqual(path.read_text(), text)  # 默认预览不写入
            self.assertIn('  "keep": "旧"', after)
            self.assertIn('  "to" : ', after)
            self.assertEqual(json.loads(after)["to"], json.loads(before)["from"])
            path.write_text(after, encoding="utf-8")
            self.assertEqual(dev.localization_changes(root, "relics", "from", "to", None), [])

    def test_missing_key_in_later_locale_does_not_partially_write(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            originals = {}
            for locale, data in (("eng", {"from": "new", "to": "old"}), ("zhs", {"from": "新"})):
                path = root / f"assets/localization/{locale}/relics.json"
                path.parent.mkdir(parents=True)
                path.write_text(json.dumps(data, indent=2), encoding="utf-8")
                originals[path] = path.read_bytes()
            args = argparse.Namespace(table="relics", source="from", destination="to",
                                      locale=None, apply=True, check=False)
            with patch.object(dev, "ROOT", root), self.assertRaisesRegex(ValueError, "to"):
                dev.sync_localization(args)
            self.assertTrue(all(path.read_bytes() == content for path, content in originals.items()))

    def test_registered_tests_are_methods_marked_with_hextech_test(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            tests = root / "tests/HextechRunes.Tests"
            tests.mkdir(parents=True)
            (tests / "Program.A.cs").write_text(
                "\t[HextechTest]\n\tprivate static void Zeta()\n\t{\n\t}\n\n"
                "\tprivate static void Helper()\n\t{\n\t}\n", encoding="utf-8")
            (tests / "Program.B.cs").write_text(
                "\t/// <summary>doc</summary>\n\t[HextechTest]\n\tstatic void Alpha() { }\n", encoding="utf-8")
            with patch.object(dev, "ROOT", root):
                self.assertEqual(dev.registered_tests(), ["Alpha", "Zeta"])

    def test_focused_runner_rejects_typo_and_stops_after_build_failure(self):
        # 测试名用虚构值并 patch 掉静态收集，真实测试改名或删除不会让这里失败。
        known = ["FictionalRegisteredTest"]
        args = argparse.Namespace(list=False, name=["MisspelledTest"], target="0.111.0", run=True, match="")
        with patch.object(dev, "registered_tests", return_value=known), \
             patch.object(dev.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "未知测试名"):
                dev.focused_tests(args)
            run.assert_not_called()
        args.name = ["FictionalRegisteredTest"]
        with patch.object(dev, "registered_tests", return_value=known), \
             patch.object(dev.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "dotnet")) as run:
            with patch.object(Path, "is_file", return_value=True), redirect_stdout(io.StringIO()):
                with self.assertRaises(subprocess.CalledProcessError):
                    dev.focused_tests(args)
            self.assertEqual(run.call_count, 1)
            command = run.call_args.args[0]
            self.assertTrue(command[1].endswith("run_tests.sh"))
            self.assertEqual(command[2:5], ["--target", "0.111.0", "FictionalRegisteredTest"])

    def make_bundle(self, root):
        dist = root / "dist"
        dist.mkdir()
        (dist / "HextechRunes.json").write_text(json.dumps({"id": "HextechRunes", "has_dll": True,
            "has_pck": True, "min_game_version": "0.107.1"}))
        (dist / "HextechRunes.dll").write_bytes(b"test loader")
        (dist / "HextechRunes.pck").write_bytes(b"test pck")
        variants = []
        for target in ("0.107.1", "0.110.0", "0.111.0"):
            sub = dist / "lib" / target
            sub.mkdir(parents=True)
            payload = target.encode()
            (sub / "HextechRunes.dll").write_bytes(payload)
            (sub / "compat-target.txt").write_text(target)
            variants.append({"compatTarget": target, "directory": "lib/" + target,
                             "assembly": "HextechRunes.dll", "sha256": hashlib.sha256(payload).hexdigest()})
        (dist / packaging.MANIFEST).write_text(json.dumps({"schema": 1, "variants": variants}))
        (dist / "更新日志.txt").write_text("不要收入包")
        (dist / ".DS_Store").write_bytes(b"metadata")
        return dist

    def test_package_contains_all_variants_and_excludes_notes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            dist = self.make_bundle(root)
            output = root / "release.zip"
            packaging.write_package(dist, output)
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(len(archive.namelist()), 10)
                for target in ("0.107.1", "0.110.0", "0.111.0"):
                    self.assertEqual(archive.read(f"HextechRunes/lib/{target}/HextechRunes.dll"), target.encode())
                self.assertFalse(any("更新日志" in name or ".DS_Store" in name for name in archive.namelist()))

    def test_corrupt_variant_preserves_previous_zip(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            dist = self.make_bundle(root)
            output = root / "release.zip"
            output.write_bytes(b"previous release")
            (dist / "lib/0.111.0/HextechRunes.dll").write_bytes(b"corrupt")
            with self.assertRaises(subprocess.CalledProcessError):
                packaging.write_package(dist, output)
            self.assertEqual(output.read_bytes(), b"previous release")


if __name__ == "__main__":
    unittest.main()
