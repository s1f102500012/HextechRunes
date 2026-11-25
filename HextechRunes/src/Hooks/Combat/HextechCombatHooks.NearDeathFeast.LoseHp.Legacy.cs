#if !STS2_114_OR_NEWER
namespace HextechRunes;

// 0.113.x 及更早：Creature.LoseHpInternal(decimal, ValueProp) 直接扣血并返回 DamageResult。
internal static partial class HextechCombatHooks
{
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
				__result = ToDamageResult(NearDeathFeastRune.LoseHpAllowingDying(__instance, amount), __instance, props);
				return false;
			}

			if (HextechEnemyNearDeath.ShouldInterceptLoseHp(__instance, amount))
			{
				__result = ToDamageResult(HextechEnemyNearDeath.LoseHpAllowingDying(__instance, amount), __instance, props);
				return false;
			}

			return true;
		}

		// DamageResult 的 UnblockedDamage / WasTargetKilled / OverkillDamage 都是 public init，直接构造即可。
		private static DamageResult ToDamageResult(HextechNearDeathHpLoss outcome, Creature creature, ValueProp props)
		{
			return new DamageResult(creature, props)
			{
				UnblockedDamage = outcome.HpLoss,
				WasTargetKilled = outcome.Killed,
				OverkillDamage = outcome.OverkillDamage
			};
		}
	}
}
#endif
