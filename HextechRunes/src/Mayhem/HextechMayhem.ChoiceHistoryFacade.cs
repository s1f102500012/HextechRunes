namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public IReadOnlyList<HextechRuneChoiceRecord> GetRuneChoiceRecords()
	{
		return ChoiceHistory.GetRuneChoiceRecords();
	}

	/// <summary>
	/// 记下一次三选一的结果（同一幕、同一玩家、同一序号重复记录时以后一次为准）。
	/// 记录失败只记日志：它影响的是读档恢复与对局结束后的上报，不能打断正在进行的选择结算。
	/// </summary>
	public void RecordRuneChoice(
		int actIndex,
		HextechRarityTier rarity,
		Player player,
		IReadOnlyList<RelicModel> options,
		RelicModel selected,
		int rerollCount,
		int choiceOrdinal)
	{
		try
		{
			// 原版 GetPlayerSlotIndex 就是 Players.IndexOf(Player 不重写相等性,即按引用);找不到时沿用旧口径记到 0 号槽。
			int playerSlot = Math.Max(0, RunState.GetPlayerSlotIndex(player));
			ChoiceHistory.RecordRuneChoice(new HextechRuneChoiceRecord(
				actIndex,
				playerSlot,
				Math.Max(0, choiceOrdinal),
				rarity.ToString(),
				options.Select(static relic => relic.CanonicalId().Entry).ToArray(),
				selected.CanonicalId().Entry,
				Math.Max(0, rerollCount)));
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Rune choice record failed: {ex.Message}");
		}
	}

	public HashSet<ModelId> GetSeenPlayerRuneIds(Player player)
	{
		return ChoiceHistory.GetSeenPlayerRuneIds(player, RunState);
	}

	public void RecordSeenPlayerRunes(Player player, IEnumerable<RelicModel> relics)
	{
		ChoiceHistory.RecordSeenPlayerRunes(player, relics, RunState);
	}

	private string DescribePlayerHexCounts()
	{
		return HextechMayhemActRecovery.DescribePlayerHexCounts(RunState);
	}

	private string DescribeRuneChoiceCounts()
	{
		return HextechMayhemActRecovery.DescribeRuneChoiceCounts(ChoiceHistory);
	}
}
