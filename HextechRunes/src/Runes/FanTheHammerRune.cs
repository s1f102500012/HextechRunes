namespace HextechRunes;

public sealed class FanTheHammerRune : TurnScopedRelicBase
{
	private const decimal DamageMultiplierValue = 0.35m;
	private const decimal DamagePercentValue = DamageMultiplierValue * 100m;

	private bool _triggeredThisTurn;
	private CardModel? _damageReducedCard;

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
		new DynamicVar("Replays", 3m),
		new DynamicVar("DamageMultiplier", DamageMultiplierValue),
		new DynamicVar("DamagePercent", DamagePercentValue)
	];

	public override Task BeforeCombatStart()
	{
		ClearDamageReducedCard();
		return base.BeforeCombatStart();
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ClearDamageReducedCard();
		return base.AfterCombatEnd(room);
	}

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		if (card.Owner != Owner)
		{
			return playCount;
		}

		EnsureTurnScopedStateCurrent();
		if (HasTurnProcTriggered(_triggeredThisTurn) || !IsOwnedAttack(card))
		{
			return playCount;
		}

		return playCount + DynamicVars["Replays"].IntValue;
	}

	public override Task AfterModifyingCardPlayCount(CardModel card)
	{
		if (card.Owner != Owner)
		{
			return Task.CompletedTask;
		}

		EnsureTurnScopedStateCurrent();
		if (IsOwnedAttack(card) && TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			TrackDamageReducedCard(card);
			Flash();
		}

		return Task.CompletedTask;
	}

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (cardSource is not CardModel card
			|| card != _damageReducedCard
			|| card.Owner != Owner
			|| dealer != Owner.Creature
			|| !IsOwnedAttack(card)
			|| target?.Side == CombatSide.Player
			|| (props & ValueProp.Unpowered) != 0)
		{
			return 1m;
		}

		return DynamicVars["DamageMultiplier"].BaseValue;
	}

	private void TrackDamageReducedCard(CardModel card)
	{
		ClearDamageReducedCard();
		_damageReducedCard = card;
		card.Played += ClearDamageReducedCard;
	}

	private void ClearDamageReducedCard()
	{
		if (_damageReducedCard is CardModel card)
		{
			card.Played -= ClearDamageReducedCard;
		}

		_damageReducedCard = null;
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
