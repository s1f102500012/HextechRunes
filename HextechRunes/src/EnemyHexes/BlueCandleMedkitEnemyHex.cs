namespace HextechRunes;

internal sealed class BlueCandleMedkitEnemyHex : HextechEnemyHexEffect
{
	internal const int CostIncrease = 1;

	internal override MonsterHexKind Kind => MonsterHexKind.BlueCandleMedkit;

	// 玩家的状态牌与诅咒牌耗能 +1,优先级最低:视同加在基础费用上,轮转不息一类的本回合定费会覆盖它;
	// 我方蓝烛药箱的 0 费与敌方开悟的 1 费下限都在 Late 阶段,晚于这里生效。
	// 原版无法打出的状态/诅咒基础费用是 -1(无费用标记),原版费用 Hook 对负费用直接跳过,这里也不处理。
	internal override int GetBaseEnergyCostIncrease(HextechEnemyHexContext context, CardModel card)
	{
		// 分发层（Modifier.TryModifyEnergyCostInCombat）已限定玩家侧、本局战斗、非 X 费。
		if (card.Type is not (CardType.Status or CardType.Curse))
		{
			return 0;
		}

		return HextechCardCostHelper.GetIncreaseSurvivingLocalModifiers(card.EnergyCost, CostIncrease);
	}
}
