namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 蛋白粉奶昔(敌方)：敌人获得格挡时先回复生命，回满后剩余的部分仍为格挡；生命已满时原样放行。BlockVar 重载内部转调这个 decimal 重载，只补这一处。
	// 濒死的敌人交给濒死狂宴的前缀（同样不给格挡），这里不回血；跳过型前缀按约定用 Priority.Low，两者谁先执行结果都一样。
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool))]
	[HextechPatch("combat.protein-shake.gain-block", "蛋白粉奶昔")]
	private static class ProteinShakeGainBlockPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay, bool fast, ref Task<decimal> __result)
		{
			if (CombatManager.Instance.IsOverOrEnding
				|| HextechEnemyNearDeath.IsDyingButAlive(creature)
				|| !TryGetLivingEnemyHexModifier(creature, MonsterHexKind.ProteinShake, out _)
				|| !ProteinShakeEnemyHex.ShouldConvert(creature))
			{
				return true;
			}

			__result = ProteinShakeEnemyHex.HealThenBlock(creature, amount, props, cardPlay, fast);
			return false;
		}
	}
}
