namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal bool QueueEnemyHealingBlock(Creature creature, decimal amount)
	{
		if (creature.Side != CombatSide.Enemy || creature.CombatId == null || amount <= 0m)
		{
			return false;
		}

		int block = (int)Math.Floor(amount);
		if (block <= 0)
		{
			return false;
		}

		uint combatId = creature.CombatId.Value;
		CombatTracking.DelayedEnemyHealingBlock[combatId] =
			CombatTracking.DelayedEnemyHealingBlock.GetValueOrDefault(combatId, 0) + block;
		return true;
	}

	private async Task ApplyDelayedEnemyHealingBlocks(HextechCombatState combatState)
	{
		foreach ((uint combatId, int block) in CombatTracking.DelayedEnemyHealingBlock.ToList())
		{
			CombatTracking.DelayedEnemyHealingBlock.Remove(combatId);
			Creature? creature = combatState.GetCreature(combatId);
			if (creature == null || !creature.IsAlive || block <= 0)
			{
				continue;
			}

			// 这笔格挡由回血换来，蛋白粉奶昔不再把它转回回血。
			await ProteinShakeEnemyHex.RunWithoutConversion(() => CreatureCmd.GainBlock(creature, block, ValueProp.Unpowered, null));
		}
	}
}
