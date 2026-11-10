namespace HextechRunes;

internal sealed class SpeedDemonEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.SpeedDemon;

	internal override async Task BeforePlayerSideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		foreach (uint combatId in context.Tracking.SpeedDemonPending.OrderBy(static id => id).ToList())
		{
			Creature? creature = combatState.GetCreature(combatId);
			context.Tracking.SpeedDemonPending.Remove(combatId);
			if (creature == null || !creature.IsAlive)
			{
				continue;
			}

			decimal blockPercent = context.TierValue(Kind, 0.05m, 0.10m, 0.15m);
			int blockAmount = HextechEnemyHexContext.FractionOfMaxHp(creature, blockPercent);
			await CreatureCmd.GainBlock(creature, blockAmount, ValueProp.Unpowered, null);
		}
	}

	internal override Task AfterEnemyDamageGivenPlayerHit(HextechEnemyHexContext context, Creature dealer, Creature target)
	{
		if (dealer.IsAlive && dealer.CombatId != null)
		{
			context.Tracking.SpeedDemonPending.Add(dealer.CombatId.Value);
		}

		return Task.CompletedTask;
	}
}
