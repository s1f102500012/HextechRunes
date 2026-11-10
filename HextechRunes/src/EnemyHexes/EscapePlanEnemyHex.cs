namespace HextechRunes;

internal sealed class EscapePlanEnemyHex : HextechEnemyHexEffect
{
	private const decimal BlockPercent = 0.6m;

	internal override MonsterHexKind Kind => MonsterHexKind.EscapePlan;

	internal override async Task BeforePlayerSideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		// 兜底:进场时就已低于阈值、没触发过伤害阈值 hook 的敌人,在回合开始时补发一次。
		foreach (Creature creature in context.GetAliveEnemies(combatState))
		{
			if (creature.CombatId == null
				|| context.Tracking.EscapePlanTriggered.Contains(creature.CombatId.Value)
				|| creature.CurrentHp >= creature.MaxHp * EnemyHealthThresholdPercent)
			{
				continue;
			}

			await ApplyEscapePlan(context, creature);
		}
	}

	internal override Task AfterEnemyHealthThreshold(HextechEnemyHexContext context, Creature target, uint combatId)
	{
		// 立即触发:掉到阈值以下的瞬间就获得格挡+缩小,不等到下个回合。
		return ApplyEscapePlan(context, target);
	}

	private static async Task ApplyEscapePlan(HextechEnemyHexContext context, Creature creature)
	{
		if (creature.CombatId == null || !context.Tracking.EscapePlanTriggered.Add(creature.CombatId.Value))
		{
			return;
		}

		int blockAmount = (int)Math.Floor(creature.MaxHp * BlockPercent);
		if (blockAmount > 0)
		{
			await CreatureCmd.GainBlock(creature, blockAmount, ValueProp.Unpowered, null);
		}

		await PowerCmd.Apply<ShrinkPower>(creature, 1m, creature, null);
	}
}
