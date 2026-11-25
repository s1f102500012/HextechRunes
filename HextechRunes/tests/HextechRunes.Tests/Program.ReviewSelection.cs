global using HextechForgeRarityWeights = HextechRunes.HextechRarityWeights;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void ReviewTelemetryConfigWithoutEndpointKeepsUserOptOut()
	{
		string defaultEndpoint = HextechServerEndpoints.TelemetryEndpoint;
		(bool enabled, string endpoint) = HextechTelemetry.ParseConfig("{\"enabled\":false}");
		Expect(!enabled, "opt-out without endpoint must stay disabled");
		Equal(defaultEndpoint, endpoint, "missing endpoint falls back to default");

		(enabled, endpoint) = HextechTelemetry.ParseConfig("{\"enabled\":false,\"endpoint\":\"  \"}");
		Expect(!enabled, "opt-out with blank endpoint must stay disabled");
		Equal(defaultEndpoint, endpoint, "blank endpoint falls back to default");

		(enabled, endpoint) = HextechTelemetry.ParseConfig("{\"enabled\":true,\"endpoint\":\"http://example.invalid/x\"}");
		Expect(enabled, "explicit enabled is preserved");
		Equal("http://example.invalid/x", endpoint, "explicit endpoint is preserved");

		(enabled, endpoint) = HextechTelemetry.ParseConfig("null");
		Expect(enabled, "null document keeps previous default-on behavior");
		Equal(defaultEndpoint, endpoint, "null document uses default endpoint");
	}

	[HextechTest]
	private static void ReviewShareCodePreviewNormalizesLikeSave()
	{
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		HextechRunConfigurationSnapshot zeroWeights = defaults with
		{
			RuneRarityWeightsByAct = [ new(0, 0, 0), new(2, 3, 4), new(0, 0, 0) ],
			ForgeRarityWeights = new(0, 0, 0),
			ChaosRuneChancePercent = 250
		};
		string code = HextechConfigShareCodec.Export(zeroWeights);
		HextechConfigShareCodec.ImportPreview preview = HextechConfigShareCodec.TryParse(code)
			?? throw new InvalidOperationException("share code should parse");
		HextechRunConfigurationSnapshot expected = HextechRuneConfiguration.NormalizeSnapshot(zeroWeights);
		SequenceEqual(expected.RuneRarityWeightsByAct, preview.Snapshot.RuneRarityWeightsByAct, "all-zero act weights fall back like save");
		Equal(expected.ForgeRarityWeights, preview.Snapshot.ForgeRarityWeights, "all-zero forge weights fall back like save");
		Equal(new HextechRarityWeights(2, 3, 4), preview.Snapshot.RuneRarityWeightsByAct[1], "valid act weights survive");
		Equal(100, preview.Snapshot.ChaosRuneChancePercent, "chaos chance clamped in preview");
		Equal(HextechRuneConfiguration.GetDefaultForgeRarityWeights(), preview.Snapshot.ForgeRarityWeights, "forge fallback is default");
	}

	[HextechTest]
	private static void ReviewExternalRegistryRejectsBuiltInTypes()
	{
		int runeCount = HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count;
		int forgeCount = HextechExternalContentRegistry.GetForgeRegistrations().Count;
		int version = HextechExternalContentRegistry.Version;
		Type sharedPool = typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool);
		bool runeQueued = HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(sharedPool, typeof(SlapRune));
		bool forgeQueued = HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(sharedPool, typeof(StrengthForge));

		// 测试进程没有 Godot 原生层,真实 Warn 可能崩溃;先耗尽该告警的日志预算。
		Action restoreWarnings = SuppressCompatibilityWarnings("external-content.built-in-rejected");
		try
		{
			// 公开 API 在池登记与 SavedProperty 注入之前就拒绝内置类型;最终登记处仍保留兜底。
			HextechRunesApi.RegisterPlayerRune(typeof(SlapRune), HextechRarityTier.Gold);
			HextechRunesApi.RegisterForge(typeof(StrengthForge), HextechRarityTier.Gold);
			HextechExternalContentRegistry.RegisterPlayerRune(new PlayerRuneRegistration(typeof(SlapRune), HextechRarityTier.Gold), assetModId: null);
			HextechExternalContentRegistry.RegisterForge(new ForgeRegistration(typeof(StrengthForge), HextechRarityTier.Gold), assetModId: null);
		}
		finally
		{
			restoreWarnings();
		}

		Equal(runeQueued, HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(sharedPool, typeof(SlapRune)), "API rejection leaves built-in rune pool registration untouched");
		Equal(forgeQueued, HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(sharedPool, typeof(StrengthForge)), "API rejection leaves built-in forge pool registration untouched");

		Equal(runeCount, HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count, "built-in rune not added to external list");
		Equal(forgeCount, HextechExternalContentRegistry.GetForgeRegistrations().Count, "built-in forge not added to external list");
		Equal(version, HextechExternalContentRegistry.Version, "rejected registration does not bump version");
		Expect(HextechContentRegistry.PlayerRuneMetadata.TryGetRarity(typeof(SlapRune), out HextechRarityTier slapRarity), "built-in rune metadata retained and lookups still build");
		Equal(HextechRarityTier.Silver, slapRarity, "built-in rune rarity retained");
		Equal(HextechRarityTier.Silver, HextechContentRegistry.ForgeRarityByType[typeof(StrengthForge)], "built-in forge metadata retained");
	}

	[HextechTest]
	private static void ReviewEnemyHexIconRelicTypesMatchMonsterHexRegistry()
	{
		HashSet<Type> playerRuneTypes = HextechPlayerRuneRegistry.Registrations.Select(static registration => registration.Type).ToHashSet();
		Type[] derived = HextechMonsterHexRegistry.Registrations
			.Select(static registration => registration.IconRelicType)
			.Where(type => !playerRuneTypes.Contains(type))
			.Distinct()
			.ToArray();
		SetEqual(derived, HextechCustomModelRegistry.EnemyHexIconRelicTypes, "enemy-only hex icon carriers must be listed in HextechCustomModelRegistry");
		Equal(HextechCustomModelRegistry.EnemyHexIconRelicTypes.Count, HextechCustomModelRegistry.EnemyHexIconRelicTypes.Distinct().Count(), "enemy hex icon carriers are unique");
	}

	[HextechTest]
	private static void ReviewSyncedRuneOptionsRequireRegisteredPlayerRunes()
	{
		ModelId slap = ModelDb.GetId<SlapRune>();
		ModelId goldrend = ModelDb.GetId<GoldrendRune>();
		Expect(HextechRuneSelectionCoordinator.AreRegisteredPlayerRuneIds([ slap, goldrend ]), "registered player runes are accepted");
		Expect(!HextechRuneSelectionCoordinator.AreRegisteredPlayerRuneIds([ slap, ModelDb.GetId<Vajra>() ]), "vanilla relic in synced options is rejected");
		Expect(!HextechRuneSelectionCoordinator.AreRegisteredPlayerRuneIds([ ModelDb.GetId<StrengthForge>() ]), "forge in synced rune options is rejected");
		Expect(!HextechRuneSelectionCoordinator.AreRegisteredPlayerRuneIds([ new ModelId("HEXTECH_TEST", "UNKNOWN_RUNE") ]), "unknown id is rejected");
		Expect(!HextechRuneSelectionCoordinator.AreRegisteredPlayerRuneIds([]), "empty synced options are rejected");
	}

	[HextechTest]
	private static void ReviewEnemyHexAdjustmentValidatesSlotsHexesAndRerolls()
	{
		MonsterHexKind[] initial = [ MonsterHexKind.Slap, MonsterHexKind.HeavyHitter ];
		MonsterHexKind[] candidates = [ MonsterHexKind.Tormentor, MonsterHexKind.Repulsor ];
		EnemyHexAdjustmentPayload Payload(MonsterHexKind?[] hexes, int[] rerolls)
		{
			return new EnemyHexAdjustmentPayload(0, 0, hexes, rerolls, IsFinal: false);
		}

		Expect(HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Slap, MonsterHexKind.Tormentor ], [ 0, 1 ]), initial, candidates, 1), "initial hex and reroll candidate within limit are valid");
		Expect(HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Repulsor, null ], [ 5, 9 ]), initial, candidates, HextechRuneConfiguration.InfiniteRerollLimit), "infinite limit accepts any reroll count");
		Expect(!HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Slap ], [ 0 ]), initial, candidates, 1), "slot count must match");
		Expect(!HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Slap, MonsterHexKind.HeavyHitter ], [ 0, 0, 0 ]), initial, candidates, 1), "reroll count list must match slots");
		Expect(!HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Slap, MonsterHexKind.CorruptHeart ], [ 0, 1 ]), initial, candidates, 1), "hex outside initial and reroll pool is rejected");
		Expect(!HextechRuneSelectionCoordinator.IsValidEnemyHexAdjustment(Payload([ MonsterHexKind.Slap, MonsterHexKind.Tormentor ], [ 0, 2 ]), initial, candidates, 1), "reroll count above limit is rejected");
	}

	[HextechTest]
	private static void ReviewRelicChoiceCodecKeepsWireFormatForBothKinds()
	{
		const int Token = 424242;
		RelicModel[] options = CreateRuneSelectionTestOptions(3);
		(HextechRelicChoiceKind Kind, int MessageKind)[] kinds =
		[
			(HextechRelicChoiceKind.Forge, ChoiceKindForgeSelection),
			(HextechRelicChoiceKind.RelicOption, ChoiceKindRelicOptionSelection)
		];
		foreach ((HextechRelicChoiceKind kind, int messageKind) in kinds)
		{
			PlayerChoiceResult result = HextechChoiceCodec.CreateRelicChoice(kind, Token, 2, options);
			Expect(HextechChoiceCodec.TryGetIndexPayload(result, out List<int> payload), $"{kind} payload is an index list");
			SequenceEqual(new[] { Magic, messageKind, Token, 2, StableModelIdListVersion }, payload.Take(5), $"{kind} header layout");
			Expect(HextechChoiceCodec.TryDecodeRelicChoice(kind, result, Token, out int selectedIndex, out List<ModelId> ids), $"{kind} round trip decodes");
			Equal(2, selectedIndex, $"{kind} selected index");
			SequenceEqual(options.Select(static relic => relic.CanonicalId()), ids, $"{kind} option ids");
			Expect(HextechChoiceCodec.MatchesOptionIds(ids, options), $"{kind} matches same options");
			Expect(!HextechChoiceCodec.MatchesOptionIds(ids, options.Reverse().ToArray()), $"{kind} rejects reordered options");
			Expect(!HextechChoiceCodec.MatchesOptionIds(ids, CreateRuneSelectionTestOptions(2)), $"{kind} rejects a different option count");
			HextechRelicChoiceKind other = kind == HextechRelicChoiceKind.Forge ? HextechRelicChoiceKind.RelicOption : HextechRelicChoiceKind.Forge;
			Expect(!HextechChoiceCodec.TryDecodeRelicChoice(other, result, Token, out _, out _), $"{kind} payload is not decoded as {other}");
			Expect(!HextechChoiceCodec.TryDecodeRelicChoice(kind, result, Token + 1, out _, out _), $"{kind} crossed operation is rejected");
		}
	}

	[HextechTest]
	private static void ReviewRuneSelectionTailParsersValidateHeader()
	{
		RelicModel[] options = [ new GeneratedTestRelic { Data = "recipe:a" }, new GeneratedTestRelic { Data = "recipe:b" } ];
		HextechWeightedRuneOptions weighted = new(options, 140);
		PlayerChoiceResult valid = HextechChoiceCodec.CreateRuneSelection(1, 0, 0, [ 1 ], weighted);
		Expect(HextechChoiceCodec.TryGetIndexPayload(valid, out List<int> payload), "rune selection payload");
		Expect(HextechChoiceCodec.TryReadRuneSelectionHeader(payload, out int cursor), "valid header parses");
		Equal(7, cursor, "final options start after header and one reroll entry");
		Expect(HextechChoiceCodec.TryDecodeRuneSelection(valid, 1, 0, out RuneSelectionPayload? decoded), "valid payload decodes");
		Equal(140, decoded.CharacterWeightPercent, "decoded weight");

		List<int> wrongKind = payload.ToList();
		wrongKind[1] = ChoiceKindForgeSelection;
		Expect(!HextechChoiceCodec.TryDecodeRuneSelection(PlayerChoiceResult.FromIndexes(wrongKind), 1, 0, out _), "non rune-selection kind is rejected");

		List<int> wrongMagic = payload.ToList();
		wrongMagic[0] = Magic + 1;
		Expect(!HextechChoiceCodec.TryDecodeRuneSelection(PlayerChoiceResult.FromIndexes(wrongMagic), 1, 0, out _), "wrong magic is rejected");
	}

	[HextechTest]
	private static void ReviewChaosTransformResultIsValidated()
	{
		RelicModel slap = new SlapRune();
		RelicModel goldrend = new GoldrendRune();
		List<RelicModel> options = [ slap, goldrend ];
		Expect(HextechRuneGeneration.TryAcceptTransformResult(options, [ goldrend, slap ], out _), "same-size hextech result is accepted");
		Expect(!HextechRuneGeneration.TryAcceptTransformResult(options, null, out _), "null result is rejected");
		Expect(!HextechRuneGeneration.TryAcceptTransformResult(options, [ slap ], out _), "count change is rejected");
		Expect(!HextechRuneGeneration.TryAcceptTransformResult(options, [ slap, new Vajra() ], out _), "vanilla relic is rejected");
		Expect(!HextechRuneGeneration.TryAcceptTransformResult(options, [ slap, null! ], out _), "null entry is rejected");
	}
}

