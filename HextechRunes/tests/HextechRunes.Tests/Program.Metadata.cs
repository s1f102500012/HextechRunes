using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using HextechRunes;
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
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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
	private static void CombatTrackingPerTurnProcLimitsResetOncePerRound()
	{
		HextechMayhemCombatTrackingState tracking = new();
		tracking.SlapProcsThisTurn[1] = 1;
		tracking.TormentorProcsThisTurn[2] = 1;
		tracking.CourageProcsThisTurn[3] = 1;
		tracking.BloodPactProcsThisTurn[4] = 1;
		tracking.PlayerRuneProcsThisTurn["1:rune"] = 1;
		tracking.PlayerRuneProcsThisTurn["2:rune"] = 1;
		tracking.ClownCollegeProcsThisTurn[5] = 1;
		tracking.DevilsDanceTriggeredThisTurn.Add(6);
		tracking.FinalFormTriggeredThisTurn.Add(7);
		tracking.EnemyPorcupineUnblockedHitsThisCombat[8] = 2;
		tracking.EnemyHundredRefinementsUnblockedHitsThisCombat[8] = 1;
		tracking.EightPennyGatePlayersTriggeredThisTurn.Add(9);
		tracking.EightPennyGatePlayersTriggeredSecondThisTurn.Add(10);
		tracking.InspectExtraDrawsPreventedThisTurn[11] = 2;
		tracking.GripPlayersTriggeredThisTurn.Add(12);

		tracking.PreparePlayerSideTurnEnd();

		Equal(1, tracking.ClownCollegeProcsThisTurn.Count, "player side end should keep clown college round proc count");
		Equal(1, tracking.EnemyPorcupineUnblockedHitsThisCombat.Count, "player side end should keep porcupine hit count");
		Equal(1, tracking.EightPennyGatePlayersTriggeredThisTurn.Count, "player side end should keep eight penny gate first round proc count");
		Equal(1, tracking.EightPennyGatePlayersTriggeredSecondThisTurn.Count, "player side end should keep eight penny gate second round proc count");
		Equal(1, tracking.InspectExtraDrawsPreventedThisTurn.Count, "player side end should keep inspect draw count");
		Equal(1, tracking.GripPlayersTriggeredThisTurn.Count, "player side end should keep grip proc count");

		tracking.PrepareEnemySideTurnStart();

		Equal(1, tracking.SlapProcsThisTurn.Count, "enemy side start should keep slap round proc count");
		Equal(1, tracking.TormentorProcsThisTurn.Count, "enemy side start should keep tormentor round proc count");
		Equal(1, tracking.CourageProcsThisTurn.Count, "enemy side start should keep courage round proc count");
		Equal(1, tracking.BloodPactProcsThisTurn.Count, "enemy side start should keep blood pact round proc count");
		Equal(2, tracking.PlayerRuneProcsThisTurn.Count, "enemy side start should keep player rune round proc count");
		Equal(1, tracking.ClownCollegeProcsThisTurn.Count, "enemy side start should keep clown college round proc count");
		Equal(1, tracking.DevilsDanceTriggeredThisTurn.Count, "enemy side start should keep devil's dance round proc count");
		Equal(1, tracking.FinalFormTriggeredThisTurn.Count, "enemy side start should keep final form round proc count");
		Equal(1, tracking.EnemyPorcupineUnblockedHitsThisCombat.Count, "enemy side start should keep porcupine hit count");
		Equal(1, tracking.EightPennyGatePlayersTriggeredThisTurn.Count, "enemy side start should keep eight penny gate first round proc count");
		Equal(1, tracking.EightPennyGatePlayersTriggeredSecondThisTurn.Count, "enemy side start should keep eight penny gate second round proc count");
		Equal(1, tracking.InspectExtraDrawsPreventedThisTurn.Count, "enemy side start should keep inspect draw count");
		Equal(1, tracking.GripPlayersTriggeredThisTurn.Count, "enemy side start should keep grip proc count");

		tracking.PreparePlayerSideTurnStart();
		Equal(2, tracking.PlayerRuneProcsThisTurn.Count, "player rune per-turn counts are not cleared for everyone at once");
		tracking.BeginPlayerTurnStart([2UL]);
		Expect(tracking.PlayerRuneProcsThisTurn.ContainsKey("1:rune"), "a teammate's extra turn keeps this player's per-turn count");
		Expect(!tracking.PlayerRuneProcsThisTurn.ContainsKey("2:rune"), "the player starting a turn gets fresh per-turn counts");
		tracking.BeginPlayerTurnStart([1UL, 2UL]);

		Equal(0, tracking.SlapProcsThisTurn.Count, "player side start should reset slap round proc count");
		Equal(0, tracking.TormentorProcsThisTurn.Count, "player side start should reset tormentor round proc count");
		Equal(0, tracking.CourageProcsThisTurn.Count, "player side start should reset courage round proc count");
		Equal(0, tracking.BloodPactProcsThisTurn.Count, "player side start should reset blood pact round proc count");
		Equal(0, tracking.PlayerRuneProcsThisTurn.Count, "player side start should reset player rune round proc count");
		Equal(0, tracking.ClownCollegeProcsThisTurn.Count, "player side start should reset clown college round proc count");
		Equal(0, tracking.DevilsDanceTriggeredThisTurn.Count, "player side start should reset devil's dance round proc count");
		Equal(0, tracking.FinalFormTriggeredThisTurn.Count, "player side start should reset final form round proc count");
		Equal(1, tracking.EnemyPorcupineUnblockedHitsThisCombat.Count, "porcupine hit count carries across turns");
		Equal(1, tracking.EnemyHundredRefinementsUnblockedHitsThisCombat.Count, "hundred refinements hit count carries across turns");
		Equal(0, tracking.EightPennyGatePlayersTriggeredThisTurn.Count, "player side start should reset eight penny gate first round proc count");
		Equal(0, tracking.EightPennyGatePlayersTriggeredSecondThisTurn.Count, "player side start should reset eight penny gate second round proc count");
		Equal(0, tracking.InspectExtraDrawsPreventedThisTurn.Count, "player side start should reset inspect draw count");
		Equal(0, tracking.GripPlayersTriggeredThisTurn.Count, "player side start should reset grip proc count");
	}

	[HextechTest]
	private static void MindOverMatterFirstDrawTrackingResetsPerPlayerTurn()
	{
		HextechMayhemCombatTrackingState tracking = new();
		Expect(MindOverMatterEnemyHex.TryConsumeFirstDraw(tracking, 11), "first draw for player one should trigger");
		Expect(!MindOverMatterEnemyHex.TryConsumeFirstDraw(tracking, 11), "second draw for player one should not trigger");
		Expect(MindOverMatterEnemyHex.TryConsumeFirstDraw(tracking, 22), "first draw for a different player should trigger independently");

		string serialized = tracking.Serialize();
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(serialized);
		SetEqual(new ulong[] { 11, 22 }, restored.MindOverMatterPlayersTriggeredThisTurn, "first-draw guards should survive a mid-turn save/load");

		restored.PrepareEnemySideTurnStart();
		Equal(2, restored.MindOverMatterPlayersTriggeredThisTurn.Count, "enemy side start should not reopen the player-turn first draw");

		restored.PreparePlayerSideTurnStart();
		Equal(0, restored.MindOverMatterPlayersTriggeredThisTurn.Count, "next player turn should reset first-draw guards");
		Expect(MindOverMatterEnemyHex.TryConsumeFirstDraw(restored, 11), "the next player turn should trigger again");
	}

	[HextechTest]
	private static void CombatTrackingGlobalProcOrdinalsSerializeAndReset()
	{
		Expect(!HextechRoundInterval.IsDue(1, 3), "round intervals should not trigger on round one");
		Expect(!HextechRoundInterval.IsDue(2, 3), "every-3 interval waits until round three");
		Expect(HextechRoundInterval.IsDue(3, 3), "every-3 interval triggers on round three");
		Expect(!HextechRoundInterval.IsDue(4, 3), "every-3 interval skips round four");
		Expect(HextechRoundInterval.IsDue(6, 3), "every-3 interval triggers again on round six");
		Expect(HextechRoundInterval.IsDue(2, 2), "every-2 interval triggers on round two");
		Expect(!HextechRoundInterval.IsDue(1, 1), "every-1 interval still skips round one");
		Expect(HextechRoundInterval.IsDue(2, 1) && HextechRoundInterval.IsDue(3, 1), "every-1 interval triggers every round after the first");
		Expect(!HextechRoundInterval.IsDue(4, 0), "nonpositive round intervals should stay disabled");

		HextechMayhemCombatTrackingState tracking = new();
		Equal(0, HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy-archmage:net:1"), "first global proc ordinal");
		Equal(1, HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy-archmage:net:1"), "second global proc ordinal");
		const string roundIntervalKey = "round-once:DivineIntervention:4";
		Equal(0, HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, roundIntervalKey), "first interval proc in a round");
		Equal(1, HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, roundIntervalKey), "extra turn should not repeat an interval proc");

		string serialized = tracking.Serialize();
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(serialized);

		Equal(2, restored.GlobalProcsThisCombat["enemy-archmage:net:1"], "global proc count should restore");
		Equal(2, restored.GlobalProcsThisCombat[roundIntervalKey], "round interval guard should restore");
		Equal(2, HextechCombatProcTracker.ConsumeGlobalProcInCombat(restored, "enemy-archmage:net:1"), "restored next global proc ordinal");

		restored.PreparePlayerSideTurnStart();
		Equal(3, restored.GlobalProcsThisCombat["enemy-archmage:net:1"], "global proc count should persist across turn reset");

		restored.Reset();
		Equal(0, restored.GlobalProcsThisCombat.Count, "global proc count should clear on combat tracking reset");
	}

	[HextechTest]
	private static void CombatTrackingPlayerRuneProcOrdinalPeekDoesNotConsume()
	{
		HextechMayhemCombatTrackingState tracking = new();
		Player player = CreateOrdinalTestPlayer(7);
		const string procKey = nameof(JeweledGauntletRune);

		Equal(0, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "peek before any play should read zero");
		Equal(0, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "repeated peeks must not mutate the ordinal");
		Equal(0, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "a speculative ModifyCardPlayCount re-evaluation must be side-effect free");

		Equal(0, HextechCombatProcTracker.ConsumePlayerRuneProcInCombat(tracking, player, procKey), "first real play should consume ordinal 0");
		Equal(1, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "peek after one real play should reflect the committed ordinal");
		Equal(1, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "peeking again before the next real play must not advance the ordinal");

		Equal(1, HextechCombatProcTracker.ConsumePlayerRuneProcInCombat(tracking, player, procKey), "second real play should consume ordinal 1");
		Equal(2, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(tracking, player, procKey), "ordinal should advance exactly once per real play, never per peek");
	}

	[HextechTest]
	private static void CombatTrackingSerializationIsCultureInvariant()
	{
		HextechMayhemCombatTrackingState tracking = new();
		// 大小写混合键：culture 比较排 a<B，ordinal 排 B<a，用来暴露 culture-sensitive 排序。
		HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy:net:1:apower");
		HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy:net:1:Bpower");
		HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy:net:1:co-op");
		HextechCombatProcTracker.ConsumeGlobalProcInCombat(tracking, "enemy:net:1:coop");
		tracking.MonsterMaxHpCoefficientBase[17] = 143;
		tracking.MonsterMaxHpCoefficientProjected[17] = 187;

		System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			List<string> serialized = [];
			foreach (string culture in new[] { "en-US", "zh-CN", "da-DK", "tr-TR" })
			{
				System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
				serialized.Add(tracking.Serialize());
			}

			for (int i = 1; i < serialized.Count; i++)
			{
				Equal(serialized[0], serialized[i], $"combat tracking serialization should be culture invariant (culture #{i})");
			}

			int upperIndex = serialized[0].IndexOf("Bpower", StringComparison.Ordinal);
			int lowerIndex = serialized[0].IndexOf("apower", StringComparison.Ordinal);
			Expect(upperIndex >= 0 && lowerIndex >= 0 && upperIndex < lowerIndex, "combat tracking keys should sort ordinally (B before a)");

			HextechMayhemCombatTrackingState restored = new();
			restored.Restore(serialized[0]);
			Equal(143, restored.MonsterMaxHpCoefficientBase[17], "enemy max HP coefficient base should survive combat tracking restore");
			Equal(187, restored.MonsterMaxHpCoefficientProjected[17], "enemy max HP coefficient projection should survive combat tracking restore");
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = original;
		}
	}

	[HextechTest]
	private static void SavedPropertyManifestMatchesCheckedInList()
	{
		ExpectSavedPropertyManifest("saved_property_manifest.txt", CollectSavedPropertyNames(typeof(HextechCatalog).Assembly, declaredInAssemblyOnly: false));
	}

	// declaredInAssemblyOnly=false 时也收入从原版基类继承的属性(本体清单一直如此,含 IsMelted/IsWax);
	// 拓展包清单只收本程序集声明的属性,继承自本体基类的由本体清单负责。
	private static string[] CollectSavedPropertyNames(Assembly assembly, bool declaredInAssemblyOnly)
	{
		Type abstractModelType = typeof(AbstractModel);
		const BindingFlags propertyFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (Type type in assembly.GetTypes())
		{
			if (type.IsAbstract || !type.IsClass || !abstractModelType.IsAssignableFrom(type))
			{
				continue;
			}

			foreach (PropertyInfo property in type.GetProperties(propertyFlags))
			{
				if (declaredInAssemblyOnly && property.DeclaringType?.Assembly != assembly)
				{
					continue;
				}

				bool isSavedProperty = property
					.GetCustomAttributes(inherit: true)
					.Any(static attr => attr.GetType().Name == "SavedPropertyAttribute");
				if (isSavedProperty)
				{
					names.Add(property.Name);
				}
			}
		}

		return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
	}

	private static void ExpectSavedPropertyManifest(string manifestFileName, string[] actual)
	{
		string manifestPath = Path.Combine(AppContext.BaseDirectory, manifestFileName);
		Expect(File.Exists(manifestPath), $"{manifestFileName} should exist at {manifestPath}");

		string[] expected = File.ReadAllLines(manifestPath)
			.Select(static line => line.Trim())
			.Where(static line => line.Length > 0 && !line.StartsWith('#'))
			.ToArray();
		SequenceEqual(
			expected,
			actual,
			$"SavedProperty manifest drift ({manifestFileName}); actual list:\n{string.Join("\n", actual)}");
	}

	[HextechTest]
	private static void SavedPropertyPreInitRegistrationLeavesWireTablesUntouched()
	{
#if STS2_109_OR_NEWER
		Type cacheType = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache);
		string[] wireFieldNames =
		[
			"_savedPropertyCache",
			"_propertyNameToNetIdMap",
			"_netIdToPropertyNameMap"
		];
		Dictionary<string, (object? Value, int? Count)> before = wireFieldNames.ToDictionary(
			static name => name,
			name => SnapshotStaticCollection(cacheType, name),
			StringComparer.Ordinal);
		RunBeforeSavedPropertyCacheInitialization(static () => HextechSavedPropertyBootstrap.InjectModelType(typeof(PreInitSavedPropertyCarrier)));

		foreach (string fieldName in wireFieldNames)
		{
			(object? afterValue, int? afterCount) = SnapshotStaticCollection(cacheType, fieldName);
			(object? beforeValue, int? beforeCount) = before[fieldName];
			Expect(ReferenceEquals(beforeValue, afterValue), $"{fieldName} instance should not change before official Init");
			Equal(beforeCount, afterCount, $"{fieldName} count before official Init");
		}
#else
		HextechSavedPropertyBootstrap.InjectModelType(typeof(PreInitSavedPropertyCarrier));
		ModelId id = ModelDb.GetId<PreInitSavedPropertyCarrier>();
		SavedProperties? properties = SavedProperties.FromInternal(new PreInitSavedPropertyCarrier(), id);
		Expect(
			properties?.ints?.Any(static property => property.name == "PreInitCounter") == true,
			"0.107 should still inject a SavedProperty carrier explicitly");
#endif
	}

	[HextechTest]
	private static void SavedPropertyLateCarrierRegistrationFailsClosed()
	{
#if STS2_109_OR_NEWER
		ExpectThrows<InvalidOperationException>(
			() => HextechRunesApi.RegisterSavedPropertyCarrier<LateSavedPropertyCarrier>(),
			"0.109 should reject a SavedProperty carrier missing from the initialized per-type cache");
#endif
	}

	[HextechTest]
	private static void SavedPropertySameNameCarrierStillRequiresPerTypeCache()
	{
#if STS2_109_OR_NEWER
		Type cacheType = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache);
		Action[] restore =
		[
			CaptureStaticCollectionRestore(cacheType, "_savedPropertyCache"),
			CaptureStaticCollectionRestore(cacheType, "_propertyNameToNetIdMap"),
			CaptureStaticCollectionRestore(cacheType, "_netIdToPropertyNameMap")
		];
		try
		{
			MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache
				.CacheSavedPropertiesForTypeDebug(typeof(SameNameSavedPropertyCarrierA));
			HextechSavedPropertyBootstrap.EnsureModelTypeRegistrationAllowed(
				typeof(SameNameSavedPropertyCarrierA));
			ExpectThrows<InvalidOperationException>(
				() => HextechSavedPropertyBootstrap.EnsureModelTypeRegistrationAllowed(
					typeof(SameNameSavedPropertyCarrierB)),
				"a globally known SavedProperty name must not hide a missing per-type carrier cache");
		}
		finally
		{
			foreach (Action restoreCollection in restore.Reverse())
			{
				restoreCollection();
			}
		}
#endif
	}

	[HextechTest]
	private static void SavedPropertyLateExternalRegistrationLeavesNoPartialState()
	{
#if STS2_109_OR_NEWER
		Type runeType = typeof(LateExternalRegistrationRune);
		int registryVersion = HextechExternalContentRegistry.Version;
		int registrationCount = HextechExternalContentRegistry
			.GetPlayerRuneRegistrations()
			.Count;
		Expect(
			!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
				typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool),
				runeType),
			"late test rune should not start in the shared relic pool queue");

		ExpectThrows<InvalidOperationException>(
			() => HextechRunesApi.RegisterPlayerRune<LateExternalRegistrationRune>(
				HextechRarityTier.Silver),
			"late external registration with an uncached SavedProperty should fail before mutation");

		Equal(registryVersion, HextechExternalContentRegistry.Version, "late failure registry version");
		Equal(
			registrationCount,
			HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count,
			"late failure registration count");
		Expect(
			!HextechExternalContentRegistry
				.GetPlayerRuneRegistrations()
				.Any(registration => registration.Type == runeType),
			"late failure should not enter the external player rune registry");
		Expect(
			!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
				typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool),
				runeType),
			"late failure should not enter the shared relic pool queue");
#endif
	}

	[HextechTest]
	private static void StableRandomPlayerIdentityUsesNetIdBeforeLocalSlot()
	{
		Equal("net:123456789", HextechStableRandom.PlayerIdentityKey(0, 123456789UL), "host-local slot");
		Equal("net:123456789", HextechStableRandom.PlayerIdentityKey(1, 123456789UL), "client-local slot");
		Equal("slot:2", HextechStableRandom.PlayerIdentityKey(2, 0UL), "local fallback");
	}

	[HextechTest]
	private static void StableRandomSequentialFloorsAvoidExcessClustering()
	{
		const int seedCount = 2048;
		const int floorCount = 24;
		double[] hitRates = new double[seedCount];
		double lagX = 0;
		double lagY = 0;
		double lagXX = 0;
		double lagYY = 0;
		double lagXY = 0;
		int lagPairs = 0;

		for (int seedIndex = 0; seedIndex < seedCount; seedIndex++)
		{
			string seed = $"TEST-SEED-{seedIndex:00000}";
			int hits = 0;
			int previousHit = -1;
			for (int floor = 1; floor <= floorCount; floor++)
			{
				int roll = HextechStableRandom.IndexFromRawParts(
					100,
					seed,
					"|act:",
					"0",
					"|floor:",
					floor.ToString(),
					"|",
					"dice-maniac-forge-reward",
					"|",
					"0:1",
					"|",
					"7");
				int hit = roll < 50 ? 1 : 0;
				hits += hit;
				if (previousHit >= 0)
				{
					lagX += previousHit;
					lagY += hit;
					lagXX += previousHit * previousHit;
					lagYY += hit * hit;
					lagXY += previousHit * hit;
					lagPairs++;
				}

				previousHit = hit;
			}

			hitRates[seedIndex] = (double)hits / floorCount;
		}

		double mean = hitRates.Average();
		double variance = hitRates.Select(rate => (rate - mean) * (rate - mean)).Average();
		double stdev = Math.Sqrt(variance);
		double lagMeanX = lagX / lagPairs;
		double lagMeanY = lagY / lagPairs;
		double lagVarianceX = lagXX / lagPairs - lagMeanX * lagMeanX;
		double lagVarianceY = lagYY / lagPairs - lagMeanY * lagMeanY;
		double lagCorrelation = (lagXY / lagPairs - lagMeanX * lagMeanY) / Math.Sqrt(lagVarianceX * lagVarianceY);

		Expect(mean is > 0.48 and < 0.52, $"stable random 50% mean should stay unbiased, got {mean:F4}");
		Expect(stdev < 0.11, $"stable random sequential floor stdev should not show excess clustering, got {stdev:F4}");
		Expect(Math.Abs(lagCorrelation) < 0.02, $"stable random lag-1 correlation should stay near zero, got {lagCorrelation:F4}");
	}

	[HextechTest]
	private static void StableRandomPowerOfTwoIndexesAvoidTerminalCounterCycle()
	{
		int[] circleTargets = Enumerable.Range(0, 8)
			.Select(historyCount => HextechStableRandom.IndexFromRawParts(
				4,
				"TEST-SEED",
				"|act:",
				"0",
				"|floor:",
				"12",
				"|",
				"circle-of-death-target",
				"|",
				"0:1",
				"|",
				"1",
				"|",
				"12",
				"|",
				historyCount.ToString()))
			.ToArray();

		int[] miseryTargets = Enumerable.Range(1, 8)
			.Select(roundNumber => HextechStableRandom.IndexFromRawParts(
				4,
				"TEST-SEED",
				"|act:",
				"0",
				"|floor:",
				"12",
				"|",
				"misery-target",
				"|",
				"0:1",
				"|",
				roundNumber.ToString()))
			.ToArray();

		Expect(!IsModuloStepCycle(circleTargets, 4), $"circle-of-death target sequence should not be a fixed modulo cycle: [{string.Join(", ", circleTargets)}]");
		Expect(!IsModuloStepCycle(miseryTargets, 4), $"misery target sequence should not be a fixed modulo cycle: [{string.Join(", ", miseryTargets)}]");
	}

	[HextechTest]
	private static void PlayerRuneMetadataClassifiesConfigStates()
	{
		PlayerRuneMetadataCatalog metadata = HextechContentRegistry.PlayerRuneMetadata;
		PlayerRuneRegistration defaultDisabled = metadata.Registrations.First(registration =>
			metadata.HasFlag(registration.Type, PlayerRuneFlags.Disabled)
			&& !metadata.HasFlag(registration.Type, PlayerRuneFlags.SelectionExcluded));

		Expect(!metadata.IsVisible(defaultDisabled.Type), "default disabled rune should not be visible by default");
		Expect(metadata.IsConfigurable(defaultDisabled.Type), "default disabled rune should remain configurable");

		PlayerRuneRegistration selectionExcluded = metadata.Registrations.First(registration =>
			metadata.HasFlag(registration.Type, PlayerRuneFlags.SelectionExcluded)
			&& !metadata.HasFlag(registration.Type, PlayerRuneFlags.Disabled));
		Expect(metadata.IsVisible(selectionExcluded.Type), "selection excluded rune should still be visible");
		Expect(!metadata.IsConfigurable(selectionExcluded.Type), "selection excluded rune should not be configurable");
	}

	[HextechTest]
	private static void MonsterHexMetadataKeepsDisabledKindsOutOfRarityPools()
	{
		MonsterHexMetadataCatalog metadata = HextechContentRegistry.MonsterHexMetadata;
		foreach (MonsterHexRegistration disabled in HextechMonsterHexRegistry.Registrations.Where(static registration => registration.Disabled))
		{
			Expect(metadata.AllKinds.Contains(disabled.Kind), $"{disabled.Kind} should stay in all-kinds set");
			Equal(disabled.Rarity, metadata.RarityByKind[disabled.Kind], $"{disabled.Kind} keeps its rarity");
			Equal(disabled.IconRelicType, metadata.IconRelicTypes[disabled.Kind], $"{disabled.Kind} keeps its icon relic type");
			Expect(!metadata.EnabledKindsByRarity.Values.Any(kinds => kinds.Contains(disabled.Kind)), $"{disabled.Kind} should not appear in any rarity pool");
		}
	}

	[HextechTest]
	private static void EnemyHexHoverTipsUseExpectedPowerModels()
	{
		foreach (MonsterHexKind hex in HextechContentRegistry.MonsterHexMetadata.AllKinds)
		{
			IReadOnlyList<Type> powerTypes = MonsterHexCatalog.GetEnemyHexPowerHoverTipTypes(hex);
			Equal(powerTypes.Count, powerTypes.Distinct().Count(), $"enemy {hex} hover-tip power types should be unique");
			foreach (Type powerType in powerTypes)
			{
				Expect(typeof(PowerModel).IsAssignableFrom(powerType), $"enemy {hex} hover-tip type should be a power model: {powerType}");
			}
		}
	}

}
