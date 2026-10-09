namespace HextechRunes;

internal sealed class TanksShieldEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.TanksShield;

	internal override async Task AfterCardPlayed(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (!context.IsManualPlayerCardPlay(cardPlay, out Player? owner, out HextechCombatState? combatState)
			|| cardPlay.Card.Type != CardType.Attack)
		{
			return;
		}

		int block = context.TierValue(Kind, 1, 2, 3);
		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await CreatureCmd.GainBlock(enemy, block, ValueProp.Unpowered, cardPlay);
		}
	}
}
