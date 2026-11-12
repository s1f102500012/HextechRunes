namespace HextechRunes;

internal sealed class RepulsorEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Repulsor;

	internal override async Task AfterEnemyHealthThreshold(HextechEnemyHexContext context, Creature target, uint combatId)
	{
		// 立即触发:掉到阈值以下的瞬间就获得滑溜,不等到下个回合。
		if (!context.Tracking.RepulsorTriggered.Add(combatId))
		{
			return;
		}

		await HextechEnemyPowerScalingHooks.Apply<SlipperyPower>(target, context.TierValue(Kind, 1, 2, 3), target, null);
	}
}
