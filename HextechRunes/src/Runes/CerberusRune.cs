namespace HextechRunes;

public sealed class CerberusRune : TurnScopedRelicBase
{
	private int _attacksPlayedThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedAttacksPlayedThisTurn
	{
		get
		{
			// 序列化可能发生在回合钩子之外（例如读档后的首个回合钩子之前）：先按持有者回合号懒清零，
			// 保证写出的是本回合的计数而不是上一回合的残值。
			EnsureTurnScopedStateCurrent();
			return GetTurnProcCount(nameof(CerberusRune), _attacksPlayedThisTurn);
		}
		set
		{
			_attacksPlayedThisTurn = Math.Max(0, value);
			InvokeDisplayAmountChanged();
			UpdateTurnScopedStateIdentity();
		}
	}

	public override bool ShowCounter => IsInLiveCombat;

	public override int DisplayAmount => !IsCanonical ? Math.Max(0, DynamicVars["FreeAttacks"].IntValue - GetTurnProcCount(nameof(CerberusRune), _attacksPlayedThisTurn)) : 0;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("FreeAttacks", 3m)
	];

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldPlayAttackForFree(card))
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldPlayAttackForFree(card))
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent();
		if (!cardPlay.IsFirstInSeries || cardPlay.IsAutoPlay || !IsOwnedAttack(cardPlay.Card))
		{
			return Task.CompletedTask;
		}

		if (!TryConsumeTurnProc(nameof(CerberusRune), ref _attacksPlayedThisTurn, int.MaxValue))
		{
			return Task.CompletedTask;
		}

		if (GetTurnProcCount(nameof(CerberusRune), _attacksPlayedThisTurn) <= DynamicVars["FreeAttacks"].IntValue)
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	private bool ShouldPlayAttackForFree(CardModel card)
	{
		EnsureTurnScopedStateCurrent();
		return IsOwnedAttack(card)
			&& card.Pile?.Type == PileType.Hand
			&& !card.EnergyCost.CostsX
			&& !HasTurnProcReachedLimit(nameof(CerberusRune), _attacksPlayedThisTurn, DynamicVars["FreeAttacks"].IntValue);
	}

	protected override void ResetTurnScopedState()
	{
		_attacksPlayedThisTurn = 0;
		InvokeDisplayAmountChanged();
	}
}
