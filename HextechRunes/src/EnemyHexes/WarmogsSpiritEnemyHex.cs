namespace HextechRunes;

internal sealed class WarmogsSpiritEnemyHex : DrawProgressEnemyHexBase
{
	private const int Tier1CardsPerPlating = 8;
	private const int Tier2CardsPerPlating = 6;
	private const int Tier3CardsPerPlating = 4;

	internal override MonsterHexKind Kind => MonsterHexKind.WarmogsSpirit;

	private int GetCardsPerPlating(HextechEnemyHexContext context)
	{
		return context.TierValue(Kind, Tier1CardsPerPlating, Tier2CardsPerPlating, Tier3CardsPerPlating);
	}

	protected override async Task AfterLocalCardDrawn(HextechEnemyHexContext context, Player owner, HextechCombatState combatState)
	{
		if (HextechEnemyDrawProgress.RecordDraw(context.Tracking.WarmogsSpiritPlayerCardsDrawnThisCombat, GetCardsPerPlating(context)) == 0)
		{
			return;
		}

		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(enemy, 1m, enemy, null);
		}
	}

	protected override async Task ResolveDrawProgressFromHistory(HextechEnemyHexContext context, HextechCombatState combatState)
	{
		int pendingPlating = HextechEnemyDrawProgress.ResolveFromHistory(
			context.Tracking.WarmogsSpiritPlayerCardsDrawnThisCombat,
			combatState,
			GetCardsPerPlating(context));
		if (pendingPlating <= 0)
		{
			return;
		}

		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(enemy, pendingPlating, enemy, null);
		}
	}
}
