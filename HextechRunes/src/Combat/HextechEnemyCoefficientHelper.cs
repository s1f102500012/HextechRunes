namespace HextechRunes;

internal interface IHextechEnemyMaxHpCoefficientProvider
{
	decimal GetMaxHpBonusFraction(HextechEnemyHexContext context, Creature creature);
}

internal static class HextechEnemyCoefficientHelper
{
	public static decimal CombineMultipliersByHex(
		IEnumerable<(MonsterHexKind Kind, decimal Multiplier)> contributions)
	{
		return CombineBonusFractionsByHex(
			contributions.Select(static contribution =>
				(contribution.Kind, contribution.Multiplier - 1m)));
	}

	// 每个敌方海克斯 Kind 只贡献一项时（生效效果的 Kind 互不相同，启动时校验），按 Kind 分组恒为单元素组。
	// 从 1m 起按遍历顺序逐项调用本方法，与 CombineMultipliersByHex 逐位一致（含 decimal 小数位数）：
	// 先换成加成（multiplier - 1m），组内照 Enumerable.Sum 从 0m 起加，再 1m + 组和，组间照 Aggregate 依次相乘。
	public static decimal MultiplyDistinctHexMultiplier(decimal product, decimal multiplier)
	{
		decimal bonusFraction = multiplier - 1m;
		decimal groupSum = 0m + bonusFraction;
		return product * (1m + groupSum);
	}

	public static decimal CombineBonusFractionsByHex(
		IEnumerable<(MonsterHexKind Kind, decimal BonusFraction)> contributions)
	{
		return contributions
			.GroupBy(static contribution => contribution.Kind)
			.Select(static group => 1m + group.Sum(static contribution => contribution.BonusFraction))
			.Aggregate(1m, static (product, multiplier) => product * multiplier);
	}
}
