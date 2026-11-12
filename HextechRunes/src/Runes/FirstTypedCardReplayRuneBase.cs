namespace HextechRunes;

public abstract class FirstTypedCardReplayRuneBase : TurnScopedRelicBase
{
	private bool _triggeredThisTurn;

	protected abstract CardType TargetCardType { get; }

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Replays", 1m)
	];

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		EnsureTurnScopedStateCurrent();
		if (HasTurnProcTriggered(_triggeredThisTurn) || !IsOwnedTargetType(card))
		{
			return playCount;
		}

		return playCount + DynamicVars["Replays"].IntValue;
	}

	public override Task AfterModifyingCardPlayCount(CardModel card)
	{
		EnsureTurnScopedStateCurrent();
		if (IsOwnedTargetType(card) && TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	private bool IsOwnedTargetType(CardModel? card)
	{
		if (TargetCardType == CardType.Attack)
		{
			return IsOwnedAttack(card);
		}

		if (TargetCardType == CardType.Skill)
		{
			return IsOwnedSkill(card);
		}

		return card?.Owner == Owner && card.Type == TargetCardType;
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
