namespace HextechRunes;

public sealed class OverflowRune : HextechRelicBase, IHextechHealingMultiplierProvider
{
	// 治疗、格挡、伤害共用的倍率与手牌加费；文案写的是字面值（翻倍、+1），改数值要同步九语言。
	private const decimal StatMultiplier = 2m;
	private const int HandCostIncrease = 1;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new EnergyVar(1)
	];

	public override decimal ModifyMaxEnergy(Player player, decimal amount)
	{
		return player == Owner ? amount + DynamicVars.Energy.BaseValue : amount;
	}

	public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (card.Owner != Owner || card.Pile?.Type != PileType.Hand || card.EnergyCost.CostsX)
		{
			return false;
		}

		// 视同加在基础费用上：本回合 0 费、直到打出前 0 费等"免费"会吃掉这 1 费；
		// 规则型免费（地狱三头犬、润滑油、原版免费攻击等）在 Late 阶段定为 0，同样晚于这里。
		int increase = HextechCardCostHelper.GetIncreaseSurvivingLocalModifiers(card.EnergyCost, HandCostIncrease);
		modifiedCost = originalCost + increase;
		return increase != 0;
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner.Creature ? StatMultiplier : 1m;
	}

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return IsDamageFromOwnerToEnemyOrPreview(target, dealer, cardSource) ? StatMultiplier : 1m;
	}

	decimal IHextechHealingMultiplierProvider.ModifyHealingMultiplicative(Player player, Creature creature, decimal amount)
	{
		return IsFirstOwnedInstance(player) ? StatMultiplier : 1m;
	}
}
