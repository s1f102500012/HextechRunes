using System.Text.Json;

namespace HextechRunes;

internal sealed class HextechMayhemChoiceHistoryState
{
	private string _telemetryChoicesJson = "";
	private string _seenPlayerRuneIdsJson = "";

	public string SavedTelemetryChoicesJson
	{
		get => _telemetryChoicesJson;
		set => _telemetryChoicesJson = value ?? "";
	}

	public string SavedSeenPlayerRuneIdsJson
	{
		get => _seenPlayerRuneIdsJson;
		set => _seenPlayerRuneIdsJson = value ?? "";
	}

	public void Reset()
	{
		_telemetryChoicesJson = "";
		_seenPlayerRuneIdsJson = "";
	}

	public IReadOnlyList<HextechTelemetry.RuneChoiceRecord> GetTelemetryChoiceRecords()
	{
		if (string.IsNullOrWhiteSpace(_telemetryChoicesJson))
		{
			return [];
		}

		try
		{
			return JsonSerializer.Deserialize<List<HextechTelemetry.RuneChoiceRecord>>(_telemetryChoicesJson, HextechTelemetry.JsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Telemetry choices decode failed: {ex.Message}");
			return [];
		}
	}

	public void RecordTelemetryChoice(HextechTelemetry.RuneChoiceRecord record)
	{
		List<HextechTelemetry.RuneChoiceRecord> records = GetTelemetryChoiceRecords().ToList();
		records.RemoveAll(existing =>
			existing.ActIndex == record.ActIndex
			&& existing.PlayerSlot == record.PlayerSlot
			&& existing.ChoiceOrdinal == record.ChoiceOrdinal);
		records.Add(record);
		_telemetryChoicesJson = JsonSerializer.Serialize(records, HextechTelemetry.JsonOptions);
	}

	public HashSet<ModelId> GetSeenPlayerRuneIds(Player player, RunState runState)
	{
		int playerSlot = GetPlayerSlotIndex(player, runState);
		HashSet<string> entries = GetSeenPlayerRuneEntries(playerSlot);

		foreach (string entry in GetTelemetryChoiceRecords()
			.Where(record => record.PlayerSlot == playerSlot)
			.SelectMany(static record => record.Options))
		{
			if (!string.IsNullOrWhiteSpace(entry))
			{
				entries.Add(entry);
			}
		}

		// 条目都来自本类与遥测记录自己序列化的非空条目名。
		return entries.Select(ToSeenRelicId).ToHashSet();
	}

	/// <summary>
	/// 存档里只记条目名;还原时必须用遗物的真实分类(RELIC)。ModelId 是 record,分类不同就永远不相等,
	/// "已见"排除会匹配不上任何候选。
	/// </summary>
	internal static ModelId ToSeenRelicId(string entry)
	{
		return new ModelId(ModelId.SlugifyCategory<RelicModel>(), entry);
	}

	public void RecordSeenPlayerRunes(Player player, IEnumerable<RelicModel> relics, RunState runState)
	{
		List<string> entriesToAdd = [];
		foreach (RelicModel relic in relics)
		{
			ModelId id = relic.CanonicalId();
			if (HextechCatalog.IsHextechRelic(relic) && !string.IsNullOrWhiteSpace(id.Entry))
			{
				entriesToAdd.Add(id.Entry);
			}
		}

		if (entriesToAdd.Count == 0)
		{
			return;
		}

		int playerSlot = GetPlayerSlotIndex(player, runState);
		Dictionary<int, HashSet<string>> seenBySlot = DecodeSeenPlayerRuneEntries();
		if (!seenBySlot.TryGetValue(playerSlot, out HashSet<string>? seenEntries))
		{
			seenEntries = new HashSet<string>(StringComparer.Ordinal);
			seenBySlot[playerSlot] = seenEntries;
		}

		bool changed = false;
		foreach (string entry in entriesToAdd)
		{
			changed |= seenEntries.Add(entry);
		}

		if (changed)
		{
			// 顶层键序也要规范化：Dictionary 插入序依赖"两端见到槽位的顺序相同"这一隐式前提。
			_seenPlayerRuneIdsJson = JsonSerializer.Serialize(
				seenBySlot
					.OrderBy(static pair => pair.Key)
					.ToDictionary(
						static pair => pair.Key.ToString(),
						static pair => pair.Value.OrderBy(static entry => entry, StringComparer.Ordinal).ToArray()),
				HextechTelemetry.JsonOptions);
		}
	}

	private HashSet<string> GetSeenPlayerRuneEntries(int playerSlot)
	{
		Dictionary<int, HashSet<string>> seenBySlot = DecodeSeenPlayerRuneEntries();
		if (seenBySlot.TryGetValue(playerSlot, out HashSet<string>? entries))
		{
			return entries.ToHashSet(StringComparer.Ordinal);
		}

		return new HashSet<string>(StringComparer.Ordinal);
	}

	private Dictionary<int, HashSet<string>> DecodeSeenPlayerRuneEntries()
	{
		Dictionary<int, HashSet<string>> result = [];
		if (string.IsNullOrWhiteSpace(_seenPlayerRuneIdsJson))
		{
			return result;
		}

		try
		{
			Dictionary<string, string[]>? decoded = JsonSerializer.Deserialize<Dictionary<string, string[]>>(_seenPlayerRuneIdsJson, HextechTelemetry.JsonOptions);
			if (decoded == null)
			{
				return result;
			}

			foreach ((string slotText, string[] entries) in decoded)
			{
				if (int.TryParse(slotText, out int slot))
				{
					result[slot] = entries
						.Where(static entry => !string.IsNullOrWhiteSpace(entry))
						.ToHashSet(StringComparer.Ordinal);
				}
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Seen player rune ids decode failed: {ex.Message}");
		}

		return result;
	}

	// 找不到的玩家记在 0 号槽(沿用既有存档口径)。
	private static int GetPlayerSlotIndex(Player player, RunState runState)
	{
		int slot = runState.GetPlayerSlotIndex(player);
		return slot >= 0 ? slot : 0;
	}
}
