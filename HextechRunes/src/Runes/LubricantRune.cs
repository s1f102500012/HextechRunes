namespace HextechRunes;

public sealed class LubricantRune : TurnScopedRelicBase
{
	private bool _usedThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedUsedThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent();
			return HasTurnProcTriggered(nameof(LubricantRune), _usedThisTurn);
		}
		set
		{
			_usedThisTurn = value;
			InvokeDisplayAmountChanged();
			UpdateTurnScopedStateIdentity();
		}
	}

	public override bool ShowCounter => IsInLiveCombat;

	public override int DisplayAmount => !IsCanonical && !HasTurnProcTriggered(nameof(LubricantRune), _usedThisTurn) ? 1 : 0;

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsDefectPlayer(player);
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldPowerCardBeFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldPowerCardBeFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent();
		if (cardPlay.IsAutoPlay
			|| !cardPlay.IsFirstInSeries
			|| cardPlay.Card.Owner != Owner
			|| cardPlay.Card.Type != CardType.Power
			|| !TryConsumeTurnProc(nameof(LubricantRune), ref _usedThisTurn))
		{
			return Task.CompletedTask;
		}

		Flash();
		return Task.CompletedTask;
	}

	private bool ShouldPowerCardBeFree(CardModel card)
	{
		EnsureTurnScopedStateCurrent();
		return !HasTurnProcTriggered(nameof(LubricantRune), _usedThisTurn)
			&& card.Owner == Owner
			&& card.Type == CardType.Power
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}

	protected override void ResetTurnScopedState()
	{
		_usedThisTurn = false;
		InvokeDisplayAmountChanged();
	}
}
