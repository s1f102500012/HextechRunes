namespace HextechRunes;

internal sealed class ShrinkEngineEnemyHex : HextechEnemyHexEffect
{
	internal const float BodyScalePerStack = 0.02f;

	internal override MonsterHexKind Kind => MonsterHexKind.ShrinkEngine;

	internal override async Task BeforePlayerSideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			if (enemy.GetPowerAmount<SlipperyPower>() <= 0m)
			{
				// 0/N/2N 层（N=联机人数）：层数已按人数算好，绕过原版对敌方滑溜的联机缩放。
				await HextechEnemyPowerScalingHooks.ApplyExact<SlipperyPower>(enemy, context.TierValue(Kind, 0, 1, 2) * context.ScalingPlayerCount, enemy, null);
			}
		}
	}

	internal override Task BeforeEnemySideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players, IReadOnlyList<Creature> enemies)
	{
		foreach (Creature enemy in enemies)
		{
			if (enemy.CombatId == null)
			{
				continue;
			}

			uint combatId = enemy.CombatId.Value;
			context.Tracking.ShrinkEngineStacks[combatId] = context.Tracking.ShrinkEngineStacks.GetValueOrDefault(combatId, 0) + 1;
			context.UpdateEnemyScale(enemy);
		}

		return Task.CompletedTask;
	}
}
