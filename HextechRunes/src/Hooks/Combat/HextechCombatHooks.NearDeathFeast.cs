using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.Combat;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// NHealthBar._creature 与 _hpLabel（0.107.1/0.111.0 原版私有字段）：濒死时血条显示负血量用；
	// 缺失时只跳过这段显示（Prepare 返回 false），不连带把濒死狂宴整个标为不可用。
	private static readonly FieldInfo HealthBarCreatureField = TryGetField(typeof(NHealthBar), "_creature")!;
	private static readonly FieldInfo HealthBarHpLabelField = TryGetField(typeof(NHealthBar), "_hpLabel")!;

	private static void NearDeathFeastKillPrefix(Creature creature)
	{
		NearDeathFeastRune.ForceDeathThresholdForKill(creature);
		HextechEnemyNearDeath.ForceDeathThresholdForKill(creature);
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal), typeof(decimal), typeof(ValueProp))]
	[HextechPatch("combat.near-death-feast.lose-hp", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastLoseHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature __instance, decimal amount, ValueProp props, ref DamageResult __result)
		{
			if (NearDeathFeastRune.ShouldInterceptLoseHp(__instance, amount))
			{
				__result = NearDeathFeastRune.LoseHpAllowingDying(__instance, amount, props);
				return false;
			}

			if (HextechEnemyNearDeath.ShouldInterceptLoseHp(__instance, amount))
			{
				__result = HextechEnemyNearDeath.LoseHpAllowingDying(__instance, amount, props);
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.CurrentHp), MethodType.Setter)]
	[HextechPatch("combat.near-death-feast.current-hp", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastCurrentHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature __instance, int value)
		{
			if (value >= 0)
			{
				// 敌方转阶段/接续/复活把 HP 设回正值:清掉残留的濒死状态。
				// 注意只认 >1:濒死维持本身就是把 HP 写成 1(LoseHpAllowingDying 内部的
				// SetCurrentHpInternal 会走本 setter),按 1 清会把刚记下的负血债务当场抹掉。
				if (value > 1)
				{
					HextechEnemyNearDeath.ClearIfRecovered(__instance, value);
				}

				return true;
			}

			if (NearDeathFeastRune.HasDyingState(__instance))
			{
				NearDeathFeastRune.PreserveNegativeHpAsDyingState(__instance, value);
				return false;
			}

			if (HextechEnemyNearDeath.HasDyingState(__instance))
			{
				HextechEnemyNearDeath.PreserveNegativeHpAsDyingState(__instance, value);
				return false;
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.IsAlive), MethodType.Getter)]
	[HextechPatch("combat.near-death-feast.is-alive", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastIsAlivePatch
	{
		[HarmonyPostfix]
		private static void Postfix(Creature __instance, ref bool __result)
		{
			if (!__result && (NearDeathFeastRune.IsDyingButAlive(__instance) || HextechEnemyNearDeath.IsDyingButAlive(__instance)))
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.IsDead), MethodType.Getter)]
	[HextechPatch("combat.near-death-feast.is-dead", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastIsDeadPatch
	{
		[HarmonyPostfix]
		private static void Postfix(Creature __instance, ref bool __result)
		{
			if (__result && (NearDeathFeastRune.IsDyingButAlive(__instance) || HextechEnemyNearDeath.IsDyingButAlive(__instance)))
			{
				__result = false;
			}
		}
	}

	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool))]
	[HextechPatch("combat.near-death-feast.gain-block", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastGainBlockPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(Creature creature, ref Task<decimal> __result)
		{
			if (!NearDeathFeastRune.IsDyingButAlive(creature) && !HextechEnemyNearDeath.IsDyingButAlive(creature))
			{
				return true;
			}

			__result = Task.FromResult(0m);
			return false;
		}
	}

	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool))]
	[HextechPatch("combat.near-death-feast.gain-block-var", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastGainBlockVarPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature creature, ref Task<decimal> __result)
		{
			return NearDeathFeastGainBlockPatch.Prefix(creature, ref __result);
		}
	}

	// 0.113.0 起伤害、灾厄等击杀直接调用带击杀来源卡的重载，旧签名只转发到它；只挂新重载，避免旧签名调用触发两次。
#if STS2_113_OR_NEWER
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(Creature), typeof(CardModel), typeof(bool))]
#else
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(Creature), typeof(bool))]
#endif
	[HextechPatch("combat.near-death-feast.kill", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastKillPatch
	{
		[HarmonyPrefix]
		private static void Prefix(Creature creature) => NearDeathFeastKillPrefix(creature);
	}

#if STS2_113_OR_NEWER
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(CardModel), typeof(bool))]
#else
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(bool))]
#endif
	[HextechPatch("combat.near-death-feast.kill-many", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastKillManyPatch
	{
		[HarmonyPrefix]
		private static void Prefix(IReadOnlyCollection<Creature> creatures)
		{
			foreach (Creature creature in creatures)
			{
				NearDeathFeastKillPrefix(creature);
			}
		}
	}

#if STS2_113_OR_NEWER
	[HarmonyPatch(typeof(CreatureCmd), "KillWithoutCheckingWinCondition", typeof(Creature), typeof(CardModel), typeof(bool), typeof(int))]
#else
	[HarmonyPatch(typeof(CreatureCmd), "KillWithoutCheckingWinCondition", typeof(Creature), typeof(bool), typeof(int))]
#endif
	[HextechPatch("combat.near-death-feast.kill-no-win-check", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastKillWithoutWinCheckPatch
	{
		[HarmonyPrefix]
		private static void Prefix(Creature creature) => NearDeathFeastKillPrefix(creature);
	}

	[HarmonyPatch(typeof(NHealthBar), "RefreshText")]
	[HextechPatch("combat.near-death-feast.health-bar-text", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastHealthBarTextPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => HealthBarCreatureField != null && HealthBarHpLabelField != null;

		[HarmonyPostfix]
		private static void Postfix(NHealthBar __instance)
		{
			if (HealthBarCreatureField.GetValue(__instance) is not Creature creature
				|| HealthBarHpLabelField.GetValue(__instance) is not MegaLabel hpLabel)
			{
				return;
			}

			if (NearDeathFeastRune.TryGetDisplayedHp(creature, out int displayedHp)
				|| HextechEnemyNearDeath.TryGetDisplayedHp(creature, out displayedHp))
			{
				hpLabel.SetTextAutoSize($"{displayedHp}/{creature.MaxHp}");
			}
		}
	}
}
