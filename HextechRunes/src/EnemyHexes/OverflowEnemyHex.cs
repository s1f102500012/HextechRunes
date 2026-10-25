namespace HextechRunes;

internal sealed class OverflowEnemyHex : HextechEnemyHexEffect
{
	private const decimal PlayerEnergyBonus = 1m;

	internal override MonsterHexKind Kind => MonsterHexKind.Overflow;

	internal override decimal ModifyDamageMultiplicative(HextechEnemyHexContext context, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return GetMultiplier(context);
	}

	internal override decimal ModifyBlockMultiplicative(HextechEnemyHexContext context, Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return GetMultiplier(context);
	}

	// 代价：所有玩家每回合额外获得 1 点能量（能量上限 +1，同我方溢流的写法）。
	internal override decimal ModifyMaxEnergy(HextechEnemyHexContext context, Player player, decimal amount)
	{
		return amount + PlayerEnergyBonus;
	}

	private decimal GetMultiplier(HextechEnemyHexContext context)
	{
		return 1m + context.TierValue(Kind, 0.30m, 0.40m, 0.50m);
	}
}
