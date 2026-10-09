namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (dealer?.Side != CombatSide.Enemy || dealer.CombatState?.RunState != RunState)
		{
			return 1m;
		}

		HextechEnemyHexContext context = new(this);
		return HextechEnemyCoefficientHelper.CombineMultipliersByHex(
			HextechEnemyHexEffects.GetActive(this)
				.Select(effect => (
					effect.Kind,
					effect.ModifyDamageMultiplicative(context, target, amount, props, dealer, cardSource))));
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		if (target.Side != CombatSide.Enemy || target.CombatState?.RunState != RunState)
		{
			return 1m;
		}

		HextechEnemyHexContext context = new(this);
		return HextechEnemyCoefficientHelper.CombineMultipliersByHex(
			HextechEnemyHexEffects.GetActive(this)
				.Select(effect => (
					effect.Kind,
					effect.ModifyBlockMultiplicative(context, target, block, props, cardSource, cardPlay))));
	}

	public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target.CombatState?.RunState != RunState)
		{
			return amount;
		}

		return HextechEnemyHexDispatcher.Transform(
			this,
			amount,
			(effect, context, current) => effect.ModifyHpLostAfterOsty(context, target, current, props, dealer, cardSource));
	}

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		return HextechEnemyHexDispatcher.Transform(
			this,
			count,
			(effect, context, current) => effect.ModifyHandDraw(context, player, current));
	}

	public override bool ShouldDraw(Player player, bool fromHandDraw)
	{
		return HextechEnemyHexDispatcher.All(
			this,
			(effect, context) => effect.ShouldDraw(context, player, fromHandDraw));
	}

	// 能否打出只管本局战斗中玩家侧的牌。
	public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
	{
		if (card.Owner?.Creature.Side != CombatSide.Player
			|| card.Owner.Creature.CombatState?.RunState != RunState)
		{
			return true;
		}

		return HextechEnemyHexDispatcher.All(
			this,
			(effect, context) => effect.ShouldPlay(context, card, autoPlayType));
	}

	public override bool ShouldFlush(Player player)
	{
		return HextechEnemyHexDispatcher.All(
			this,
			(effect, context) => effect.ShouldFlush(context, player));
	}

	public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (card.Owner?.Creature.Side != CombatSide.Player
			|| card.EnergyCost.CostsX
			|| card.Owner.Creature.CombatState?.RunState != RunState)
		{
			return false;
		}

		// 基础费用增量先加:它已按临时定费折算,所以免费打出类的本回合 0 费不会被加回来。
		int baseIncrease = HextechEnemyHexDispatcher.Transform(
			this,
			0,
			(effect, context, current) => current + effect.GetBaseEnergyCostIncrease(context, card));
		decimal cost = originalCost + baseIncrease;

		if (card.Type == CardType.Attack
			&& card.Pile?.Type == PileType.Hand
			&& originalCost > 0m)
		{
			decimal multiplier = HextechEnemyHexDispatcher.Transform(
				this,
				1m,
				(effect, context, current) => current * effect.ModifyPlayerAttackEnergyCostMultiplier(context, card, originalCost));
			cost *= multiplier;
		}

		modifiedCost = cost;
		return cost != originalCost;
	}

	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = HextechEnemyHexDispatcher.Transform(
			this,
			originalCost,
			(effect, context, current) => effect.ModifyEnergyCostInCombatLate(context, card, current));
		return modifiedCost != originalCost;
	}

	public override (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPositionCompat(CardModel card, bool isAutoPlay, ResourceInfo resources, PileType pileType, CardPilePosition position)
	{
		(pileType, position) = HextechEnemyHexDispatcher.Transform(
			this,
			(pileType, position),
			(effect, context, current) =>
			{
				if (effect.ModifyCardPlayResultPileTypeAndPosition(context, card, isAutoPlay, resources, current.pileType, current.position) is (PileType nextPileType, CardPilePosition nextPosition))
				{
					return (nextPileType, nextPosition);
				}

				return current;
			});

		return (pileType, position);
	}
}
