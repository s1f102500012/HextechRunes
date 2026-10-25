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

	// 原版 CreatureCmd.Damage 在 ModifyHpLost 改动了数值后才调这里，模拟/预览只问查询钩子不会走到这里。
	// 同步处理：原版在这之后才扣血，确认必须赶在本次伤害的受伤回调之前。
	public override Task AfterModifyingHpLostAfterOsty()
	{
		HextechEnemyHexContext context = new(this);
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			effect.AfterModifyingHpLostAfterOsty(context);
		}

		return Task.CompletedTask;
	}

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		return HextechEnemyHexDispatcher.Transform(
			this,
			count,
			(effect, context, current) => effect.ModifyHandDraw(context, player, current));
	}

	// ShouldDraw 是查询钩子，可能在真实抽牌之外被调用（其他模组预测抽牌），这里只记下"最近一次拦下了谁"，不动记账。
	// 原版 CardPileCmd.Draw 与 HextechSelectedDrawHelper 在它返回 false 后同步紧接着调 AfterPreventingDraw，中间没有等待，
	// 所以 After 读到的一定是这次真实查询的结果；模拟调用留下的记录会被下一次真实查询覆盖。不保存：两次调用之间不会存档。
	private (Player Player, bool FromHandDraw, int RequestedCount)? _lastPreventedDraw;

	public override bool ShouldDraw(Player player, bool fromHandDraw)
	{
		bool allowed = HextechEnemyHexDispatcher.All(
			this,
			(effect, context) => effect.ShouldDraw(context, player, fromHandDraw));
		_lastPreventedDraw = allowed ? null : (player, fromHandDraw, HextechDrawRequest.GetRequestedCount(player));
		return allowed;
	}

	// 被拦下的抽牌按效果顺序交给第一个认领它的效果（我细看在转出胜利之前：被我细看作废的抽牌不再推迟到下回合）。
	public override async Task AfterPreventingDraw()
	{
		if (_lastPreventedDraw is not (Player player, bool fromHandDraw, int requestedCount))
		{
			return;
		}

		_lastPreventedDraw = null;
		HextechEnemyHexContext context = new(this);
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this).ToList())
		{
			if (await effect.AfterPreventingDraw(context, player, fromHandDraw, requestedCount))
			{
				return;
			}
		}
	}

	public override decimal ModifyEnergyGain(Player player, decimal amount)
	{
		if (player.Creature.CombatState?.RunState != RunState)
		{
			return amount;
		}

		return HextechEnemyHexDispatcher.Transform(
			this,
			amount,
			(effect, context, current) => effect.ModifyEnergyGain(context, player, current));
	}

	public override async Task AfterModifyingEnergyGain()
	{
		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterModifyingEnergyGain(context));
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
