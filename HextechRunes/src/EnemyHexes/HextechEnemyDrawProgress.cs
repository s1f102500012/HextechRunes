namespace HextechRunes;

/// <summary>
/// "玩家每抽 X 张牌"类敌方海克斯的抽牌进度：全队共用一个计数，所有玩家的抽牌合在一起算，
/// 零头不会因为分散在不同玩家身上而丢失。阈值不随人数放大，每次跨过阈值的奖励也不再按人数放大，
/// 所以全队均匀抽牌时总奖励与按人分别计数时相同。
/// </summary>
internal static class HextechEnemyDrawProgress
{
	// 计数字典沿用按玩家 NetId 存的旧格式；全队合计固定存在这个键上（单机玩家的 NetId 本来就是 0）。
	internal const ulong TeamCountKey = 0UL;

	internal static int RecordDraw(Dictionary<ulong, int> counts, int threshold)
	{
		return RecordTotal(counts, GetTeamCount(counts) + 1, threshold);
	}

	internal static int RecordTotal(Dictionary<ulong, int> counts, int drawnCards, int threshold)
	{
		int previous = GetTeamCount(counts);
		if (drawnCards <= previous)
		{
			return 0;
		}

		counts.Clear();
		counts[TeamCountKey] = drawnCards;
		return HextechRelicBase.CountThresholdCrossings(previous, drawnCards, threshold);
	}

	internal static int ResolveFromHistory(Dictionary<ulong, int> counts, HextechCombatState combatState, int threshold)
	{
		// 联机在双方都会经过的结算点按战斗历史补记全队抽牌；重复结算不重复发奖。
		int totalDrawn = combatState.Players.Sum(static player => HextechCombatHistoryHelper.CountOwnedCardsDrawn(player));
		return RecordTotal(counts, totalDrawn, threshold);
	}

	// 旧版本按玩家分别存的进度（战斗中途读档）按合计接上，避免读档后重复发奖。
	private static int GetTeamCount(Dictionary<ulong, int> counts)
	{
		return counts.TryGetValue(TeamCountKey, out int teamCount) ? teamCount : counts.Values.Sum();
	}
}
