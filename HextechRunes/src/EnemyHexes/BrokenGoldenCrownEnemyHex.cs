namespace HextechRunes;

/// <summary>破碎金冠（敌方，白银）：玩家的战后卡牌奖励少出现 1 张牌。</summary>
internal sealed class BrokenGoldenCrownEnemyHex : HextechEnemyHexEffect
{
	private const int RewardOptionsLost = 1;

	internal override MonsterHexKind Kind => MonsterHexKind.BrokenGoldenCrown;

	// 与我方破碎金冠同一钩子（Late）：先让其他来源补齐或替换选项，再从末尾去掉。
	internal override bool TryModifyCardRewardOptionsLate(HextechEnemyHexContext context, Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		if (player.RunState != context.RunState
			|| creationOptions.Source != CardCreationSource.Encounter
			|| cardRewardOptions.Count <= 1)
		{
			return false;
		}

		int optionsToRemove = Math.Min(RewardOptionsLost, cardRewardOptions.Count - 1);
		cardRewardOptions.RemoveRange(cardRewardOptions.Count - optionsToRemove, optionsToRemove);
		return true;
	}
}
