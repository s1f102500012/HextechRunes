namespace HextechRunes;

internal static class HextechMayhemActRecovery
{
	public static HextechMayhemActRecoveryResult RecoverResolvedActs(
		RunState runState,
		HextechMayhemActState actState,
		HextechMayhemChoiceHistoryState choiceHistory,
		int hexCountRecoveryBaseline,
		IReadOnlyList<int> playerHexCountsByAct,
		int maxRecoverActIndex,
		int currentStageIndex)
	{
		int recoveryCeiling = Math.Min(currentStageIndex, maxRecoverActIndex);
		if (recoveryCeiling < 0 || runState.Players.Count == 0)
		{
			return HextechMayhemActRecoveryResult.None;
		}

		int choiceRecoverThroughAct = GetHighestActResolvedByRuneChoices(
			runState,
			choiceHistory,
			recoveryCeiling,
			playerHexCountsByAct);
		int countRecoverThroughAct = GetHighestActResolvedByPlayerRuneCounts(
			runState,
			Math.Min(recoveryCeiling, currentStageIndex == 0 ? 0 : currentStageIndex - 1),
			hexCountRecoveryBaseline,
			playerHexCountsByAct);
		int recoverThroughAct = Math.Max(choiceRecoverThroughAct, countRecoverThroughAct);
		if (recoverThroughAct < 0)
		{
			return HextechMayhemActRecoveryResult.None;
		}

		bool changed = false;
		for (int actIndex = 0; actIndex <= recoverThroughAct; actIndex++)
		{
			changed |= actState.TryMarkResolved(actIndex);

			if (TryInferRarityForActFromRuneChoices(choiceHistory, actIndex, out HextechRarityTier rarity)
				|| TryInferRarityForActFromPlayerRelics(runState, hexCountRecoveryBaseline, playerHexCountsByAct, actIndex, out rarity))
			{
				changed |= actState.TrySetRarityIfMissing(actIndex, rarity);
			}
		}

		return new HextechMayhemActRecoveryResult(changed, recoverThroughAct, choiceRecoverThroughAct, countRecoverThroughAct);
	}

	public static string DescribePlayerHexCounts(RunState runState)
	{
		return string.Join(",", runState.Players.Select(player => $"{player.NetId}:{player.Relics.Count(HextechCatalog.IsHextechRelic)}"));
	}

	public static string DescribeRuneChoiceCounts(HextechMayhemChoiceHistoryState choiceHistory)
	{
		return string.Join(",", choiceHistory.GetRuneChoiceRecords()
			.GroupBy(static record => record.ActIndex)
			.OrderBy(static group => group.Key)
			.Select(static group => $"{group.Key}:{group.Count()}"));
	}

	public static int GetMinimumPlayerHexCount(RunState runState)
	{
		int minHexCount = int.MaxValue;
		foreach (Player player in runState.Players)
		{
			int count = player.Relics.Count(HextechCatalog.IsHextechRelic);
			minHexCount = Math.Min(minHexCount, count);
		}

		return minHexCount == int.MaxValue ? 0 : minHexCount;
	}

	private static int GetHighestActResolvedByRuneChoices(
		RunState runState,
		HextechMayhemChoiceHistoryState choiceHistory,
		int maxActIndex,
		IReadOnlyList<int> playerHexCountsByAct)
	{

		IReadOnlyList<HextechRuneChoiceRecord> records = choiceHistory.GetRuneChoiceRecords();
		if (records.Count == 0)
		{
			return -1;
		}

		int highest = -1;
		for (int actIndex = 0; actIndex <= maxActIndex; actIndex++)
		{
			int requiredChoices = GetPlayerHexCountForAct(playerHexCountsByAct, actIndex);
			if (requiredChoices <= 0)
			{
				break;
			}

			ILookup<int, HextechRuneChoiceRecord> choicesByPlayerSlot = records
				.Where(record => record.ActIndex == actIndex)
				.ToLookup(static record => record.PlayerSlot);
			bool allPlayersRecorded = true;
			for (int playerSlot = 0; playerSlot < runState.Players.Count; playerSlot++)
			{
				HashSet<string> ownedRuneEntries = runState.Players[playerSlot].Relics
					.Where(HextechCatalog.IsHextechRelic)
					.Select(static relic => relic.CanonicalId().Entry)
					.ToHashSet(StringComparer.Ordinal);
				for (int choiceOrdinal = 0; choiceOrdinal < requiredChoices; choiceOrdinal++)
				{
					HextechRuneChoiceRecord[] matchingChoices = choicesByPlayerSlot[playerSlot]
						.Where(record => record.ChoiceOrdinal == choiceOrdinal)
						.Take(2)
						.ToArray();
					if (matchingChoices.Length != 1
						|| matchingChoices[0].Selected is not string selected
						|| string.IsNullOrWhiteSpace(selected)
						|| !ownedRuneEntries.Contains(selected))
					{
						allPlayersRecorded = false;
						break;
					}
				}

				if (!allPlayersRecorded)
				{
					break;
				}
			}

			if (!allPlayersRecorded)
			{
				break;
			}

			highest = actIndex;
		}

		return highest;
	}

	private static int GetHighestActResolvedByPlayerRuneCounts(
		RunState runState,
		int maxActIndex,
		int hexCountRecoveryBaseline,
		IReadOnlyList<int> playerHexCountsByAct)
	{
		int minHexCount = GetMinimumPlayerHexCount(runState);
		int loopHexCount = Math.Max(0, minHexCount - hexCountRecoveryBaseline);
		if (loopHexCount <= 0)
		{
			return -1;
		}

		int cumulativeRequired = 0;
		int highest = -1;
		for (int actIndex = 0; actIndex <= maxActIndex; actIndex++)
		{
			cumulativeRequired += GetPlayerHexCountForAct(playerHexCountsByAct, actIndex);
			if (cumulativeRequired <= 0)
			{
				break;
			}

			if (loopHexCount < cumulativeRequired)
			{
				break;
			}

			highest = actIndex;
		}

		return highest;
	}

	private static bool TryInferRarityForActFromRuneChoices(
		HextechMayhemChoiceHistoryState choiceHistory,
		int actIndex,
		out HextechRarityTier rarity)
	{
		foreach (HextechRuneChoiceRecord record in choiceHistory.GetRuneChoiceRecords().Where(record => record.ActIndex == actIndex))
		{
			if (Enum.TryParse(record.Rarity, ignoreCase: true, out rarity))
			{
				return true;
			}
		}

		rarity = default;
		return false;
	}

	private static bool TryInferRarityForActFromPlayerRelics(
		RunState runState,
		int hexCountRecoveryBaseline,
		IReadOnlyList<int> playerHexCountsByAct,
		int actIndex,
		out HextechRarityTier rarity)
	{
		int actHexCount = GetPlayerHexCountForAct(playerHexCountsByAct, actIndex);
		if (actHexCount <= 0)
		{
			rarity = default;
			return false;
		}

		int relicOffset = hexCountRecoveryBaseline;
		for (int previousAct = 0; previousAct < actIndex; previousAct++)
		{
			relicOffset += GetPlayerHexCountForAct(playerHexCountsByAct, previousAct);
		}

		foreach (Player player in runState.Players)
		{
			RelicModel? relic = player.Relics
				.Where(HextechCatalog.IsHextechRelic)
				.ElementAtOrDefault(relicOffset);
			if (HextechCatalog.TryGetPlayerRuneRarity(relic, out rarity))
			{
				return true;
			}
		}

		rarity = default;
		return false;
	}

	private static int GetPlayerHexCountForAct(IReadOnlyList<int> playerHexCountsByAct, int actIndex)
	{
		return HextechHexCountState.GetForAct(
			HextechRuneConfiguration.NormalizePlayerHexCounts(playerHexCountsByAct),
			actIndex,
			endless: false);
	}
}

internal readonly record struct HextechMayhemActRecoveryResult(
	bool Changed,
	int RecoverThroughAct,
	int ChoiceRecoverThroughAct,
	int CountRecoverThroughAct)
{
	public static HextechMayhemActRecoveryResult None => new(false, -1, -1, -1);
}
