using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

/// <summary>
/// "视同加在卡牌基础费用上"的加费（优先级最低）：卡牌自身的临时定费（本回合 0 费、直到打出前 0 费等"免费"）会把增量吃掉，
/// 相对加减费照常叠加。我方溢流与敌方蓝烛药箱共用这个口径。
/// </summary>
internal static class HextechCardCostHelper
{
	// 原版私有字段 CardEnergyCost._base（int）与 _localModifiers（List<LocalCostModifier>），0.107.1 与 0.111.0 同名。
	// 缺失时进启动摘要并降级为直接加在规范费用上，不能让类型初始化失败连带调用方。
	private static readonly FieldInfo? BaseCostField = TryGetField(typeof(CardEnergyCost), "_base");
	private static readonly FieldInfo? LocalModifiersField = TryGetField(typeof(CardEnergyCost), "_localModifiers");

	/// <summary>
	/// 把增量加到基础费用上再走一遍卡牌自身的临时修正，返回最终还剩多少：
	/// 绝对定费（如本回合 0 费）吃掉增量，相对加减费照常叠加。临时修正列表原版未公开，只读不写。
	/// </summary>
	internal static int GetIncreaseSurvivingLocalModifiers(CardEnergyCost energyCost, int increase)
	{
		int baseCost = BaseCostField?.GetValue(energyCost) is int reflectedBase
			? reflectedBase
			: energyCost.Canonical;
		if (baseCost < 0)
		{
			return 0;
		}

		int withIncrease = baseCost + increase;
		int withoutIncrease = baseCost;
		if (LocalModifiersField?.GetValue(energyCost) is IEnumerable<LocalCostModifier> modifiers)
		{
			foreach (LocalCostModifier modifier in modifiers)
			{
				withIncrease = modifier.Modify(withIncrease);
				withoutIncrease = modifier.Modify(withoutIncrease);
			}
		}

		return withIncrease - withoutIncrease;
	}
}
