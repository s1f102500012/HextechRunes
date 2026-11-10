namespace HextechRunes;

public sealed class SomethingForNothingRune : TurnScopedRelicBase
{
	private bool _discountTriggeredThisTurn;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1),
		new EnergyVar(1)
	];

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedTriggeredThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent();
			return HasTurnProcTriggered(_discountTriggeredThisTurn);
		}
		set
		{
			_discountTriggeredThisTurn = value;
			UpdateTurnScopedStateIdentity();
		}
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (Owner.Creature.IsDead
			|| cardPlay.Card.Owner != Owner)
		{
			return Task.CompletedTask;
		}

		decimal playedEnergyCost = HextechCombatHooks.GetEnergyCostForCurrentCardPlay(cardPlay.Card);
		if (IsZeroCostPlay(playedEnergyCost))
		{
			Flash();
			return CardPileCmd.Draw(context, DynamicVars.Cards.BaseValue, Owner, fromHandDraw: false);
		}

		EnsureTurnScopedStateCurrent();
		if (cardPlay.Card.EnergyCost.CostsX
			|| !TryConsumeTurnProc(ref _discountTriggeredThisTurn))
		{
			return Task.CompletedTask;
		}

		int currentCost = cardPlay.Card.EnergyCost.GetWithModifiers(CostModifiers.Local);
		int reducedCost = ReduceCost(currentCost, DynamicVars.Energy.IntValue);
		cardPlay.Card.EnergyCost.SetThisCombat(reducedCost, reduceOnly: true);
		try
		{
			cardPlay.Card.InvokeEnergyCostChanged();
		}
		catch (Exception ex)
		{
			HextechLog.Warn("SomethingForNothing", $"Cost visual refresh failed: {ex.Message}");
		}

		Flash();
		return Task.CompletedTask;
	}

	internal static bool IsZeroCostPlay(decimal energyCost)
	{
		return energyCost <= 0m;
	}

	internal static int ReduceCost(int currentCost, int reduction)
	{
		return Math.Max(0, currentCost - reduction);
	}

	protected override void ResetTurnScopedState()
	{
		_discountTriggeredThisTurn = false;
	}
}
