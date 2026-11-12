namespace HextechRunes;

public sealed class FinalFormRune : TurnScopedRelicBase
{
	private const decimal PlatingPercentValue = 0.10m;
	private const decimal PlatingDisplayPercentValue = PlatingPercentValue * 100m;

	private bool _triggeredThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedTriggeredThisTurn
	{
		get => false;
		set
		{
			// Legacy save compatibility: this is turn-scoped runtime state and must not enter multiplayer checksums.
			_triggeredThisTurn = false;
			UpdateTurnScopedStateIdentity(null);
		}
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("MinCost", 2m),
		new DynamicVar("PlatingPercent", PlatingPercentValue),
		new CardsVar(2),
		new DynamicVar("PlatingDisplayPercent", PlatingDisplayPercentValue)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PlatingPower>()
	];

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent();
		if (!IsOwnedCardWithEffectiveCostAtLeast(cardPlay.Card, DynamicVars["MinCost"].BaseValue)
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		int plating = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * DynamicVars["PlatingPercent"].BaseValue));
		Flash();
		await PowerCmd.Apply<PlatingPower>(Owner.Creature, plating, Owner.Creature, cardPlay.Card);
		await CardPileCmd.Draw(context, DynamicVars.Cards.BaseValue, Owner, fromHandDraw: false);
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
