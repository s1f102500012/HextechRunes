namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal int GetCharacterRuneWeight(ulong playerNetId) => _runContext.RuneSelectionJournal.GetCharacterWeight(playerNetId);

	internal void CommitCharacterRuneWeight(Player player, IReadOnlyList<RelicModel> options)
	{
		if (options is HextechWeightedRuneOptions weighted)
		{
			_runContext.RuneSelectionJournal.CommitCharacterWeight(player.NetId, weighted.CharacterWeightPercent);
		}
	}

	internal bool HasRuneSelectionJournalEntriesForAct(int actIndex)
	{
		return _runContext.RuneSelectionJournal.HasEntriesForAct(actIndex);
	}

	internal bool TryGetRuneSelectionJournalEntry(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		out HextechRuneSelectionJournalEntry entry)
	{
		return _runContext.RuneSelectionJournal.TryGet(
			actIndex,
			choiceOrdinal,
			playerNetId,
			out entry);
	}

	internal bool TryRecoverRuneSelectionJournalEntryFromChoiceHistory(
		int actIndex,
		int choiceOrdinal,
		Player player,
		out HextechRuneSelectionJournalEntry entry)
	{
		entry = default;
		int playerSlot = RunState.GetPlayerSlotIndex(player);
		if (playerSlot < 0)
		{
			return false;
		}

		HextechRuneChoiceRecord[] matchingRecords = ChoiceHistory.GetRuneChoiceRecords()
			.Where(record =>
				record.ActIndex == actIndex
				&& record.ChoiceOrdinal == choiceOrdinal
				&& record.PlayerSlot == playerSlot
				&& !string.IsNullOrWhiteSpace(record.Selected))
			.Take(2)
			.ToArray();
		if (matchingRecords.Length != 1)
		{
			return false;
		}

		HextechRuneChoiceRecord matchingRecord = matchingRecords[0];
		if (matchingRecord.Selected is not string selectedEntry
			|| !matchingRecord.Options.Contains(selectedEntry, StringComparer.Ordinal)
			|| !Enum.TryParse(
				matchingRecord.Rarity,
				ignoreCase: false,
				out HextechRarityTier recordedRarity)
			|| ActState.GetRarity(actIndex) != recordedRarity)
		{
			return false;
		}

		ModelId? selectedId = HextechCatalog.GetConfigurablePlayerRuneIds()
			.SingleOrDefault(id => string.Equals(id.Entry, selectedEntry, StringComparison.Ordinal));
		if (selectedId == null || ModelDb.GetById<RelicModel>(selectedId) is IHextechGeneratedRune)
		{
			return false;
		}

		_runContext.RuneSelectionJournal.RecordSelected(
			actIndex,
			choiceOrdinal,
			player.NetId,
			selectedId);
		entry = new HextechRuneSelectionJournalEntry(selectedId, Applied: false);
		return true;
	}

	internal bool RecordRuneSelectionJournalSelection(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		ModelId selectedId, string selectionData = "")
	{
		return _runContext.RuneSelectionJournal.RecordSelected(
			actIndex,
			choiceOrdinal,
			playerNetId,
			selectedId, selectionData);
	}

	internal bool MarkRuneSelectionJournalApplied(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		ModelId selectedId)
	{
		return _runContext.RuneSelectionJournal.MarkApplied(
			actIndex,
			choiceOrdinal,
			playerNetId,
			selectedId);
	}
}
