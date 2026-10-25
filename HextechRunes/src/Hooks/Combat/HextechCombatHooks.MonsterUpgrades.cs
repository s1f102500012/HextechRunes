using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static bool TryGetLivingEnemyHexModifier(
		Creature creature,
		MonsterHexKind kind,
		[NotNullWhen(true)] out HextechMayhemModifier? modifier)
	{
		modifier = null;
		return !creature.IsDead && TryGetActiveEnemyHexModifier(creature, kind, out modifier);
	}

	private static void AddMonsterUpgradeIntents(MonsterModel monster, MoveState move, bool rollTheft)
	{
		Creature creature = monster.Creature;
		if (creature.Side != CombatSide.Enemy
			|| creature.IsDead
			|| MoveStateIntentsField == null
			|| HextechMayhemModifier.FindIn(creature.CombatState?.RunState) is not HextechMayhemModifier modifier)
		{
			return;
		}

		HextechEnemyHexContext context = new(modifier);
		bool strength = context.IsActive(MonsterHexKind.CeremonialBeast);
		bool theft = context.IsActive(MonsterHexKind.ThievingHopper);
		int dazed = context.IsActive(MonsterHexKind.Chomper) ? ChomperEnemyHex.GetDazedCount(context) : 0;
		if (!strength && !theft && dazed <= 0)
		{
			return;
		}

		// 原版没有改写意图的 Hook；保留行动对象及原委托，避免破坏怪物状态机的引用判定。
		bool planTheft = rollTheft
			&& theft
			&& ThievingHopperEnemyHex.CanPlanTheft(creature, context)
			&& ThievingHopperEnemyHex.IsBelowTheftThreshold(creature);
		int strengthAmount = strength ? context.TierValue(MonsterHexKind.CeremonialBeast, 1, 2, 3) : 0;
		MoveStateIntentsField.SetValue(move, ComposeMonsterUpgradeIntents(move.Intents, creature, strengthAmount, planTheft, dazed));
	}

	/// <summary>偷窃草蜢：把偷牌意图补进当前已显示的下一次行动并刷新意图显示。</summary>
	internal static async Task PlanThievingHopperTheftNow(MonsterModel monster)
	{
		MoveState move = monster.NextMove;
		if (move.Intents.Any(static intent => intent is ThievingHopperTheftIntent))
		{
			return;
		}

		AddMonsterUpgradeIntents(monster, move, rollTheft: true);
		if (!move.Intents.Any(static intent => intent is ThievingHopperTheftIntent)
			|| monster.Creature.CombatState is not { } combatState
			|| NCombatRoom.Instance?.GetCreatureNode(monster.Creature) is not { } node)
		{
			return;
		}

		try
		{
			await node.UpdateIntent(combatState.Allies);
		}
		catch (ObjectDisposedException ex)
		{
			HextechLog.Warn("ThievingHopper", $"Intent refresh skipped: {ex.Message}");
		}
	}

	internal static AbstractIntent[] ComposeMonsterUpgradeIntents(
		IReadOnlyList<AbstractIntent> original,
		Creature source,
		int strength,
		bool theft,
		int dazed = 0)
	{
		List<AbstractIntent> intents = original
			.Where(static intent => intent is not CeremonialBeastStrengthIntent and not ThievingHopperTheftIntent and not ChomperDazedIntent)
			.ToList();
		bool attacks = intents.Any(static intent => intent is AttackIntent);
		if (strength > 0 && attacks)
		{
			intents.Add(new CeremonialBeastStrengthIntent(source, strength));
		}

		if (theft)
		{
			intents.Add(new ThievingHopperTheftIntent(source));
		}

		// 升级：啃咬机：只补在不含攻击的行动上（按原版意图判断，不算本模组补的意图）。
		// 逃跑行动执行完敌人已离场、失去战斗归属，附加的晕眩结算不了，不补意图，免得显示了却不加牌。
		bool escapes = intents.Any(static intent => intent is EscapeIntent);
		if (dazed > 0 && !attacks && !escapes)
		{
			intents.Add(new ChomperDazedIntent(source, dazed));
		}

		return intents.ToArray();
	}

	internal static async Task CompleteMonsterUpgradeMove(Task original, CeremonialBeastStrengthIntent? strength, ThievingHopperTheftIntent? theft, ChomperDazedIntent? dazed = null)
	{
		await original;
		if (dazed != null
			&& TryGetLivingEnemyHexModifier(dazed.Source, MonsterHexKind.Chomper, out _)
			&& dazed.Source.CombatState is { } combatState
			&& !CombatManager.Instance.IsOverOrEnding)
		{
			Creature[] targets = combatState.PlayerCreatures.Where(static creature => creature.IsAlive).ToArray();
			if (targets.Length > 0)
			{
				await CardPileCmd.AddToCombatAndPreview<Dazed>(targets, PileType.Discard, dazed.CardCount, null);
			}
		}

		if (strength != null && TryGetLivingEnemyHexModifier(strength.Source, MonsterHexKind.CeremonialBeast, out _))
		{
			await PowerCmd.Apply<StrengthPower>(strength.Source, strength.Strength, strength.Source, null);
		}

		if (theft != null && TryGetLivingEnemyHexModifier(theft.Source, MonsterHexKind.ThievingHopper, out HextechMayhemModifier? theftModifier))
		{
			await ThievingHopperEnemyHex.StealAndPlanEscape(new HextechEnemyHexContext(theftModifier), theft.Source);
		}
	}

	[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.RollMove), typeof(IEnumerable<Creature>))]
	[HextechPatch("combat.monster-upgrades.roll-intents", "升级：仪式兽、升级：偷窃草蜢、升级：啃咬机")]
	private static class MonsterUpgradeRollIntentsPatch
	{
		[HarmonyPostfix]
		private static void Postfix(MonsterModel __instance) => AddMonsterUpgradeIntents(__instance, __instance.NextMove, rollTheft: true);
	}

	[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.SetMoveImmediate), typeof(MoveState), typeof(bool))]
	[HextechPatch("combat.monster-upgrades.immediate-intents", "升级：仪式兽、升级：啃咬机")]
	private static class MonsterUpgradeImmediateIntentsPatch
	{
		[HarmonyPrefix]
		private static void Prefix(MonsterModel __instance, MoveState state) => AddMonsterUpgradeIntents(__instance, state, rollTheft: false);
	}

	[HarmonyPatch(typeof(MoveState), nameof(MoveState.PerformMove), typeof(IEnumerable<Creature>))]
	[HextechPatch("combat.monster-upgrades.perform-move", "升级：仪式兽、升级：偷窃草蜢、升级：啃咬机")]
	private static class MonsterUpgradePerformMovePatch
	{
		[HarmonyPostfix]
		private static void Postfix(MoveState __instance, ref Task __result)
		{
			CeremonialBeastStrengthIntent? strength = __instance.Intents.OfType<CeremonialBeastStrengthIntent>().FirstOrDefault();
			ThievingHopperTheftIntent? theft = __instance.Intents.OfType<ThievingHopperTheftIntent>().FirstOrDefault();
			ChomperDazedIntent? dazed = __instance.Intents.OfType<ChomperDazedIntent>().FirstOrDefault();
			if (strength != null || theft != null || dazed != null)
			{
				__result = CompleteMonsterUpgradeMove(__result, strength, theft, dazed);
			}
		}
	}
}
