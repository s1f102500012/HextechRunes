using System.Reflection;
using Godot;
using HarmonyLib;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using System.Text.Json;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void ConfigMigrationForceResetsBelowV15()
	{
		(int version, IReadOnlySet<string> disabled) = HextechRuneConfiguration.MigrateDisabledIdsForTests(14, ["some-user-custom-id"]);
		Equal(CurrentConfigVersion, version, "v14 config should land on current version");
		SetEqual(HextechRuneConfiguration.GetDefaultDisabledPlayerRuneIds().ToArray(), disabled, "v14 config should force-reset to factory defaults");
	}

	[HextechTest]
	private static void ConfigMigrationV15BaselineReachesCurrentDefault()
	{
		IReadOnlySet<string> baseline = HextechPlayerRuneConfigIds.FromTypes(Version15FactoryDisabledRuneTypes);
		(int version, IReadOnlySet<string> migrated) = HextechRuneConfiguration.MigrateDisabledIdsForTests(15, baseline);
		Equal(CurrentConfigVersion, version, "v15 config should land on current version");
		SetEqual(
			HextechRuneConfiguration.GetDefaultDisabledPlayerRuneIds().ToArray(),
			migrated,
			$"v15 factory defaults + migration chain should equal current factory defaults; migrated:\n{string.Join("\n", migrated.OrderBy(static id => id, StringComparer.Ordinal))}\ncurrent defaults:\n{string.Join("\n", HextechRuneConfiguration.GetDefaultDisabledPlayerRuneIds().OrderBy(static id => id, StringComparer.Ordinal))}");
	}

	/// <summary>
	/// 默认开关迁移表逐段检查:低于该段版本的配置迁移一次即带上本段改动、保留玩家自定义项,迁移结果再次载入不变;
	/// 已在该段版本之上的配置尊重玩家手动开关。迁移表直接从源码读取,新增一段不必改这里;
	/// 被后续段再次翻转的符文(如回力OK镖 v17 启用、v27 禁用)只看最后一段,整条链的终点由 V15 基线测试守护。
	/// </summary>
	[HextechTest]
	private static void ConfigDefaultMigrationSegmentsApplyOnceAndKeepUserChoices()
	{
		const string customId = "custom-unknown-rune";
		(int BelowVersion, Type[] Enable, Type[] Disable)[] playerSegments = ReadPlayerRuneDefaultMigrations();
		Expect(playerSegments.Length > 0, "player migration table should be readable");
		for (int index = 0; index < playerSegments.Length; index++)
		{
			(int below, Type[] enable, Type[] disable) = playerSegments[index];
			HashSet<Type> flippedLater = playerSegments
				.Skip(index + 1)
				.SelectMany(static segment => segment.Enable.Concat(segment.Disable))
				.ToHashSet();
			string[] disableIds = HextechPlayerRuneConfigIds.FromTypes(disable.Where(type => !flippedLater.Contains(type))).ToArray();
			string[] enableIds = HextechPlayerRuneConfigIds.FromTypes(enable.Where(type => !flippedLater.Contains(type))).ToArray();
			Expect(disable.All(HextechContentRegistry.PlayerRuneMetadata.IsConfigurable), $"v{below} default-disabled runes stay configurable");

			(int version, IReadOnlySet<string> migrated) = HextechRuneConfiguration.MigrateDisabledIdsForTests(below - 1, [customId, .. enableIds]);
			Equal(CurrentConfigVersion, version, $"v{below - 1} config should land on current version");
			Expect(disableIds.All(migrated.Contains), $"v{below - 1} config gains the v{below} default disables");
			Expect(!enableIds.Any(migrated.Contains), $"v{below - 1} config gains the v{below} default enables");
			Expect(migrated.Contains(customId), $"v{below} migration keeps custom selections");
			(_, IReadOnlySet<string> reloaded) = HextechRuneConfiguration.MigrateDisabledIdsForTests(version, migrated);
			SetEqual(migrated, reloaded, $"v{below} migration runs only once");

			(_, IReadOnlySet<string> userChoice) = HextechRuneConfiguration.MigrateDisabledIdsForTests(below, enableIds);
			Expect(!disableIds.Any(userChoice.Contains), $"manual re-enable after v{below} is preserved");
			Expect(enableIds.All(userChoice.Contains), $"manual disable after v{below} is preserved");
		}

		(int BelowVersion, MonsterHexKind Kind)[] monsterSegments = (((int, MonsterHexKind)[]?)typeof(HextechRuneConfiguration)
			.GetField("MonsterHexDefaultDisableMigrations", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null))
			?? throw new MissingFieldException(nameof(HextechRuneConfiguration), "MonsterHexDefaultDisableMigrations");
		Expect(monsterSegments.Length > 0, "monster migration table should be readable");
		string customMonsterId = MonsterHexKind.FrostWraith.ToString();
		foreach ((int below, MonsterHexKind kind) in monsterSegments)
		{
			string id = kind.ToString();
			Expect(HextechRuneConfiguration.GetDefaultDisabledMonsterHexIds().Contains(id), $"new configs disable enemy {kind} by default");
			Expect(!HextechMonsterHexRegistry.Registrations.Single(row => row.Kind == kind).Disabled, $"enemy {kind} default-off stays configurable, not hard-removed");

			(int version, IReadOnlySet<string> migrated) = HextechRuneConfiguration.MigrateDisabledMonsterHexIdsForTests(below - 1, [customMonsterId]);
			Equal(CurrentConfigVersion, version, $"v{below - 1} monster config should land on current version");
			Expect(migrated.Contains(id) && migrated.Contains(customMonsterId), $"v{below - 1} monster config gains enemy {kind} and keeps custom selections");
			(_, IReadOnlySet<string> reloaded) = HextechRuneConfiguration.MigrateDisabledMonsterHexIdsForTests(version, migrated);
			SetEqual(migrated, reloaded, $"v{below} monster migration runs only once");
			(_, IReadOnlySet<string> userChoice) = HextechRuneConfiguration.MigrateDisabledMonsterHexIdsForTests(below, []);
			Expect(!userChoice.Contains(id), $"manual enemy {kind} re-enable after v{below} is preserved");
		}
	}

	// 两者都是 HextechRuneConfiguration 的私有成员:测试统一从这里读,升配置版本或追加迁移段时测试不必逐处同步。
	private static int CurrentConfigVersion => (int)(typeof(HextechRuneConfiguration)
		.GetField("CurrentConfigVersion", BindingFlags.NonPublic | BindingFlags.Static)
		?.GetRawConstantValue()
		?? throw new MissingFieldException(nameof(HextechRuneConfiguration), "CurrentConfigVersion"));

	private static (int BelowVersion, Type[] Enable, Type[] Disable)[] ReadPlayerRuneDefaultMigrations()
	{
		Array table = (Array?)typeof(HextechRuneConfiguration)
			.GetField("PlayerRuneDefaultMigrations", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null)
			?? throw new MissingFieldException(nameof(HextechRuneConfiguration), "PlayerRuneDefaultMigrations");
		return table.Cast<object>()
			.Select(static row =>
			{
				object Read(string name) => row.GetType().GetProperty(name)?.GetValue(row)
					?? throw new MissingMemberException(row.GetType().Name, name);
				return ((int)Read("BelowVersion"), (Type[])Read("Enable"), (Type[])Read("Disable"));
			})
			.ToArray();
	}

	[HextechTest]
	private static void ConfigMigrationCurrentVersionPreservesCustomDisabledIds()
	{
		string customId = HextechRuneConfiguration.GetDefaultDisabledPlayerRuneIds().OrderBy(static id => id, StringComparer.Ordinal).First();
		(int version, IReadOnlySet<string> disabled) = HextechRuneConfiguration.MigrateDisabledIdsForTests(CurrentConfigVersion, [customId]);
		Equal(CurrentConfigVersion, version, "current-version config keeps version");
		SetEqual([customId], disabled, "current-version config should pass user selection through unchanged");

		(int monsterVersion, IReadOnlySet<string> disabledMonsters) =
			HextechRuneConfiguration.MigrateDisabledMonsterHexIdsForTests(CurrentConfigVersion, [MonsterHexKind.FrostWraith.ToString()]);
		Equal(CurrentConfigVersion, monsterVersion, "current-version monster config keeps version");
		SetEqual(
			[MonsterHexKind.FrostWraith.ToString()],
			disabledMonsters,
			"current-version monster config should preserve a user-enabled Blank Check");
	}

	[HextechTest]
	private static void ConfigShareRoundTripKeepsActRarityWeights()
	{
		HextechRarityWeights[] expectedWeights =
		[
			new HextechRarityWeights(1, 2, 3),
			new HextechRarityWeights(4, 5, 6),
			new HextechRarityWeights(7, 8, 9)
		];
		HextechRunConfigurationSnapshot snapshot = HextechRuneConfiguration.GetDefaultSnapshot() with
		{
			RuneRarityWeightsByAct = expectedWeights
		};
		string code = HextechConfigShareCodec.Export(snapshot);
		HextechConfigShareCodec.ImportPreview preview = HextechConfigShareCodec.TryParse(code)
			?? throw new InvalidOperationException("act rarity share code should decode");
		SequenceEqual(expectedWeights, preview.Snapshot.RuneRarityWeightsByAct, "act rarity weights should survive share-code round trip");
	}

	[HextechTest]
	private static void ConfigMigrationV27KeepsNormalWeightsAndEnablesConsecutiveSilverPrevention()
	{
		(int migratedVersion, HextechRarityWeights migratedWeights, bool consecutiveSilverPrevention) =
			HextechRuneConfiguration.MigrateRarityConfigForTests(
				27,
				new HextechRarityWeights(4, 5, 6));
		Equal(CurrentConfigVersion, migratedVersion, "v27 rarity config should land on current version");
		Equal(new HextechRarityWeights(4, 5, 6), migratedWeights, "v27 normal weights should become rune weights");
		Equal(true, consecutiveSilverPrevention, "legacy rarity config should enable consecutive-Silver prevention by default");

		HextechRarityWeights[] migratedByAct = HextechRuneConfiguration.MigrateSingleRarityConfigForTests(
			31,
			new HextechRarityWeights(3, 4, 5));
		SequenceEqual(
			new[]
			{
				new HextechRarityWeights(3, 4, 5),
				new HextechRarityWeights(3, 4, 5),
				new HextechRarityWeights(3, 4, 5)
			},
			migratedByAct,
			"v31 single rarity weights should migrate to every act");
	}

	[HextechTest]
	private static void ConfigMigrationV33ChangesLegacyInfiniteMonsterRerolls()
	{
		(int migratedVersion, int migratedLimit) =
			HextechRuneConfiguration.MigrateMonsterHexRerollLimitForTests(32, HextechRuneConfiguration.InfiniteRerollLimit);
		Equal(CurrentConfigVersion, migratedVersion, "v32 config should land on current version");
		Equal(1, migratedLimit, "v32 infinite enemy rerolls should migrate to the new one-reroll default");

		(_, int finiteLimit) = HextechRuneConfiguration.MigrateMonsterHexRerollLimitForTests(32, 4);
		Equal(4, finiteLimit, "v32 custom finite enemy rerolls should be preserved");

		(_, int currentInfiniteLimit) = HextechRuneConfiguration.MigrateMonsterHexRerollLimitForTests(
			33,
			HextechRuneConfiguration.InfiniteRerollLimit);
		Equal(
			HextechRuneConfiguration.InfiniteRerollLimit,
			currentInfiniteLimit,
			"v33 explicit infinite enemy rerolls should be preserved");
	}

	[HextechTest]
	private static void ChaosChanceConfigurationRoundTripsAndDefaults()
	{
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		Equal(33, defaults.ChaosRuneChancePercent, "default chance");
		HextechRunConfigurationSnapshot snapshot = defaults with { ChaosRuneChancePercent = 73 };
		PlayerChoiceResult roll = HextechChoiceCodec.CreateActRoll(0, HextechRarityTier.Gold, null, false,
			snapshot.EnemyHexCountsByAct, snapshot.DisabledPlayerRuneIds, snapshot);
		Expect(HextechChoiceCodec.TryDecodeActRoll(roll, 0, out _, out _, out _, out _, out _, out HextechRunConfigurationSnapshot decoded), "snapshot decodes");
		Equal(73, decoded.ChaosRuneChancePercent, "host chance wins");
		Equal(73, HextechConfigShareCodec.TryParse(HextechConfigShareCodec.Export(snapshot))!.Snapshot.ChaosRuneChancePercent, "share code preserves chance");
		Equal(0, HextechRuneConfiguration.NormalizeSnapshot(snapshot with { ChaosRuneChancePercent = -1 }).ChaosRuneChancePercent, "lower bound");
		Equal(100, HextechRuneConfiguration.NormalizeSnapshot(snapshot with { ChaosRuneChancePercent = 101 }).ChaosRuneChancePercent, "upper bound");
		string json = JsonSerializer.Serialize(snapshot);
		Equal(73, JsonSerializer.Deserialize<HextechRunConfigurationSnapshot>(json)!.ChaosRuneChancePercent, "save JSON preserves chance");
		json = json.Replace(",\"ChaosRuneChancePercent\":73", "");
		Equal(33, JsonSerializer.Deserialize<HextechRunConfigurationSnapshot>(json)!.ChaosRuneChancePercent, "old JSON default");
	}

	[HextechTest]
	private static void EnemyHexCountStateNormalizesMissingAndOutOfRangeValues()
	{
		SequenceEqual(new[] { 1, 1, 1 }, HextechRuneConfiguration.NormalizePlayerHexCounts(null), "null player count snapshot");
		SequenceEqual(new[] { 2, 2, 2 }, HextechHexCountState.NormalizeEnemyCounts(null), "null enemy count snapshot");
		SequenceEqual(new[] { 0, 6, 2 }, HextechHexCountState.NormalizeEnemyCounts([ -1, 7 ]), "partial clamped enemy count snapshot");

		HextechHexCountState state = new(HextechHexCountState.NormalizeEnemyCounts);
		state.Set([ 2, 3, 4, 5 ]);
		SequenceEqual(new[] { 2, 3, 4 }, state.Snapshot, "state should keep exactly three normalized act counts");
	}

	// 只迁移仍是旧默认值 1/2/3 的配置;玩家改过的数量、已迁移过的配置都不动。
	[HextechTest]
	private static void EnemyHexCountMigrationOnlyRewritesOldDefault()
	{
		(int version, int[] migrated) = HextechRuneConfiguration.MigrateEnemyHexCountsForTests(45, [ 1, 2, 3 ]);
		Equal(CurrentConfigVersion, version, "migrated config version");
		SequenceEqual(new[] { 2, 2, 2 }, migrated, "old default enemy counts migrate to the new default");
		SequenceEqual(new[] { 1, 3, 3 }, HextechRuneConfiguration.MigrateEnemyHexCountsForTests(45, [ 1, 3, 3 ]).EnemyHexCountsByAct, "customized enemy counts stay");
		SequenceEqual(new[] { 1, 2, 3 }, HextechRuneConfiguration.MigrateEnemyHexCountsForTests(46, [ 1, 2, 3 ]).EnemyHexCountsByAct, "a later manual 1/2/3 stays");
	}

	[HextechTest]
	private static void RunConfigurationDefaultSnapshotUsesExpectedActCounts()
	{
		HextechRunConfigurationSnapshot snapshot = HextechRuneConfiguration.GetDefaultSnapshot();
		SequenceEqual(new[] { 1, 1, 1 }, snapshot.PlayerHexCountsByAct, "default player act counts");
		SequenceEqual(new[] { 2, 2, 2 }, snapshot.EnemyHexCountsByAct, "default enemy act counts");
		Equal(1, snapshot.PlayerRuneRerollLimit, "default player reroll limit");
		Equal(1, snapshot.MonsterHexRerollLimit, "default monster reroll limit");
		SequenceEqual(
			new[]
			{
				new HextechRarityWeights(1, 1, 1),
				new HextechRarityWeights(1, 1, 1),
				new HextechRarityWeights(1, 1, 1)
			},
			snapshot.RuneRarityWeightsByAct,
			"default rune rarity weights by act");
		Equal(snapshot.RuneRarityWeightsByAct[2], snapshot.GetRuneRarityWeightsForAct(5), "extra acts should use third-act-plus weights");
		Equal(true, snapshot.PreventConsecutiveSilverRunes, "default prevent consecutive Silver toggle");
		Equal(5, snapshot.GoldenRerollChancePercent, "default golden reroll chance");
		Equal(0, HextechRuneConfiguration.ClampGoldenRerollChancePercent(-1), "golden reroll chance lower clamp");
		Equal(100, HextechRuneConfiguration.ClampGoldenRerollChancePercent(101), "golden reroll chance upper clamp");
	}

	[HextechTest]
	private static void RetiredCustomRarityModifiersAreNotInstalledIntoCustomRunUi()
	{
		SequenceEqual(
			new[]
			{
				typeof(HextechSilverRunModifier),
				typeof(HextechGoldRunModifier),
				typeof(HextechPrismaticRunModifier)
			},
			HextechCustomModelRegistry.CustomRarityModifierTypes,
			"retired custom rarity modifier models should remain registered for old runs");
		Expect(
			typeof(HextechCustomRunModifierCompatibility).GetMethod(
				"Install",
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null,
			"retired rarity modifiers should expose no custom-run UI installer");

		MethodInfo initialize = typeof(ModEntry).GetMethod(nameof(ModEntry.Initialize), BindingFlags.Static | BindingFlags.Public)
			?? throw new MissingMethodException(nameof(ModEntry), nameof(ModEntry.Initialize));
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(initialize)
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Expect(
			calls.All(static method => method.DeclaringType != typeof(HextechCustomRunModifierCompatibility)),
			"mod initialization should not install retired custom rarity modifier UI hooks");
	}

	[HextechTest]
	private static void RunConfigurationDefaultSnapshotDisablesRiskyContent()
	{
		// 腐化树枝自配置 v16 起转为默认启用;改用长期默认禁用的逃跑计划做代表。
		string escapePlanId = ModelDb.GetId<EscapePlanRune>().Entry;
		string corruptedBranchId = ModelDb.GetId<CorruptedBranchRune>().Entry;
		string advanceToRetreatId = ModelDb.GetId<AdvanceToRetreatRune>().Entry;
		string happyAccidentId = ModelDb.GetId<HappyAccidentRune>().Entry;
		HextechRunConfigurationSnapshot snapshot = HextechRuneConfiguration.GetDefaultSnapshot();

		Expect(HextechRuneConfiguration.GetDefaultDisabledPlayerRuneIds().Contains(escapePlanId), "default player rune ids should disable escape plan");
		Expect(snapshot.DisabledPlayerRuneIds.Contains(escapePlanId), "default snapshot should disable escape plan");
		Expect(!snapshot.DisabledPlayerRuneIds.Contains(advanceToRetreatId), "default snapshot should enable Advance to Retreat");
		Expect(!snapshot.DisabledPlayerRuneIds.Contains(happyAccidentId), "default snapshot should enable Happy Accident");
		foreach (Type runeType in new[] { typeof(OmegaRune), typeof(OkBoomerangRune), typeof(FeyMagicRune), typeof(PorcupineRune), typeof(AstralBodyRune) })
		{
			Expect(
				snapshot.DisabledPlayerRuneIds.Contains(ModelDb.GetId(runeType).Entry),
				$"default snapshot should disable {runeType.Name}");
		}
		Expect(!snapshot.DisabledPlayerRuneIds.Contains(corruptedBranchId), "corrupted branch should be enabled by default since config v16");
	}

	[HextechTest]
	private static void RerollLimitConfigUsesZeroToNineThenInfinite()
	{
		Equal(0, HextechRuneConfiguration.StepRerollLimit(0, -1), "zero stays zero on decrement");
		Equal(1, HextechRuneConfiguration.StepRerollLimit(0, 1), "zero increments to one");
		Equal(9, HextechRuneConfiguration.StepRerollLimit(8, 1), "eight increments to nine");
		Equal(HextechRuneConfiguration.InfiniteRerollLimit, HextechRuneConfiguration.StepRerollLimit(9, 1), "nine increments to infinite");
		Equal(9, HextechRuneConfiguration.StepRerollLimit(HextechRuneConfiguration.InfiniteRerollLimit, -1), "infinite decrements to nine");
		Equal(HextechRuneConfiguration.InfiniteRerollLimit, HextechRuneConfiguration.StepRerollLimit(HextechRuneConfiguration.InfiniteRerollLimit, 1), "infinite stays infinite on increment");
		Equal(9, HextechRuneConfiguration.ClampRerollLimit(99), "finite values clamp to nine");
	}

	[HextechTest]
	private static void EnemyHexCountStateUsesThirdActForEndlessAndBeyondThirdAct()
	{
		HextechHexCountState state = new(HextechHexCountState.NormalizeEnemyCounts);
		state.Set([ 1, 2, 3 ]);

		Equal(1, state.GetForAct(-1, endless: false), "negative act clamps to first act");
		Equal(1, state.GetForAct(0, endless: false), "first act count");
		Equal(2, state.GetForAct(1, endless: false), "second act count");
		Equal(3, state.GetForAct(2, endless: false), "third act count");
		Equal(3, state.GetForAct(3, endless: false), "beyond third act count");
		Equal(3, state.GetForAct(0, endless: true), "endless first loop uses third act count");
	}

	[HextechTest]
	private static void PlayerRuneConfigSnapshotStateUsesClientFallbackWithoutSnapshot()
	{
		string localDisabledId = HextechCatalog.GetConfigurablePlayerRuneIds()
			.OrderBy(static id => id.Entry, StringComparer.Ordinal)
			.First()
			.Entry;
		HextechPlayerRuneConfigSnapshotState state = new();

		Expect(!state.HasSnapshot, "new player rune config state should not have snapshot");
		SetEqual([ localDisabledId ], state.GetDisabledIdsForPool(isClient: false, [ localDisabledId ]), "host/local fallback disabled ids");
		Expect(state.GetDisabledIdsForPool(isClient: true, [ localDisabledId ]).Count == 0, "client fallback should ignore local disabled ids without host snapshot");
	}

	[HextechTest]
	private static void PlayerRuneConfigSnapshotStateSnapshotOverridesLocalFallback()
	{
		string[] ids = HextechCatalog.GetConfigurablePlayerRuneIds()
			.OrderBy(static id => id.Entry, StringComparer.Ordinal)
			.Take(2)
			.Select(static id => id.Entry)
			.ToArray();
		HextechPlayerRuneConfigSnapshotState state = new();

		state.Set([ ids[1] ]);

		Expect(state.HasSnapshot, "snapshot should be present after set");
		Equal(1, state.SnapshotCount, "snapshot count");
		SetEqual([ ids[1] ], state.GetDisabledIdsForPool(isClient: true, [ ids[0] ]), "snapshot should override client fallback");
		SetEqual([ ids[1] ], state.GetDisabledIdsForPool(isClient: false, [ ids[0] ]), "snapshot should override host fallback");
	}

	[HextechTest]
	private static void PlayerRuneConfigSnapshotStateSerializesAndClearsMalformedData()
	{
		string[] ids = HextechCatalog.GetConfigurablePlayerRuneIds()
			.OrderByDescending(static id => id.Entry, StringComparer.Ordinal)
			.Take(2)
			.Select(static id => id.Entry)
			.ToArray();
		HextechPlayerRuneConfigSnapshotState state = new();

		state.Set(ids);
		string serialized = state.Serialize();
		HextechPlayerRuneConfigSnapshotState restored = new();
		Expect(restored.TryRestore(serialized, out string? restoreError), $"serialized snapshot should restore: {restoreError}");
		SetEqual(ids, restored.GetDisabledIdsForPool(isClient: true, []), "restored snapshot ids");

		Expect(!restored.TryRestore("{", out string? malformedError), "malformed snapshot should fail");
		Expect(!string.IsNullOrWhiteSpace(malformedError), "malformed snapshot should return an error");
		Expect(!restored.HasSnapshot, "malformed snapshot should clear existing snapshot");
		Expect(restored.Serialize() == "", "cleared snapshot should serialize as empty string");
	}

	[HextechTest]
	private static void MayhemRunContextResetForNewRunClearsState()
	{
		HextechMayhemRunContext context = new();
		context.ActState.SetResolved(0, true);
		context.ChoiceHistory.SavedTelemetryChoicesJson = "[1]";
		context.CombatTracking.GlobalProcsThisCombat["reset-check"] = 7;
		context.HexCountRecoveryBaseline = 5;
		context.MonsterHexStrengthTierFloor = 3;
		context.EnemyTezcatarasMercyCombatCounter = 4;
		context.HostUsesBetterMultiplayerScaling = true;
		context.RuneSelectionJournal.RecordSelected(
			0,
			0,
			11,
			new ModelId("HEXTECH_TEST", "RESET_ME"));

		context.ResetForNewRun([ 7, -1, 2 ], [ 2, 7, -1 ]);

		SequenceEqual(new[] { 6, 0, 2 }, context.PlayerHexCounts.Snapshot, "new-run player count snapshot");
		SequenceEqual(new[] { 2, 6, 0 }, context.EnemyHexCounts.Snapshot, "new-run enemy count snapshot");
		Equal(0, context.HexCountRecoveryBaseline, "new-run recovery baseline");
		Equal(0, context.MonsterHexStrengthTierFloor, "new-run strength floor");
		Equal(0, context.EnemyTezcatarasMercyCombatCounter, "new-run tezcataras counter");
		Expect(!context.ActState.IsResolved(0), "new-run act state should reset");
		Equal("", context.ChoiceHistory.SavedTelemetryChoicesJson, "new-run telemetry choices should reset");
		Equal(0, context.CombatTracking.GlobalProcsThisCombat.Count, "new-run combat tracking should reset");
		Equal(true, context.HostUsesBetterMultiplayerScaling, "new-run should preserve host scaling flag until act roll refreshes it");
		Expect(
			!context.RuneSelectionJournal.TryGet(0, 0, 11, out _),
			"new-run rune selection journal should reset");
	}

	[HextechTest]
	private static void MayhemRunContextResetForEndlessLoopPreservesStageRows()
	{
		HextechMayhemRunContext context = new();
		context.EnemyHexCounts.Set([ 1, 2, 3 ]);
		context.ActState.SetMonsterHexes(0, [ MonsterHexKind.ShrinkRay ]);
		context.ActState.SetResolved(0, true);
		context.ActState.SetMonsterHexes(1, [ MonsterHexKind.ShrinkRay, MonsterHexKind.PandorasBox ]);
		context.ActState.SetResolved(1, true);
		context.ChoiceHistory.SavedSeenPlayerRuneIdsJson = "{\"0\":[\"A\"]}";
		context.CombatTracking.GlobalProcsThisCombat["reset-check"] = 9;

		context.ResetForEndlessLoop(6);

		SequenceEqual(new[] { 1, 2, 3 }, context.EnemyHexCounts.Snapshot, "endless reset should keep enemy count snapshot");
		Equal(6, context.HexCountRecoveryBaseline, "endless recovery baseline");
		Equal(3, context.MonsterHexStrengthTierFloor, "endless strength floor");
		Expect(context.IsEndlessLoopActive, "endless flag");
		Equal(3, context.ActSelectionIndexOffset, "endless reset should advance the monotonic stage index");
		Expect(context.ActState.IsResolved(1), "endless reset should preserve resolved stage history");
		IReadOnlyList<IReadOnlyList<MonsterHexKind>> existingRows = context.ActState.GetMonsterHexRows();
		Equal(2, existingRows.Count, "endless reset should preserve previous acquisition rows");
		SequenceEqual(new[] { MonsterHexKind.ShrinkRay }, existingRows[0], "first enemy-hex acquisition row");
		SequenceEqual(new[] { MonsterHexKind.PandorasBox }, existingRows[1], "second enemy-hex acquisition row");

		context.ActState.SetMonsterHexes(3, [ MonsterHexKind.ShrinkRay, MonsterHexKind.PandorasBox, MonsterHexKind.FrostWraith ]);
		context.ActState.SetResolved(3, true);
		IReadOnlyList<IReadOnlyList<MonsterHexKind>> rowsAfterNextLoop = context.ActState.GetMonsterHexRows();
		Equal(3, rowsAfterNextLoop.Count, "fourth acquisition should create a new collapse row");
		SequenceEqual(new[] { MonsterHexKind.FrostWraith }, rowsAfterNextLoop[2], "next-loop acquisition row");
		Equal("", context.ChoiceHistory.SavedSeenPlayerRuneIdsJson, "endless reset should clear seen runes");
		Equal(0, context.CombatTracking.GlobalProcsThisCombat.Count, "endless reset should clear combat tracking");
	}

	// "已见"只存条目名;还原出的 ID 必须和候选池里遗物的真实 ID 相等,否则跨幕排除永远匹配不上。
	[HextechTest]
	private static void SeenRuneIdsRoundTripToRealRelicIds()
	{
		RelicModel rune = CreateMutableTestModel<BigStrengthRune>();
		ModelId realId = rune.CanonicalInstance?.Id ?? rune.Id;
		Equal(realId, HextechMayhemChoiceHistoryState.ToSeenRelicId(realId.Entry), "seen entry restores to the pool's relic id");
		Expect(new HashSet<ModelId> { HextechMayhemChoiceHistoryState.ToSeenRelicId(realId.Entry) }.Contains(realId),
			"seen exclusion set matches candidates by id");
	}

	[HextechTest]
	private static void MayhemActStateSupportsExtraActsAndStableExtraStageIds()
	{
		HextechMayhemActState state = new();
		state.SetRarity(4, HextechRarityTier.Prismatic);
		state.SetMonsterHexes(4, [ MonsterHexKind.ShrinkRay ]);
		state.SetResolved(4, true);
		int finaleIndex = state.GetOrCreateExtraStageIndex("0:IntegratedStrategyEvents:Finale:EternalDust", 5);
		Equal(5, finaleIndex, "extra finale should be placed after real acts");
		Equal(finaleIndex, state.GetOrCreateExtraStageIndex("0:IntegratedStrategyEvents:Finale:EternalDust", 99), "extra finale identity should be stable");

		HextechMayhemActState restored = new();
		restored.SavedRarityByAct = state.SavedRarityByAct;
		restored.SavedResolvedActs = state.SavedResolvedActs;
		restored.SavedMonsterHexesByActJson = state.SavedMonsterHexesByActJson;
		restored.SavedExtraStageIndexesJson = state.SavedExtraStageIndexesJson;
		Equal(HextechRarityTier.Prismatic, restored.GetRarity(4), "extra-act rarity should round-trip");
		Expect(restored.IsResolved(4), "extra-act resolved state should round-trip");
		SequenceEqual(new[] { MonsterHexKind.ShrinkRay }, restored.GetMonsterHexes(4), "extra-act enemy hexes should round-trip");
		Equal(finaleIndex, restored.GetOrCreateExtraStageIndex("0:IntegratedStrategyEvents:Finale:EternalDust", 99), "extra finale mapping should round-trip");
	}

	[HextechTest]
	private static void MayhemRunContextDebugResetSetsOnlyRequestedMonsterHex()
	{
		HextechMayhemRunContext context = new();
		context.EnemyHexCounts.Set([ 2, 3, 4 ]);
		context.ActState.SetMonsterHexes(0, [ MonsterHexKind.FrostWraith ]);
		context.ActState.SetResolved(0, true);
		context.HexCountRecoveryBaseline = 2;
		context.MonsterHexStrengthTierFloor = 3;
		context.EnemyTezcatarasMercyCombatCounter = 5;

		context.ResetForDebugMonsterHex(2, MonsterHexKind.PandorasBox, HextechRarityTier.Prismatic);

		SequenceEqual(new[] { 2, 2, 2 }, context.EnemyHexCounts.Snapshot, "debug reset enemy count snapshot");
		Equal(0, context.HexCountRecoveryBaseline, "debug reset recovery baseline");
		Equal(0, context.MonsterHexStrengthTierFloor, "debug reset strength floor");
		Equal(0, context.EnemyTezcatarasMercyCombatCounter, "debug reset tezcataras counter");
		SequenceEqual(new[] { MonsterHexKind.PandorasBox }, context.ActState.GetMonsterHexes(2), "debug reset monster hex");
		Expect(context.ActState.IsResolved(2), "debug reset should resolve requested act");
		Expect(!context.ActState.GetKnownMonsterHexes().Contains(MonsterHexKind.FrostWraith), "debug reset should discard previous monster hexes");
	}

	[HextechTest]
	private static void ExternalModelIdConflictsAreRejectedBeforeRegistration()
	{
		Type playerCollisionType = typeof(BurningBlood);
		Type forgeCollisionType = typeof(Anchor);
		Equal(
			ModelDb.GetId<MegaCrit.Sts2.Core.Models.Relics.BurningBlood>(),
			ModelDb.GetId(playerCollisionType),
			"test player rune should collide with the vanilla Burning Blood ModelId");
		Equal(
			ModelDb.GetId<MegaCrit.Sts2.Core.Models.Relics.Anchor>(),
			ModelDb.GetId(forgeCollisionType),
			"test forge should collide with the vanilla Anchor ModelId");

		int registryVersion = HextechExternalContentRegistry.Version;
		int playerRuneCount = HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count;
		int forgeCount = HextechExternalContentRegistry.GetForgeRegistrations().Count;
		int eventRelicCount = HextechExternalContentRegistry.GetEventRelicTypes().Count;
		InvalidOperationException universeConflict = ExpectThrows<InvalidOperationException>(
			() => HextechCatalog.EnsureExternalModelIdAvailable(playerCollisionType),
			"external ModelId validation should include vanilla model types");
		Expect(
			universeConflict.Message.Contains("same ModelId", StringComparison.Ordinal),
			"vanilla collision should come from the ModelId validator");
		ExpectThrows<InvalidOperationException>(
			() => RunBeforeSavedPropertyCacheInitialization(() =>
				HextechRunesApi.RegisterPlayerRune<BurningBlood>(HextechRarityTier.Silver)),
			"player rune API should reject a vanilla ModelId collision before registration");
		ExpectThrows<InvalidOperationException>(
			() => RunBeforeSavedPropertyCacheInitialization(() =>
				HextechRunesApi.RegisterEventRelic<BurningBlood>()),
			"event relic API should reject a vanilla ModelId collision before registration");
		ExpectThrows<InvalidOperationException>(
			() => RunBeforeSavedPropertyCacheInitialization(() =>
				HextechRunesApi.RegisterForge<Anchor>(HextechRarityTier.Gold)),
			"forge API should reject a vanilla ModelId collision before registration");
		Equal(registryVersion, HextechExternalContentRegistry.Version, "ModelId collision registry version");
		Equal(playerRuneCount, HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count, "ModelId collision player rune count");
		Equal(forgeCount, HextechExternalContentRegistry.GetForgeRegistrations().Count, "ModelId collision forge count");
		Equal(eventRelicCount, HextechExternalContentRegistry.GetEventRelicTypes().Count, "ModelId collision event relic count");
		Expect(
			!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
				typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool),
				playerCollisionType),
			"colliding player rune should not enter the shared relic pool queue");
		Expect(
			!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
				typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool),
				forgeCollisionType),
			"colliding forge should not enter the shared relic pool queue");
		Expect(
			!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
				typeof(MegaCrit.Sts2.Core.Models.RelicPools.EventRelicPool),
				playerCollisionType),
			"colliding event relic should not enter the event relic pool queue");

		Type existingType = typeof(ExternalRegistrationEventRelic);
		Type incomingType = typeof(ExternalRegistrationTestRune);
		Dictionary<Type, ModelId> duplicateIds = new()
		{
			[existingType] = new ModelId("HEXTECH_TEST", "DUPLICATE"),
			[incomingType] = new ModelId("HEXTECH_TEST", "DUPLICATE")
		};
		ExpectThrows<InvalidOperationException>(
			() => HextechCatalog.EnsureUniqueModelIds(
				[ existingType, incomingType ],
				type => duplicateIds[type]),
			"different external model types must not share a full ModelId");

		Dictionary<Type, ModelId> duplicateEntries = new()
		{
			[existingType] = new ModelId("EXTERNAL_A", "SAME_ENTRY"),
			[incomingType] = new ModelId("EXTERNAL_B", "SAME_ENTRY")
		};
		ExpectThrows<InvalidOperationException>(
			() => HextechCatalog.EnsureConfigurablePlayerRuneIdEntryAvailable(
				incomingType,
				[ existingType ],
				type => duplicateEntries[type]),
			"configurable external runes must reject duplicate Entry values across categories");
	}

	[HextechTest]
	private static void ExternalPlayerRuneRegistrationUpdatesCatalog()
	{
		Type runeType = typeof(ExternalRegistrationTestRune);
		Expect(!HextechContentRegistry.PlayerRuneMetadata.IsVisible(runeType), "external rune should not be visible before registration");
		RunBeforeSavedPropertyCacheInitialization(() =>
			HextechRunesApi.RegisterPlayerRune<ExternalRegistrationTestRune>(
				HextechRarityTier.Gold,
				tagKey: "COMPREHENSIVE",
				assetModId: "HextechRunes.Tests"));
		Expect(HextechContentRegistry.PlayerRuneMetadata.IsVisible(runeType), "external rune should be visible after registration");
		Expect(HextechCatalog.IsPlayerRuneTypeConfigurable(runeType), "external rune should be configurable after registration");
		Expect(HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(HextechRarityTier.Gold).Contains(runeType), "external rune should enter rarity pool");
		Expect(HextechCatalog.GetAllConfigurableRuneTypes().Contains(runeType), "external rune should enter configurable rune type pool");
		Expect(HextechCatalog.GetConfigurablePlayerRuneIds().Contains(ModelDb.GetId(runeType)), "external rune should enter configurable rune id pool");
	}

	[HextechTest]
	private static void ExternalEventRelicRegistrationUpdatesRegistry()
	{
		Type relicType = typeof(ExternalRegistrationEventRelic);
		Expect(!HextechContentRegistry.EventRelicTypes.Contains(relicType), "external event relic should not be registered initially");
		RunBeforeSavedPropertyCacheInitialization(() =>
			HextechRunesApi.RegisterEventRelic<ExternalRegistrationEventRelic>("HextechRunes.Tests"));
		Expect(HextechContentRegistry.EventRelicTypes.Contains(relicType), "external event relic should be registered");
		RunBeforeSavedPropertyCacheInitialization(() =>
			HextechRunesApi.RegisterEventRelic<ExternalRegistrationEventRelic>("HextechRunes.Tests"));
		Equal(1, HextechExternalContentRegistry.GetEventRelicTypes().Count(type => type == relicType), "idempotent event relic registration count");
	}

	[HextechTest]
	private static void ExternalForgeRegistrationUpdatesCatalog()
	{
		Type forgeType = typeof(ExternalRegistrationForge);
		Expect(!HextechContentRegistry.AllForgeTypes.Contains(forgeType), "external forge should not be registered initially");
		RunBeforeSavedPropertyCacheInitialization(() =>
			HextechRunesApi.RegisterForge<ExternalRegistrationForge>(
				HextechRarityTier.Prismatic,
				"HextechRunes.Tests"));
		Expect(HextechContentRegistry.AllForgeTypes.Contains(forgeType), "external forge should enter all forge types");
		Expect(HextechContentRegistry.ForgeTypesByRarity[HextechRarityTier.Prismatic].Contains(forgeType), "external forge should enter prismatic pool");
		string forgeId = ModelDb.GetId(forgeType).Entry;
		Expect(HextechRuneConfiguration.NormalizeDisabledForgeIds([ forgeId ]).Contains(forgeId), "external forge should be accepted by disabled forge config");
	}

	[HextechTest]
	private static void ExternalConfigDisabledIdsPreserveUnloadedContent()
	{
		const string unloadedRuneId = "ExternalMod.UnloadedRune";
		const string unloadedForgeId = "ExternalMod.UnloadedForge";

		SetEqual(
			[ unloadedRuneId ],
			HextechPlayerRuneConfigIds.Normalize([ unloadedRuneId, unloadedRuneId, " " ]),
			"unloaded external rune disabled id should be preserved");
		SetEqual(
			[ unloadedForgeId ],
			HextechRuneConfiguration.NormalizeDisabledForgeIds([ unloadedForgeId, unloadedForgeId, " " ]),
			"unloaded external forge disabled id should be preserved");
	}

	[HextechTest]
	private static void ExternalEnchantmentIconRegistrationTracksPath()
	{
		ModelId id = ModelDb.GetId<ExternalRegistrationEnchantment>();
		const string iconPath = "res://HextechRunes.Tests/images/enchantments/externalRegistrationEnchantment.png";
		Expect(HextechExternalContentRegistry.GetEnchantmentIconPath(id) == null, "external enchantment icon should not be registered initially");
		RunBeforeSavedPropertyCacheInitialization(() =>
		{
			HextechRunesApi.RegisterSavedPropertyCarrier<ExternalRegistrationEnchantment>();
			HextechRunesApi.RegisterEnchantmentIcon<ExternalRegistrationEnchantment>(iconPath);
		});
		Equal(iconPath, HextechExternalContentRegistry.GetEnchantmentIconPath(id), "external enchantment icon path");

#if !STS2_109_OR_NEWER
		SavedProperties? props = SavedProperties.FromInternal(new ExternalRegistrationEnchantment(), id);
		Expect(
			props?.ints?.Any(static property => property.name == "PersistentCounter" && property.value == 7) == true,
			"0.107 explicit SavedProperty carrier registration should inject the property");
#endif
	}
}
