namespace HextechRunes;

internal sealed class IGripEnemyHex : HextechEnemyHexEffect
{
	private const int EnergyLoss = 1;

	internal override MonsterHexKind Kind => MonsterHexKind.IGrip;

	// 每名玩家各自累计手动打出的牌数，每满 4/3/2 张失去 1 点能量。
	internal override Task AfterCardPlayed(
		HextechEnemyHexContext context,
		PlayerChoiceContext choiceContext,
		CardPlay cardPlay)
	{
		if (!context.IsManualPlayerCardPlay(cardPlay, out Player? owner, out _)
			|| owner.Creature.IsDead
			|| owner.PlayerCombatState is not { } playerCombatState)
		{
			return Task.CompletedTask;
		}

		int cardsNeeded = context.TierValue(Kind, 4, 3, 2);
		if (TryCountCard(context.Tracking, owner.NetId, cardsNeeded))
		{
			playerCombatState.LoseEnergy(EnergyLoss);
		}

		return Task.CompletedTask;
	}

	/// <summary>记一张牌；凑满 <paramref name="cardsNeeded"/> 张时清零并返回 true。</summary>
	internal static bool TryCountCard(
		HextechMayhemCombatTrackingState tracking,
		ulong playerId,
		int cardsNeeded)
	{
		int played = tracking.GripPlayerCardsPlayedThisCombat.GetValueOrDefault(playerId, 0) + 1;
		if (played >= cardsNeeded)
		{
			tracking.GripPlayerCardsPlayedThisCombat.Remove(playerId);
			return true;
		}

		tracking.GripPlayerCardsPlayedThisCombat[playerId] = played;
		return false;
	}
}
