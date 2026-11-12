namespace HextechRunes;

internal static class HextechStableModelIdListCodec
{
	public const int Version = -3;
	// 选择候选、已见历史分块等共用的数量上限；已见历史按它分块，改它会改变线格式。
	public const int MaxCount = 64;
	public const int MaxSerializedLength = 128;

	public static void Append(List<int> payload, IEnumerable<ModelId> modelIds)
	{
		Append(payload, modelIds, MaxCount);
	}

	/// <summary>同一线格式（版本、数量、逐项长度前缀字符串），只是数量上限由调用方给出。</summary>
	public static void Append(List<int> payload, IEnumerable<ModelId> modelIds, int maxCount)
	{
		ModelId[] ids = modelIds.ToArray();
		if (ids.Length > maxCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(modelIds),
				ids.Length,
				$"ModelId payload count must not exceed {maxCount}.");
		}

		string[] serializedIds = new string[ids.Length];
		for (int i = 0; i < ids.Length; i++)
		{
			string serialized = ids[i].ToString();
			if (serialized.Length > MaxSerializedLength)
			{
				throw new ArgumentException(
					$"Serialized ModelId length must not exceed {MaxSerializedLength}: {serialized.Length}.",
					nameof(modelIds));
			}

			serializedIds[i] = serialized;
		}

		payload.Add(Version);
		payload.Add(ids.Length);
		foreach (string serialized in serializedIds)
		{
			HextechChoiceCodec.AppendLengthPrefixedString(payload, serialized);
		}
	}

	public static bool TryDecode(IReadOnlyList<int> payload, int cursor, out List<ModelId> modelIds, out int nextCursor)
	{
		return TryDecode(payload, cursor, MaxCount, out modelIds, out nextCursor);
	}

	public static bool TryDecode(IReadOnlyList<int> payload, int cursor, int maxCount, out List<ModelId> modelIds, out int nextCursor)
	{
		modelIds = [];
		nextCursor = cursor;
		if (!HextechChoiceCodec.HasRemaining(payload, cursor, 1) || payload[cursor] != Version)
		{
			return false;
		}

		cursor++;
		if (!HextechChoiceCodec.HasRemaining(payload, cursor, 1))
		{
			return false;
		}

		int count = payload[cursor++];
		if (count < 0 || count > maxCount)
		{
			return false;
		}

		for (int i = 0; i < count; i++)
		{
			if (!HextechChoiceCodec.TryReadLengthPrefixedString(payload, ref cursor, MaxSerializedLength, out string? serialized))
			{
				modelIds.Clear();
				return false;
			}

			try
			{
				modelIds.Add(ModelId.Deserialize(serialized));
			}
			catch
			{
				modelIds.Clear();
				return false;
			}
		}

		nextCursor = cursor;
		return true;
	}
}
