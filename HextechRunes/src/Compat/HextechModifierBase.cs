namespace HextechRunes;

// 自有 Modifier 基类:0.108.0 伤害管线与 0.109.0 卡牌打出去向的签名桥。
// 回合钩子(带 participants)三版签名一致,子类直接覆写原版;Modifier 没有持有者,需要区分"谁在这次回合里"
// 时自己按 participants 筛(队友额外回合只带那名玩家重入回合钩子)。
internal abstract class HextechModifierBase : ModifierModel
{
	public virtual decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return 1m;
	}

	public sealed override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
	{
		return ModifyDamageMultiplicativeCompat(target, amount, props, dealer, cardSource);
	}

	// 0.109.0 起卡牌打出去向从 (PileType, CardPilePosition) 元组改为 CardLocation:同 HextechRelicBase 的桥。
	public virtual (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPositionCompat(CardModel card, bool isAutoPlay, ResourceInfo resources, PileType pileType, CardPilePosition position)
	{
		return (pileType, position);
	}

	public sealed override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
	{
		(PileType pileType, CardPilePosition position) = ModifyCardPlayResultPileTypeAndPositionCompat(card, isAutoPlay, resources, cardLocation.pileType, cardLocation.position);
		cardLocation.pileType = pileType;
		cardLocation.position = position;
		return cardLocation;
	}
}
