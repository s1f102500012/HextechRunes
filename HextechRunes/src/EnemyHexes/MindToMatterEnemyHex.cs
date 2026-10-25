namespace HextechRunes;

internal sealed class MindToMatterEnemyHex : HextechEnemyHexEffect, IHextechEnemyMaxHpCoefficientProvider
{
	// 全队牌组合计后按人数分组，每 N 张 +1% 最大生命值；描述侧由 MonsterHexCatalog 的 CardsNeeded 阈值同步显示。
	internal const int CardsPerPercentPerPlayer = 1;

	internal override MonsterHexKind Kind => MonsterHexKind.MindToMatter;

	internal override int PersistentOrder => 36;

	// 开战时的所有敌人、战斗中新加入的敌人各结算一次，首领转阶段重放时随最大生命一起补算。
	internal override async Task ApplyPersistentToEnemy(HextechEnemyHexContext context, Creature creature, int? maxHpBaseOverride, bool replayOneShotPowers)
	{
		if (HextechCombatProcTracker.TryMarkPersistentHexApplied(context.Tracking.MindToMatterApplied, creature, replayOneShotPowers))
		{
			await context.Modifier.ReapplyMonsterMaxHpCoefficients(creature, maxHpBaseOverride);
		}
	}

	public decimal GetMaxHpBonusFraction(HextechEnemyHexContext context, Creature creature)
	{
		int deckCards = context.RunState.Players.Sum(static player => player.Deck.Cards.Count);
		return (deckCards / (CardsPerPercentPerPlayer * context.ScalingPlayerCount)) * 0.01m;
	}
}
