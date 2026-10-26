namespace HextechRunes;

internal sealed class MoreTheMerrierEnemyHex : HextechEnemyHexEffect, IHextechEnemyMaxHpCoefficientProvider
{
	internal override MonsterHexKind Kind => MonsterHexKind.MoreTheMerrier;

	internal override int PersistentOrder => 37;

	// 最大生命与由心及物同一做法：开战时的所有敌人、战斗中新加入的敌人各结算一次，首领转阶段重放时随最大生命一起补算。
	internal override async Task ApplyPersistentToEnemy(HextechEnemyHexContext context, Creature creature, int? maxHpBaseOverride, bool replayOneShotPowers)
	{
		if (HextechCombatProcTracker.TryMarkPersistentHexApplied(context.Tracking.MoreTheMerrierApplied, creature, replayOneShotPowers))
		{
			await context.Modifier.ReapplyMonsterMaxHpCoefficients(creature, maxHpBaseOverride);
		}
	}

	public decimal GetMaxHpBonusFraction(HextechEnemyHexContext context, Creature creature)
	{
		return GetMultiplier(context) - 1m;
	}

	internal override decimal ModifyDamageMultiplicative(HextechEnemyHexContext context, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return GetMultiplier(context);
	}

	internal override decimal ModifyBlockMultiplicative(HextechEnemyHexContext context, Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return GetMultiplier(context);
	}

	internal override decimal ModifyEnemyHealMultiplicative(HextechEnemyHexContext context, Creature creature, decimal amount)
	{
		return GetMultiplier(context);
	}

	private static decimal GetMultiplier(HextechEnemyHexContext context)
	{
		// 与我方同名符文一致，统计原版遗物集合；全队合计后按人数分组，不逐人取整。
		int relicCount = context.RunState.Players.Sum(static player => player.Relics.Count);
		return 1m + (relicCount / context.ScalingPlayerCount) * 0.01m;
	}
}
