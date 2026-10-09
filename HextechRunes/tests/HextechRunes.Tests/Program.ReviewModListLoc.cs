using System.Text.Json;
using HextechRunes;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static readonly string[] ModListLocLanguages = ["zhs", "eng", "jpn", "kor", "esp", "spa", "ptb", "rus", "tha"];

	/// <summary>模组列表只替换本体与拓展包两个 mod id 的文案，其他模组不查键。</summary>
	[HextechTest]
	private static void ModListLocMapsOnlyOwnModIds()
	{
		Expect(HextechModListLocalizationHooks.TryGetKeys("HextechRunes", out string? mainName, out string? mainDescription), "main mod id should map to keys");
		Equal("HEXTECH_MOD_NAME", mainName, "main name key");
		Equal("HEXTECH_MOD_DESCRIPTION", mainDescription, "main description key");

		Expect(HextechModListLocalizationHooks.TryGetKeys("HextechRunesSponsorPack", out string? sponsorName, out string? sponsorDescription), "sponsor pack id should map to keys");
		Equal("HEXTECH_SPONSOR_MOD_NAME", sponsorName, "sponsor name key");
		Equal("HEXTECH_SPONSOR_MOD_DESCRIPTION", sponsorDescription, "sponsor description key");

		Expect(!HextechModListLocalizationHooks.TryGetKeys("BaseLib", out _, out _), "third-party mods must stay untouched");
		Expect(!HextechModListLocalizationHooks.TryGetKeys(null, out _, out _), "rows without manifest id must stay untouched");
		Expect(!HextechModListLocalizationHooks.TryGetKeys("hextechrunes", out _, out _), "mod id match is ordinal");
	}

	/// <summary>标题：有译文才替换；缺键、空译文、标签已被他人改写、非本模组时保持原版文本。</summary>
	[HextechTest]
	private static void ModListLocTitleFallsBackToManifestText()
	{
		Dictionary<string, string> table = new(StringComparer.Ordinal)
		{
			["HEXTECH_MOD_NAME"] = "ARAM: Mayhem",
			["HEXTECH_SPONSOR_MOD_NAME"] = " ",
		};
		string? Lookup(string key) => table.TryGetValue(key, out string? value) ? value : null;
		const string Vanilla = "ARAM: Mayhem / 海克斯大乱斗";

		Equal("ARAM: Mayhem", HextechModListLocalizationHooks.ResolveTitle("HextechRunes", Vanilla, Vanilla, Lookup), "localized main title");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveTitle("HextechRunesSponsorPack", Vanilla, Vanilla, Lookup), "blank translation keeps manifest name");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveTitle("HextechRunes", Vanilla, Vanilla, static _ => null), "missing key keeps manifest name, never the key");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveTitle("HextechRunes", "Renamed by another mod", Vanilla, Lookup), "another mod's rewrite wins");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveTitle("OtherMod", Vanilla, Vanilla, Lookup), "other mods are not localized");
	}

	/// <summary>详情：只把 manifest 介绍原文换成译文，作者/版本行与红色加载错误原样保留。</summary>
	[HextechTest]
	private static void ModListLocDescriptionReplacesOnlyManifestDescription()
	{
		const string ManifestDescription = "Line one.\n第二行。";
		string vanilla = "[gold]Author[/gold]: Natsuki\n[gold]Version[/gold]: 0.9.6\n\n" + ManifestDescription + "\n\n[red]Some load error[/red]\n";
		string? Lookup(string key) => key == "HEXTECH_MOD_DESCRIPTION" ? "本地化介绍" : null;

		Equal(
			"[gold]Author[/gold]: Natsuki\n[gold]Version[/gold]: 0.9.6\n\n本地化介绍\n\n[red]Some load error[/red]\n",
			HextechModListLocalizationHooks.ResolveDescription("HextechRunes", vanilla, ManifestDescription, Lookup),
			"description body replaced in place");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveDescription("HextechRunesSponsorPack", vanilla, ManifestDescription, Lookup), "missing sponsor key keeps manifest description");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveDescription("HextechRunes", vanilla, null, Lookup), "manifest without description keeps vanilla text");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveDescription("HextechRunes", "rewritten by another mod", ManifestDescription, Lookup), "rewritten panel is left alone");
		Equal<string?>(null, HextechModListLocalizationHooks.ResolveDescription("OtherMod", vanilla, ManifestDescription, Lookup), "other mods are not localized");
	}

	/// <summary>
	/// 键落在原版会合并的 main_menu_ui 表：本体键在本体九语言文件，拓展包键在拓展包九语言文件，且都非空；
	/// manifest 的 id 与补丁认的 id 一致（manifest 名称改动不影响识别）。
	/// </summary>
	[HextechTest]
	private static void ModListLocKeysExistInEveryLanguage()
	{
		string repoRoot = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "..", "..", ".."));
		CheckModListLocFiles(Path.Combine(repoRoot, "HextechRunes", "assets"), "HextechRunes.json", "HextechRunes");
		CheckModListLocFiles(Path.Combine(repoRoot, "HextechRunesSponsorPack", "assets"), "HextechRunesSponsorPack.json", "HextechRunesSponsorPack");
	}

	private static void CheckModListLocFiles(string assetsRoot, string manifestFile, string expectedId)
	{
		using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assetsRoot, manifestFile)));
		Equal(expectedId, manifest.RootElement.GetProperty("id").GetString(), $"{manifestFile} id");
		Expect(HextechModListLocalizationHooks.TryGetKeys(expectedId, out string? nameKey, out string? descriptionKey), $"{expectedId} should map to keys");
		foreach (string language in ModListLocLanguages)
		{
			string path = Path.Combine(assetsRoot, "localization", language, "main_menu_ui.json");
			Expect(File.Exists(path), $"{path} should exist");
			Dictionary<string, string> table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
				?? throw new InvalidOperationException($"{path} is not a string table");
			foreach (string key in new[] { nameKey, descriptionKey })
			{
				Expect(table.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), $"{language}/main_menu_ui.json of {expectedId} should define {key}");
			}
		}
	}
}
