using System.Text.Json;

namespace HextechRunes;

internal readonly record struct HextechRuneSelectionJournalEntry(
	ModelId SelectedId,
	// Applied 以遗物已进入背包为提交边界；AfterObtained 若在插入后失败，不可重跑 Obtain，
	// 否则会重复遗物及已执行的拾取副作用。该异常必须中止联机事务并保留诊断。
	bool Applied, string SelectionData = "");

internal sealed class HextechRuneSelectionJournalState
{
	private const int CurrentVersion = 1;

	private readonly Dictionary<JournalKey, HextechRuneSelectionJournalEntry> _entries = new();
	private readonly SortedDictionary<ulong, int> _characterWeights = new();

	internal int GetCharacterWeight(ulong playerNetId)
	{
		return _characterWeights.GetValueOrDefault(playerNetId, HextechWeightedRuneOptions.InitialCharacterWeightPercent);
	}

	internal void CommitCharacterWeight(ulong playerNetId, int weight)
	{
		if (weight < 0 || weight % HextechWeightedRuneOptions.WeightStep != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(weight));
		}

		// 提交绝对值而非增量：恢复检查点或重复确认不能再次推进掉落权重。
		_characterWeights[playerNetId] = weight;
	}

	public bool TryGet(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		out HextechRuneSelectionJournalEntry entry)
	{
		return _entries.TryGetValue(CreateKey(actIndex, choiceOrdinal, playerNetId), out entry);
	}

	public bool HasEntriesForAct(int actIndex)
	{
		if (actIndex < 0)
		{
			return false;
		}

		return _entries.Keys.Any(key => key.ActIndex == actIndex);
	}

	public bool RecordSelected(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		ModelId selectedId, string selectionData = "")
	{
		JournalKey key = CreateKey(actIndex, choiceOrdinal, playerNetId);
		ValidateModelId(selectedId);

		if (_entries.TryGetValue(key, out HextechRuneSelectionJournalEntry existing))
		{
			if (existing.SelectedId != selectedId || existing.SelectionData != selectionData)
			{
				throw new InvalidOperationException(
					$"[{ModInfo.Id}][Mayhem] Rune selection journal conflict: "
					+ $"act={actIndex} ordinal={choiceOrdinal} player={playerNetId} "
					+ $"existing={Describe(existing.SelectedId)} incoming={Describe(selectedId)}.");
			}

			return false;
		}

		_entries.Add(key, new HextechRuneSelectionJournalEntry(selectedId, Applied: false, selectionData));
		return true;
	}

	public bool MarkApplied(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId,
		ModelId selectedId)
	{
		JournalKey key = CreateKey(actIndex, choiceOrdinal, playerNetId);
		ValidateModelId(selectedId);

		if (!_entries.TryGetValue(key, out HextechRuneSelectionJournalEntry existing))
		{
			throw new InvalidOperationException(
				$"[{ModInfo.Id}][Mayhem] Rune selection journal cannot mark an unrecorded selection as applied: "
				+ $"act={actIndex} ordinal={choiceOrdinal} player={playerNetId} selected={Describe(selectedId)}.");
		}

		if (existing.SelectedId != selectedId)
		{
			throw new InvalidOperationException(
				$"[{ModInfo.Id}][Mayhem] Rune selection journal apply mismatch: "
				+ $"act={actIndex} ordinal={choiceOrdinal} player={playerNetId} "
				+ $"recorded={Describe(existing.SelectedId)} applied={Describe(selectedId)}.");
		}

		if (existing.Applied)
		{
			return false;
		}

		_entries[key] = existing with { Applied = true };
		return true;
	}

	public string Serialize()
	{
		if (_entries.Count == 0 && _characterWeights.Count == 0)
		{
			return "";
		}

		JournalJsonEntry[] entries = _entries
			.OrderBy(static pair => pair.Key.ActIndex)
			.ThenBy(static pair => pair.Key.ChoiceOrdinal)
			.ThenBy(static pair => pair.Key.PlayerNetId)
			.Select(static pair => new JournalJsonEntry(
				pair.Key.ActIndex,
				pair.Key.ChoiceOrdinal,
				pair.Key.PlayerNetId,
				pair.Value.SelectedId.Category,
				pair.Value.SelectedId.Entry,
				pair.Value.Applied, pair.Value.SelectionData))
			.ToArray();
		return JsonSerializer.Serialize(
			new JournalJsonSnapshot(CurrentVersion, entries, _characterWeights),
			HextechMayhemSavedJson.Options);
	}

	public void Restore(string? json)
	{
		_entries.Clear();
		_characterWeights.Clear();
		if (string.IsNullOrWhiteSpace(json))
		{
			return;
		}

		JournalJsonSnapshot? snapshot;
		try
		{
			snapshot = JsonSerializer.Deserialize<JournalJsonSnapshot>(
				json,
				HextechMayhemSavedJson.Options);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Rune selection journal restore failed; journal cleared: {ex.Message}");
			return;
		}

		if (snapshot == null || snapshot.Version != CurrentVersion || snapshot.Entries == null)
		{
			HextechLog.Warn(
				"Mayhem", $"Rune selection journal restore ignored unsupported payload: "
				+ $"version={snapshot?.Version.ToString() ?? "null"}.");
			return;
		}

		if (snapshot.CharacterWeights != null)
		{
			foreach ((ulong playerId, int weight) in snapshot.CharacterWeights)
			{
				if (weight >= 0 && weight % HextechWeightedRuneOptions.WeightStep == 0)
				{
					_characterWeights[playerId] = weight;
				}
			}
		}

		// JSON 由 Serialize 从字典写出,键不重复;这里只跳过字段不完整的条目。
		int ignored = 0;
		foreach (JournalJsonEntry? serialized in snapshot.Entries)
		{
			if (serialized == null
				|| serialized.ActIndex < 0
				|| serialized.ChoiceOrdinal < 0
				|| string.IsNullOrWhiteSpace(serialized.Category)
				|| string.IsNullOrWhiteSpace(serialized.Entry)
				|| !_entries.TryAdd(
					new JournalKey(serialized.ActIndex, serialized.ChoiceOrdinal, serialized.PlayerNetId),
					new HextechRuneSelectionJournalEntry(
						new ModelId(serialized.Category, serialized.Entry),
						serialized.Applied,
						serialized.SelectionData ?? "")))
			{
				ignored++;
			}
		}

		if (ignored > 0)
		{
			HextechLog.Warn(
				"Mayhem", $"Rune selection journal restore ignored invalid entries: "
				+ $"ignored={ignored} restored={_entries.Count}.");
		}
	}

	public void Reset(bool preserveCharacterWeights = false)
	{
		_entries.Clear();
		if (!preserveCharacterWeights)
		{
			_characterWeights.Clear();
		}
	}

	private static JournalKey CreateKey(
		int actIndex,
		int choiceOrdinal,
		ulong playerNetId)
	{
		if (actIndex < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(actIndex), actIndex, "Act index must be non-negative.");
		}
		if (choiceOrdinal < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(choiceOrdinal), choiceOrdinal, "Choice ordinal must be non-negative.");
		}

		return new JournalKey(actIndex, choiceOrdinal, playerNetId);
	}

	private static void ValidateModelId(ModelId id)
	{
		if (string.IsNullOrWhiteSpace(id.Category) || string.IsNullOrWhiteSpace(id.Entry))
		{
			throw new ArgumentException("Rune selection journal ModelId must include both category and entry.", nameof(id));
		}
	}

	private static string Describe(ModelId id)
	{
		return $"{id.Category}:{id.Entry}";
	}

	private readonly record struct JournalKey(
		int ActIndex,
		int ChoiceOrdinal,
		ulong PlayerNetId);

	private sealed record JournalJsonSnapshot(
		int Version,
		JournalJsonEntry?[]? Entries,
		SortedDictionary<ulong, int>? CharacterWeights = null);

	private sealed record JournalJsonEntry(
		int ActIndex,
		int ChoiceOrdinal,
		ulong PlayerNetId,
		string Category,
		string Entry,
		bool Applied, string SelectionData = "");
}
