#if STS2_114_OR_NEWER
namespace HextechRunes;

// 0.114.0 起原版每次伤害先在 CreatureCmd.GetDamageResults 里以 dryRun 试算（卡面伤害预览也走这条路），
// 再在 ApplyDamageResults 里用试算得到的失血量落实；LoseHpInternal 返回 HpLossResult，DamageResult 由原版自己组装。
// 试算时只返回濒死推演的结果（失血量不按当前生命截断，与旧版本返回的 UnblockedDamage 同口径），
// 落实时原版传回的正是这个失血量，再写生命与负血债务。
internal static partial class HextechCombatHooks
{
	[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal), typeof(decimal), typeof(bool))]
	[HextechPatch("combat.near-death-feast.lose-hp", "濒死狂宴 / 敌方海克斯:濒死狂宴", Rune = typeof(NearDeathFeastRune), MonsterHexes = [MonsterHexKind.NearDeathFeast])]
	private static class NearDeathFeastLoseHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature __instance, decimal amount, bool dryRun, ref HpLossResult __result)
		{
			if (NearDeathFeastRune.ShouldInterceptLoseHp(__instance, amount))
			{
				__result = ToHpLossResult(NearDeathFeastRune.LoseHpAllowingDying(__instance, amount, dryRun));
				return false;
			}

			if (HextechEnemyNearDeath.ShouldInterceptLoseHp(__instance, amount))
			{
				__result = ToHpLossResult(HextechEnemyNearDeath.LoseHpAllowingDying(__instance, amount, dryRun));
				return false;
			}

			return true;
		}

		private static HpLossResult ToHpLossResult(HextechNearDeathHpLoss outcome)
		{
			return new HpLossResult
			{
				hpLost = outcome.HpLoss,
				killed = outcome.Killed,
				overkillAmount = outcome.OverkillDamage
			};
		}
	}
}
#endif
