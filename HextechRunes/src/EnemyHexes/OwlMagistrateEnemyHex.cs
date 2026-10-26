namespace HextechRunes;

internal sealed class OwlMagistrateEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.OwlMagistrate;

	// 翱翔（HextechSoarPower）每层管一回合，敌方回合结束时减 1 层；层数固定 1/2/3，不随人数缩放。
	// “前 N 回合”按整场战斗算：首领转阶段不重发（不传 replayOneShotPowers），中途召唤的敌人不走开局入口。
	internal override async Task ApplyOpeningCombatStartToEnemy(
		HextechEnemyHexContext context,
		Creature creature,
		CombatRoom room,
		bool replayOneShotPowers)
	{
		if (HextechCombatProcTracker.TryMarkPersistentHexApplied(
			context.Tracking.OwlMagistrateApplied,
			creature))
		{
			await HextechEnemyPowerScalingHooks.ApplyExact<HextechSoarPower>(creature, context.TierValue(Kind, 1, 2, 3), creature, null);
		}
	}
}
