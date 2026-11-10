namespace HextechRunes;

public sealed class SwordFlightRune : TurnScopedRelicBase
{
	private bool _triggeredThisTurn;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<SovereignBlade>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent();
		if (Owner.Creature.IsDead
			|| !cardPlay.IsFirstInSeries
			|| cardPlay.IsAutoPlay
			|| cardPlay.Card.Owner != Owner
			|| cardPlay.Card is not SovereignBlade
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		int cardsToDraw = Math.Max(0, CardPile.MaxCardsInHand - PileType.Hand.GetPile(Owner).Cards.Count);

		if (cardsToDraw <= 0)
		{
			return;
		}

		Flash();
		await CardPileCmd.Draw(context, cardsToDraw, Owner, fromHandDraw: false);
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
