namespace HextechRunes;

internal sealed class NightstalkingEnemyHex : DrawProgressEnemyHexBase
{
	internal const int CardsPerSlippery = 12;

	internal override MonsterHexKind Kind => MonsterHexKind.Nightstalking;

	protected override async Task AfterLocalCardDrawn(HextechEnemyHexContext context, Player owner, HextechCombatState combatState)
	{
		if (HextechEnemyDrawProgress.RecordDraw(context.Tracking.NightstalkingPlayerCardsDrawnThisCombat, CardsPerSlippery) == 0)
		{
			return;
		}

		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.ApplyExact<SlipperyPower>(enemy, 1m, enemy, null);
		}
	}

	protected override async Task ResolveDrawProgressFromHistory(HextechEnemyHexContext context, HextechCombatState combatState)
	{
		int pendingSlippery = HextechEnemyDrawProgress.ResolveFromHistory(
			context.Tracking.NightstalkingPlayerCardsDrawnThisCombat,
			combatState,
			CardsPerSlippery);
		if (pendingSlippery <= 0)
		{
			return;
		}

		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.ApplyExact<SlipperyPower>(enemy, pendingSlippery, enemy, null);
		}
	}
}
