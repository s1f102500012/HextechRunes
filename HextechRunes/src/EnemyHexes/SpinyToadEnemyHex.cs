namespace HextechRunes;

internal sealed class SpinyToadEnemyHex : HextechEnemyHexEffect
{
	private const int RoundsPerTrigger = 2;

	internal override MonsterHexKind Kind => MonsterHexKind.SpinyToad;

	// 偶数回合开始时（玩家回合开始），所有敌人获得只持续到玩家回合结束的荆棘；实际增加的层数记在追踪状态里，结束时原数撤回。
	internal override async Task BeforePlayerSideTurnStart(
		HextechEnemyHexContext context,
		HextechCombatState combatState,
		IReadOnlyList<Creature> players)
	{
		if (players.Count == 0
			|| !context.TryConsumeRoundInterval(Kind, combatState, RoundsPerTrigger))
		{
			return;
		}

		int thorns = context.TierValue(Kind, 1, 3, 5);
		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			if (enemy.CombatId is not uint combatId)
			{
				continue;
			}

			// 按施加前后的实际增量记账：其他效果改了施加量时，撤回的也正好是实际多出来的层数。
			int before = enemy.GetPowerAmount<ThornsPower>();
			await PowerCmd.Apply<ThornsPower>(enemy, thorns, enemy, null);
			int gained = enemy.GetPowerAmount<ThornsPower>() - before;
			if (gained > 0)
			{
				context.Tracking.EnemySpinyToadTemporaryThorns[combatId] =
					context.Tracking.EnemySpinyToadTemporaryThorns.GetValueOrDefault(combatId, 0) + gained;
			}
		}
	}

	internal override async Task BeforeTurnEnd(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, CombatRoom? combatRoom)
	{
		if (side == CombatSide.Player && combatRoom != null)
		{
			await RemoveTemporaryThorns(context, combatRoom.CombatState.Enemies);
		}
	}

	// 兜底：玩家回合结束的回调没走到（读档、异常中断）时，敌方回合开始前也撤回。
	internal override async Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (side == CombatSide.Enemy)
		{
			await RemoveTemporaryThorns(context, combatState.Enemies);
		}
	}

	private static async Task RemoveTemporaryThorns(HextechEnemyHexContext context, IReadOnlyList<Creature> enemies)
	{
		if (context.Tracking.EnemySpinyToadTemporaryThorns.Count == 0)
		{
			return;
		}

		foreach ((uint combatId, int thorns) in context.Tracking.EnemySpinyToadTemporaryThorns.ToList())
		{
			Creature? enemy = enemies.FirstOrDefault(creature => creature.CombatId == combatId);
			if (enemy is { IsAlive: true })
			{
				await PowerCmd.Apply<ThornsPower>(enemy, -thorns, enemy, null);
			}
		}

		context.Tracking.EnemySpinyToadTemporaryThorns.Clear();
	}
}
