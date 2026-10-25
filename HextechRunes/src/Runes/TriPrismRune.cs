namespace HextechRunes;

public sealed class TriPrismRune : TurnScopedRelicBase
{
	private bool _triggeredThisTurn;

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldTrigger(card) && !card.EnergyCost.CostsX;
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldTrigger(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		return ShouldTrigger(card) ? playCount + 1 : playCount;
	}

	public override Task AfterModifyingCardPlayCount(CardModel card)
	{
		if (ShouldTrigger(card) && TryConsumeTurnProc(nameof(TriPrismRune), ref _triggeredThisTurn))
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	private bool ShouldTrigger(CardModel card)
	{
		EnsureTurnScopedStateCurrent();
		return !HasTurnProcTriggered(nameof(TriPrismRune), _triggeredThisTurn)
			&& card.Owner == Owner
			&& HextechColorlessCardHelper.IsColorlessCard(card);
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
