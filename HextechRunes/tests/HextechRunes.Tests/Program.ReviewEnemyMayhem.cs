using System.Reflection;
using HextechRunes;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 改名只改 C# 标识符：沃格莫特之灵的抽牌计数仍写旧 JSON 键；删掉的奥术重击字段出现在旧档里也能正常读。
	[HextechTest]
	private static void ReviewCombatTrackingSnapshotKeepsLegacyJsonKeys()
	{
		HextechMayhemCombatTrackingState state = new();
		state.WarmogsSpiritPlayerCardsDrawnThisCombat[1] = 5;
		string json = state.Serialize();
		Expect(json.Contains("\"PlayerCardsDrawnThisCombat\"", StringComparison.Ordinal), "Warmog draw counter keeps the legacy JSON key");
		Expect(!json.Contains("WarmogsSpirit", StringComparison.Ordinal), "the C# rename does not leak into the JSON");

		// 含已删除的「我饥饿」记账键（改为从出牌历史推算，#22）：旧存档读回时直接忽略。
		const string legacyJson = "{\"PlayerCardsDrawnThisCombat\":{\"1\":7},\"ArcanePunchPlayerAttackCardsPlayed\":3,\"EscapePlanTriggered\":[4,9],\"EightPennyGatePlayersTriggeredThisTurn\":[1],\"EightPennyGatePlayersTriggeredSecondThisTurn\":[1]}";
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(legacyJson);
		Equal(7, restored.WarmogsSpiritPlayerCardsDrawnThisCombat[1], "legacy Warmog counter restores into the renamed field");
		Expect(restored.EscapePlanTriggered.SetEquals([4u, 9u]), "HashSet fields restore through the cached accessor");

		restored.DevilsDanceTriggeredThisTurn.Add(3);
		restored.PreparePlayerSideTurnStart();
		Equal(0, restored.DevilsDanceTriggeredThisTurn.Count, "per-turn HashSet fields clear on the player turn start phase");
		Equal(2, restored.EscapePlanTriggered.Count, "combat-long HashSet fields survive a turn boundary");

		restored.Reset();
		Equal(0, restored.WarmogsSpiritPlayerCardsDrawnThisCombat.Count, "reset clears dictionaries");
		Equal(0, restored.EscapePlanTriggered.Count, "reset clears HashSets");
		Equal("", restored.Serialize(), "a cleared tracking state serializes as empty");
	}

	// TXT 同步脚本按字面量读描述阈值，效果类的常量必须与之一致；描述里的阈值占位符靠这张表填值，漏登记会原样显示占位符。
	[HextechTest]
	private static void ReviewEnemyHexScaledThresholdsMatchEffectConstants()
	{
		Dictionary<MonsterHexKind, (string Var, int Base)> expected = new()
		{
			[MonsterHexKind.HeavyHitter] = ("HpPerPercent", HeavyHitterEnemyHex.HpPerPercentPerPlayer),
			[MonsterHexKind.VitalitySurge] = ("HpPerPercent", VitalitySurgeEnemyHex.HpPerPercentPerPlayer),
			// 多多益善按全队遗物数除以人数取整，每 N 件（N=人数）+1%，效果类里没有单独的常量。
			[MonsterHexKind.MoreTheMerrier] = ("RelicsNeeded", 1),
			[MonsterHexKind.Porcupine] = ("HitsNeeded", PorcupineEnemyHex.HitsPerTriggerPerPlayer),
			[MonsterHexKind.HundredRefinements] = ("HitsNeeded", HundredRefinementsEnemyHex.HitsPerTriggerPerPlayer),
			[MonsterHexKind.AncientWine] = ("SkillsNeeded", AncientWineEnemyHex.SkillsPerTriggerPerPlayer),
			[MonsterHexKind.AnthonyBias] = ("CardsNeeded", AnthonyBiasEnemyHex.CardsPerPercentPerPlayer),
			[MonsterHexKind.MindToMatter] = ("CardsNeeded", MindToMatterEnemyHex.CardsPerPercentPerPlayer),
		};
		SetEqual(expected.Keys, MonsterHexCatalog.PlayerCountScaledThresholds.Keys, "scaled threshold kinds");
		foreach ((MonsterHexKind kind, (string Var, int Base) threshold) in MonsterHexCatalog.PlayerCountScaledThresholds)
		{
			Equal(expected[kind], threshold, $"{kind} description threshold matches the effect");
		}
	}

	[HextechTest]
	private static void ReviewEnemyMaxHpStepMultipliersScaleWithParty()
	{
		Equal(1.30m, HeavyHitterEnemyHex.ResolveMultiplier(10000m, 3), "capped multipliers stay capped for any party size");
		Equal(1.02m, VitalitySurgeEnemyHex.ResolveMultiplier(120m, 3), "Vitality Surge threshold scales with the party");
		Equal(16, HextechEnemyHexContext.ClampScalingPlayerCount(40), "scaling player count caps at sixteen");
		Equal(1, HextechEnemyHexContext.ClampScalingPlayerCount(0), "scaling player count never drops below one");
	}

	[HextechTest]
	private static void ReviewLoadedAssemblyLookupFindsLoadedAndMissesUnknown()
	{
		HextechLoadedAssemblyLookup own = new(typeof(HextechLoadedAssemblyLookup).Assembly.GetName().Name!, StringComparison.Ordinal);
		Expect(own.Find() == typeof(HextechLoadedAssemblyLookup).Assembly, "a loaded assembly is found by name");
		Expect(own.Find() == typeof(HextechLoadedAssemblyLookup).Assembly, "the cached result is stable");
		HextechLoadedAssemblyLookup missing = new("HextechRunes.Tests.NoSuchAssembly", StringComparison.Ordinal);
		Expect(missing.Find() == null, "an absent soft dependency resolves to null");
		Expect(missing.Find() == null, "the negative result is cached until another assembly loads");
	}

	// 单机（或非联机战斗）时，Power 的「每回合 1 次」退回调用方自己的本地标记。
	[HextechTest]
	private static void ReviewOwnerTurnProcFallsBackToLocalFlagOutsideNetworkCombat()
	{
		bool triggered = false;
		Expect(!HextechCombatProcTracker.HasOwnerTurnProcTriggered(null, "review-proc", triggered), "not triggered yet");
		Expect(HextechCombatProcTracker.TryConsumeOwnerTurnProc(null, "review-proc", ref triggered), "first consume succeeds");
		Expect(triggered, "the local flag records the trigger");
		Expect(HextechCombatProcTracker.HasOwnerTurnProcTriggered(null, "review-proc", triggered), "triggered after consuming");
		Expect(!HextechCombatProcTracker.TryConsumeOwnerTurnProc(null, "review-proc", ref triggered), "second consume in the same turn fails");
	}
}
