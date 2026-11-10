namespace HextechRunes;

internal sealed class AnthonyBiasEnemyHex : HextechEnemyHexEffect
{
	// 全队牌组合计后按人数分组（不逐人取整），每 N 张 +1%；描述侧由 MonsterHexCatalog 的 CardsNeeded 阈值同步显示。
	internal const int CardsPerPercentPerPlayer = 1;

	internal override MonsterHexKind Kind => MonsterHexKind.AnthonyBias;

	internal override decimal ModifyDamageMultiplicative(HextechEnemyHexContext context, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return GetMultiplier(context);
	}

	internal override decimal ModifyBlockMultiplicative(HextechEnemyHexContext context, Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return GetMultiplier(context);
	}

	internal override decimal ModifyEnemyHealMultiplicative(HextechEnemyHexContext context, Creature creature, decimal amount)
	{
		return GetMultiplier(context);
	}

	private static decimal GetMultiplier(HextechEnemyHexContext context)
	{
		// 数值每次现算、不缓存；按索引累加不分配，checked 与 Enumerable.Sum 的溢出行为一致。
		IReadOnlyList<Player> players = context.RunState.Players;
		int deckCards = 0;
		for (int i = 0; i < players.Count; i++)
		{
			deckCards = checked(deckCards + players[i].Deck.Cards.Count);
		}

		return 1m + (deckCards / (CardsPerPercentPerPlayer * context.ScalingPlayerCount)) * 0.01m;
	}
}
