namespace HextechRunes;

internal sealed class MonarchsGazeEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.MonarchsGaze;

	internal override Task AfterCardPlayed(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (!context.IsManualPlayerCardPlay(cardPlay, out Player? owner, out _)
			|| cardPlay.Card.Type != CardType.Attack
			|| !owner.Creature.IsAlive)
		{
			return Task.CompletedTask;
		}

		return PowerCmd.Apply<HextechTemporaryStrengthLossPower>(owner.Creature, 1m, owner.Creature, cardPlay.Card);
	}
}
