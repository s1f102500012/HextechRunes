namespace HextechRunes;

internal sealed class HastyScribbleEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.HastyScribble;

	// 原版手牌上限是静态属性 CardPile.MaxCardsInHand，抽牌、加入手牌、回合开始抽牌和各张卡牌都直接读它，
	// 没有按玩家改写的 Hook；由 combat.max-cards-in-hand 补丁在战斗中扣减，所有玩家同时生效。
	internal static int GetHandLimitReduction(HextechEnemyHexContext context)
	{
		return context.IsActive(MonsterHexKind.HastyScribble)
			? context.TierValue(MonsterHexKind.HastyScribble, 1, 2, 3)
			: 0;
	}
}
