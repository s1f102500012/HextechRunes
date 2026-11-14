using System.Text.Json;
using System.Text.RegularExpressions;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 愚者专属海克斯的共享部分：三个目标都要成立（注册身份、可用性闸门、界面隐藏、文案、配置编码）。
// 需要 0.113.0 类型的测试放 Program.FoolRunes.V113.cs；后续各组的战斗测试放 Program.FoolRunes.<组名>.V113.cs。
internal static partial class Program
{
	// 注册表末尾的愚者海克斯，按注册表顺序。
	private static readonly (Type Type, HextechRarityTier Rarity, string TagKey)[] FoolRuneRegistrations =
	[
		(typeof(FullQuiverRune), HextechRarityTier.Silver, "ARROW"),
		(typeof(SureShotRune), HextechRarityTier.Silver, "ARROW"),
		(typeof(PluckRune), HextechRarityTier.Silver, "ARROW"),
		(typeof(LoyaltyRune), HextechRarityTier.Gold, "ARROW"),
		(typeof(CloudPiercerRune), HextechRarityTier.Gold, "ARROW"),
		(typeof(DeadeyeRune), HextechRarityTier.Gold, "ARROW"),
		(typeof(PartingShotRune), HextechRarityTier.Prismatic, "ARROW"),
		(typeof(OpeningNoteRune), HextechRarityTier.Silver, "MELODY"),
		(typeof(SingOutRune), HextechRarityTier.Gold, "MELODY"),
		(typeof(OvertoneRune), HextechRarityTier.Gold, "MELODY"),
		(typeof(TrioRune), HextechRarityTier.Prismatic, "MELODY"),
		(typeof(RibbitRune), HextechRarityTier.Gold, "COMPREHENSIVE"),
		(typeof(LimberUpRune), HextechRarityTier.Silver, "SURVIVAL"),
		(typeof(FlexibilityRune), HextechRarityTier.Gold, "MULTIPLAYER"),
		(typeof(PreludeRune), HextechRarityTier.Gold, "COMPREHENSIVE"),
		(typeof(BittersweetRune), HextechRarityTier.Silver, "EMOTION"),
		(typeof(TearsToLaughterRune), HextechRarityTier.Gold, "EMOTION"),
		(typeof(MixedFeelingsRune), HextechRarityTier.Prismatic, "EMOTION"),
		(typeof(CrystalBallRune), HextechRarityTier.Gold, "TAROT"),
		(typeof(TarotClothRune), HextechRarityTier.Prismatic, "TAROT"),
		(typeof(MythicFormUpgradeRune), HextechRarityTier.Prismatic, "TAROT")
	];

	// 不按注册表顺序连续编号的 characterOrder：0.10.0 之后新增的条目注册在同流派条目旁边，图鉴里排在愚者池末尾。
	// 其余条目按注册表顺序从 1 起连续编号。
	private static readonly Dictionary<Type, int> FoolRuneCharacterOrderOverrides = new()
	{
		[typeof(PluckRune)] = 19,
		[typeof(RibbitRune)] = 20,
		[typeof(PreludeRune)] = 21
	};

	// 默认禁用的愚者海克斯（仍可配置）。
	private static readonly HashSet<Type> FoolRunesDisabledByDefault = [typeof(SingOutRune)];

	private static readonly string[] FoolRuneLanguages = ["zhs", "eng", "jpn", "kor", "esp", "spa", "ptb", "rus", "tha"];

	/// <summary>
	/// 三个目标的注册内容一致：愚者海克斯追加在注册表末尾（不挪动已有条目的注册顺序），
	/// 都在愚者池、都可配置（配置 ID 集合与联机禁用位图里有它们），稀有度、标签、图鉴顺序与默认开关按规格。
	/// </summary>
	[HextechTest]
	private static void FoolRunesAreRegisteredAtRegistryTailInFoolPool()
	{
		IReadOnlyList<PlayerRuneRegistration> registrations = HextechPlayerRuneRegistry.Registrations;
		PlayerRuneRegistration[] tail = registrations.Skip(registrations.Count - FoolRuneRegistrations.Length).ToArray();
		SequenceEqual(FoolRuneRegistrations.Select(static entry => entry.Type), tail.Select(static entry => entry.Type), "Fool runes are the registry tail");
		Expect(registrations[registrations.Count - FoolRuneRegistrations.Length - 1].Type == typeof(GodslayerRune),
			"existing registrations keep their order; Fool runes are appended after GodslayerRune");
		IReadOnlySet<ModelId> configurable = HextechCatalog.GetConfigurablePlayerRuneIds();
		Dictionary<Type, int> expectedOrders = ExpectedFoolRuneCharacterOrders();
		for (int i = 0; i < tail.Length; i++)
		{
			(Type type, HextechRarityTier rarity, string tagKey) = FoolRuneRegistrations[i];
			Equal(PlayerRuneCharacterPool.Fool, tail[i].CharacterPool, $"{type.Name} pool");
			Equal(expectedOrders[type], tail[i].CharacterOrder, $"{type.Name} character order");
			Equal(rarity, tail[i].Rarity, $"{type.Name} rarity");
			Equal(tagKey, tail[i].TagKey, $"{type.Name} tag");
			Equal(FoolRunesDisabledByDefault.Contains(type) ? PlayerRuneFlags.Disabled : PlayerRuneFlags.None, tail[i].Flags, $"{type.Name} flags");
			Expect(configurable.Contains(ModelDb.GetId(type)), $"{type.Name} is configurable on every target");
		}

		Equal(tail.Length, tail.Select(static entry => entry.CharacterOrder).Distinct().Count(), "Fool character orders are unique");
		SequenceEqual(
			FoolRuneRegistrations.Select(static entry => entry.Type).OrderBy(type => expectedOrders[type]),
			HextechContentRegistry.PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Fool],
			"Fool pool lists exactly the Fool runes, by character order");
		Equal(5, (int)PlayerRuneCharacterPool.Fool, "Fool is appended after the five existing pools");
	}

	/// <summary>
	/// 没有愚者的目标（0.107.1 / 0.111.0）：愚者池对应不到角色，界面不列出，任何玩家都拿不到；0.113.0 上界面照常列出。
	/// 非愚者玩家在所有目标上都拿不到。
	/// </summary>
	[HextechTest]
	private static void FoolRunesAreHiddenAndUnavailableWithoutFoolCharacter()
	{
		bool gameHasFool = typeof(Ironclad).Assembly.GetType("MegaCrit.Sts2.Core.Models.Characters.Fool") != null;
		Equal(ModInfo.TargetGameVersion == "0.113.0", gameHasFool, "only the 0.113.0 game assembly has the Fool");
		Equal(gameHasFool, HextechFoolCompat.IsFoolCharacterAvailable, "compat detects the Fool");
		Equal(gameHasFool, HextechPlayerContextHelper.IsCharacterPoolPresentInGame(PlayerRuneCharacterPool.Fool), "Fool pool presence");
		Equal(gameHasFool, HextechPlayerContextHelper.GetCharacterId(PlayerRuneCharacterPool.Fool) != null, "Fool pool character id");
		Expect(HextechCatalog.IsPlayerRuneTypeShownInUi(typeof(TauntRune)), "existing character pools stay visible");
		Expect(HextechCatalog.IsPlayerRuneTypeShownInUi(typeof(SlapRune)), "generic runes stay visible");

		Player ironclad = CreateCharacterTestPlayer(typeof(Ironclad));
		Expect(!HextechFoolCompat.IsFoolPlayer(ironclad), "Ironclad is not the Fool");
		Expect(!HextechPlayerContextHelper.TryGetRuneCharacterPool(ironclad, out PlayerRuneCharacterPool pool) || pool == PlayerRuneCharacterPool.Ironclad,
			"Ironclad still maps to its own pool");
		foreach ((Type type, _, _) in FoolRuneRegistrations)
		{
			Equal(gameHasFool, HextechCatalog.IsPlayerRuneTypeShownInUi(type), $"{type.Name} UI visibility");
			HextechRelicBase rune = (HextechRelicBase)Activator.CreateInstance(type)!;
			Expect(!rune.IsAvailableForPlayer(ironclad), $"{type.Name} is not available to Ironclad");
		}
	}

	/// <summary>
	/// 每个愚者海克斯 × 九语言 × title/description/flavor 齐全；描述里的占位符都是该符文声明的 DynamicVar；
	/// 愚者池的来源标签、图鉴分组与四个新标签九语言齐全。
	/// </summary>
	[HextechTest]
	private static void FoolRuneLocalizationCoversNineLanguagesAndDeclaredVars()
	{
		string root = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "..", "..", "assets", "localization"));
		Regex placeholder = new(@"(?<!\{)\{([A-Za-z_]\w*)(?=[:.}])");
		foreach (string language in FoolRuneLanguages)
		{
			Dictionary<string, string> relics = ReadLocTable(Path.Combine(root, language, "relics.json"));
			Dictionary<string, string> collection = ReadLocTable(Path.Combine(root, language, "relic_collection.json"));
			foreach (string key in new[] { "HEXTECH_POOL.FOOL", "HEXTECH_CHARACTER.FOOL", "HEXTECH_TAG.ARROW", "HEXTECH_TAG.MELODY", "HEXTECH_TAG.EMOTION", "HEXTECH_TAG.TAROT" })
			{
				Expect(collection.TryGetValue(key, out string? text) && text.Length > 0, $"{language}/relic_collection.json has {key}");
			}

			foreach ((Type type, _, _) in FoolRuneRegistrations)
			{
				string entry = ModelDb.GetId(type).Entry;
				foreach (string field in new[] { "title", "description", "flavor" })
				{
					Expect(relics.TryGetValue($"{entry}.{field}", out string? text) && text.Length > 0, $"{language}: {entry}.{field}");
				}

				HextechRelicBase rune = CreateMutableTestRune(type);
				HashSet<string> declared = rune.DynamicVars.Keys.ToHashSet(StringComparer.Ordinal);
				foreach (Match match in placeholder.Matches(relics[$"{entry}.description"]))
				{
					Expect(declared.Contains(match.Groups[1].Value), $"{language}: {entry}.description uses undeclared {{{match.Groups[1].Value}}}");
				}
			}
		}
	}

	/// <summary>
	/// 新增条目不改变已有配置的含义：分享码与配置都按 ID 字符串存禁用集，旧 ID 原样往返、愚者条目除登记为默认禁用的以外默认启用；
	/// 联机禁用位图按可配置 ID 的字典序编码（只在同版本两端之间传输），新条目自然落位，含愚者 ID 的禁用集完整往返。
	/// </summary>
	[HextechTest]
	private static void FoolRunesKeepConfigShareCodeAndBitmapMeaning()
	{
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		foreach ((Type type, _, _) in FoolRuneRegistrations)
		{
			Equal(FoolRunesDisabledByDefault.Contains(type), defaults.DisabledPlayerRuneIds.Contains(ModelDb.GetId(type).Entry), $"{type.Name} default disabled state");
		}

		string[] disabled = [ModelDb.GetId<SlapRune>().Entry, ModelDb.GetId<TauntRune>().Entry, ModelDb.GetId<TarotClothRune>().Entry];
		HextechRunConfigurationSnapshot snapshot = defaults with { DisabledPlayerRuneIds = disabled.ToHashSet(StringComparer.Ordinal) };
		HextechConfigShareCodec.ImportPreview preview = HextechConfigShareCodec.TryParse(HextechConfigShareCodec.Export(snapshot))
			?? throw new InvalidOperationException("share code with a Fool rune should decode");
		SequenceEqual(disabled.Order(StringComparer.Ordinal), preview.Snapshot.DisabledPlayerRuneIds.Order(StringComparer.Ordinal), "share code keeps disabled ids");

		PlayerChoiceResult roll = HextechChoiceCodec.CreateActRoll(0, HextechRarityTier.Gold, null, false,
			snapshot.EnemyHexCountsByAct, snapshot.DisabledPlayerRuneIds, snapshot);
		Expect(HextechChoiceCodec.TryDecodeActRoll(roll, 0, out _, out _, out _, out _, out HashSet<string> decoded), "act roll decodes");
		SequenceEqual(disabled.Order(StringComparer.Ordinal), decoded.Order(StringComparer.Ordinal), "bitmap keeps existing and Fool disabled ids");
	}

	private static Dictionary<Type, int> ExpectedFoolRuneCharacterOrders()
	{
		Dictionary<Type, int> orders = new();
		int next = 1;
		foreach ((Type type, _, _) in FoolRuneRegistrations)
		{
			orders[type] = FoolRuneCharacterOrderOverrides.TryGetValue(type, out int order) ? order : next++;
		}

		return orders;
	}

	private static Dictionary<string, string> ReadLocTable(string path)
	{
		return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
			?? throw new InvalidOperationException("empty localization table: " + path);
	}

	private static HextechRelicBase CreateMutableTestRune(Type type)
	{
		HextechRelicBase rune = (HextechRelicBase)Activator.CreateInstance(type)!;
		SetAutoProperty(rune, nameof(AbstractModel.IsMutable), true);
		return rune;
	}
}
