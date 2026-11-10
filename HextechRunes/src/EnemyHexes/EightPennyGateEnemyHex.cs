namespace HextechRunes;

internal sealed class EightPennyGateEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.EightPennyGate;

	// 只读：这个钩子会在真正出牌之外被调用（随机数预测等模组拿预览牌模拟出牌时会直接调到它），
	// 在这里记"本回合已消耗"会让只在本机发生的预览占掉名额，联机两端对同一张牌的去向判断不同（#22）。
	// 所以每次都从两端同步的出牌记录里重新数本回合之前已打出的计数牌。
	internal override (PileType, CardPilePosition)? ModifyCardPlayResultPileTypeAndPosition(
		HextechEnemyHexContext context,
		CardModel card,
		bool isAutoPlay,
		ResourceInfo resources,
		PileType pileType,
		CardPilePosition position)
	{
		if (isAutoPlay
			|| card.Type == CardType.Power
			|| card.Owner?.Creature.Side != CombatSide.Player
			|| card.Owner.Creature.CombatState?.RunState != context.RunState)
		{
			return null;
		}

		int limit = context.TierValue(Kind, 0, 1, 2);
		if (limit <= 0)
		{
			return null;
		}

		IEnumerable<CardPlay> playsThisTurn = CombatManager.Instance.History.CardPlaysFinished
			.Where(entry => entry.HappenedThisTurn(card.Owner.Creature.CombatState))
			.Select(static entry => entry.CardPlay);
		return CountEarlierCountedPlays(playsThisTurn, card.Owner) < limit
			? (PileType.Exhaust, position)
			: null;
	}

	/// <summary>
	/// 本回合 <paramref name="owner"/> 已手动打出的非能力牌次数（重放只算第一次）。
	/// 原版（0.107.1–0.111.0）在进入出牌循环前就问去向，本次出牌还没记入历史，所以真实结算和预览数到的是同一个值。
	/// </summary>
	internal static int CountEarlierCountedPlays(IEnumerable<CardPlay> playsThisTurn, Player owner)
	{
		return playsThisTurn.Count(play => play.IsFirstInSeries
			&& !play.IsAutoPlay
			&& play.Card.Type != CardType.Power
			&& play.Card.Owner?.NetId == owner.NetId);
	}
}
