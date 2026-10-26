namespace HextechRunes;

internal sealed class ScrollOfBitingEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.ScrollOfBiting;

	// 纸伤难愈：该敌人每对玩家造成未被格挡的伤害，玩家失去层数点最大生命（永久）。层数固定 0/1/2，不随人数缩放。
	internal override async Task ApplyOpeningCombatStartToEnemy(
		HextechEnemyHexContext context,
		Creature creature,
		CombatRoom room,
		bool replayOneShotPowers)
	{
		int stacks = context.TierValue(Kind, 0, 1, 2);
		if (stacks > 0
			&& HextechCombatProcTracker.TryMarkPersistentHexApplied(
				context.Tracking.ScrollOfBitingApplied,
				creature,
				replayOneShotPowers))
		{
			await HextechEnemyPowerScalingHooks.ApplyExact<PaperCutsPower>(creature, stacks, creature, null);
		}
	}
}
