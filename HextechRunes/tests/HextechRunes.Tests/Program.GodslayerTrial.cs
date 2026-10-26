using System.Text.Json;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static readonly string[] AncientsLocLanguages = [ "zhs", "eng", "esp", "jpn", "kor", "ptb", "rus", "spa", "tha" ];

	[HextechTest]
	private static void GodslayerRuneStaysOutOfPoolsConfigAndCollection()
	{
		PlayerRuneMetadataCatalog metadata = HextechContentRegistry.PlayerRuneMetadata;
		Type rune = typeof(GodslayerRune);
		Expect(metadata.HasFlag(rune, PlayerRuneFlags.Hidden), "registered as hidden");
		Expect(metadata.TryGetRarity(rune, out HextechRarityTier rarity) && rarity == HextechRarityTier.Prismatic, "keeps its prismatic rarity");
		Expect(!metadata.IsConfigurable(rune), "not configurable");
		Expect(!metadata.IsVisible(rune), "not visible in the compendium");
		Expect(!HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(HextechRarityTier.Prismatic).Contains(rune), "not in the prismatic pool");
	}

	[HextechTest]
	private static void GodslayerTrialStaysOutOfSnapshotJsonAndShareCodes()
	{
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		Expect(!defaults.GodslayerTrial, "off by default");
		Expect(!JsonSerializer.Serialize(defaults).Contains(nameof(HextechRunConfigurationSnapshot.GodslayerTrial)), "ordinary save JSON omits the trial");

		HextechRunConfigurationSnapshot trial = defaults with { ModEnabled = false, GodslayerTrial = true };
		Expect(HextechRuneConfiguration.NormalizeSnapshot(trial).GodslayerTrial, "normalization keeps the trial");
		Expect(JsonSerializer.Deserialize<HextechRunConfigurationSnapshot>(JsonSerializer.Serialize(trial))!.GodslayerTrial, "trial save JSON round-trips");
		Expect(!HextechConfigShareCodec.TryParse(HextechConfigShareCodec.Export(trial))!.Snapshot.GodslayerTrial, "share codes never carry the trial");
	}

	// 每套台词从 0 连续编号，每行只有一个说话人，最后一行带按钮文字；各语言键集与简中一致。
	[HextechTest]
	private static void ArchitectGodslayerDialogueKeysMatchAcrossLanguages()
	{
		string assetsRoot = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "..", "..", "assets"));
		HashSet<string>? reference = null;
		foreach (string language in AncientsLocLanguages)
		{
			string path = Path.Combine(assetsRoot, "localization", language, "ancients.json");
			Dictionary<string, string> table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
				?? throw new InvalidOperationException($"{path} is not a string table");
			HashSet<string> keys = table.Keys.Where(static key => key.StartsWith("HEXTECH_ARCHITECT.", StringComparison.Ordinal)).ToHashSet();
			Expect(keys.All(key => !string.IsNullOrWhiteSpace(table[key])), $"{language} architect lines are not blank");
			if (reference == null)
			{
				reference = keys;
				CheckArchitectDialogueSets(keys);
				continue;
			}

			Expect(reference.SetEquals(keys), $"{language}/ancients.json architect keys match zhs");
		}
	}

	[HextechTest]
	private static void GodslayerFinaleLinesExistInEveryLanguage()
	{
		string assetsRoot = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "..", "..", "assets"));
		foreach (string language in AncientsLocLanguages)
		{
			string path = Path.Combine(assetsRoot, "localization", language, "relic_collection.json");
			Dictionary<string, string> table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
				?? throw new InvalidOperationException($"{path} is not a string table");
			for (int i = 1; i <= HextechGodslayerFinale.LineCount; i++)
			{
				string key = $"HEXTECH_GODSLAYER_FINALE_{i}";
				Expect(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{language} defines {key}");
				Equal(CountOccurrences(text!, "[jitter]"), CountOccurrences(text!, "[/jitter]"), $"{language} {key} jitter tags balance");
			}

			Expect(!table.ContainsKey($"HEXTECH_GODSLAYER_FINALE_{HextechGodslayerFinale.LineCount + 1}"), $"{language} has no stray finale line");

			string relicsPath = Path.Combine(assetsRoot, "localization", language, "relics.json");
			Dictionary<string, string> relics = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(relicsPath))
				?? throw new InvalidOperationException($"{relicsPath} is not a string table");
			Expect(relics.TryGetValue("GODSLAYER_RUNE.rememberedDescription", out string? remembered) && !string.IsNullOrWhiteSpace(remembered), $"{language} defines the remembered description");
		}
	}

	private static int CountOccurrences(string text, string token)
	{
		int count = 0;
		for (int index = text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	private static void CheckArchitectDialogueSets(HashSet<string> keys)
	{
		const string prefix = "HEXTECH_ARCHITECT.talk.";
		string[] sets = keys.Select(key => key[prefix.Length..].Split('.')[0]).Distinct().ToArray();
		Expect(sets.Contains("GENERIC"), "generic fallback set exists");
		foreach (string set in sets)
		{
			int count = keys.Where(key => key.StartsWith($"{prefix}{set}.", StringComparison.Ordinal))
				.Select(key => int.Parse(key[(prefix.Length + set.Length + 1)..].Split('.')[0]))
				.Max() + 1;
			for (int i = 0; i < count; i++)
			{
				string line = $"{prefix}{set}.{i}";
				Expect(keys.Contains($"{line}.ancient") ^ keys.Contains($"{line}.char"), $"{line} has exactly one speaker");
			}

			Expect(keys.Contains($"{prefix}{set}.{count - 1}.next"), $"{set} last line names the final button");
		}
	}
}
