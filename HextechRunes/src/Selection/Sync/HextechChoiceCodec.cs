using System.Diagnostics.CodeAnalysis;
using System.Text;
using MegaCrit.Sts2.Core.GameActions;

namespace HextechRunes;

internal readonly record struct EnemyHexAdjustmentPayload(
	int ActIndex,
	int Sequence,
	IReadOnlyList<MonsterHexKind?> MonsterHexes,
	IReadOnlyList<int> RerollCounts,
	bool IsFinal);

internal static partial class HextechChoiceCodec
{
	private const int Magic = 0x48585452; // HXTR
	private const int ChoiceKindActRoll = 1;
	private const int ChoiceKindRuneSelection = 2;
	private const int ChoiceKindActSelectionApplied = 3;
	private const int ChoiceKindEnemyHexAdjustment = 4;
	private const int ChoiceKindForgeSelection = 5;
	private const int ChoiceKindRandomRuneGrant = 6;
	private const int ChoiceKindRelicOptionSelection = 7;
	private const int EnemyHexAdjustmentListVersion = -2;
	private const int PlayerRuneConfigBitsetVersion = -4;
	private const int LegacyRunConfigurationSnapshotVersion = -5;
	private const int LegacyRerollRunConfigurationSnapshotVersion = -6;
	private const int PreviousRunConfigurationSnapshotVersion = -7;
	private const int PreviousSingleRarityRunConfigurationSnapshotVersion = -8;
	private const int RunConfigurationSnapshotVersion = -9;
	private const int PlayerRuneConfigBitsPerWord = 30;
	private const int MaxPlayerRuneConfigBitsetWords = 64;
	// 配置快照里两份禁用列表的健全性上限，只防畸形载荷，不是协议语义：原版按"完整 int 数量 + 每项完整 int"
	// 写选择结果的整数列表，不限数量。取值覆盖注册表规模并留余量；两份都满额且锻造器 ID 都取最长 128 字符时
	// 载荷约 270 KB，仍低于 Steam 单条消息上限 512 KiB。
	internal const int MaxDisabledMonsterHexes = 1024;
	internal const int MaxDisabledForgeIds = 512;
	private const int MaxChoiceListCount = HextechStableModelIdListCodec.MaxCount;
	private const uint Fnv1aOffsetBasis = 2166136261U;
	private const uint Fnv1aPrime = 16777619U;

	public static int ComputeOperationToken(
		string operationKind,
		uint choiceId,
		ulong playerNetId,
		string context)
	{
		ArgumentNullException.ThrowIfNull(operationKind);
		ArgumentNullException.ThrowIfNull(context);

		uint hash = Fnv1aOffsetBasis;
		AppendFnvString(ref hash, operationKind);
		AppendFnvUInt32(ref hash, choiceId);
		AppendFnvUInt64(ref hash, playerNetId);
		AppendFnvString(ref hash, context);
		return unchecked((int)hash);
	}

	public static bool TryGetIndexPayload(PlayerChoiceResult result, out List<int> payload)
	{
		payload = [];
		try
		{
			List<int>? indexes = result.AsIndexes();
			if (indexes == null)
			{
				return false;
			}

			payload = indexes;
			return true;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	internal static bool HasRemaining(IReadOnlyList<int> payload, int cursor, int count)
	{
		return cursor >= 0
			&& count >= 0
			&& cursor <= payload.Count
			&& count <= payload.Count - cursor;
	}

	/// <summary>整数载荷里的字符串格式:长度后接逐个 UTF-16 码元。模型 ID 列表与生成符文配方共用。</summary>
	internal static void AppendLengthPrefixedString(List<int> payload, string value)
	{
		payload.Add(value.Length);
		foreach (char ch in value)
		{
			payload.Add(ch);
		}
	}

	internal static bool TryReadLengthPrefixedString(IReadOnlyList<int> payload, ref int cursor, int maxLength, [NotNullWhen(true)] out string? value)
	{
		value = null;
		if (!HasRemaining(payload, cursor, 1))
		{
			return false;
		}

		int length = payload[cursor];
		if (length < 0 || length > maxLength || !HasRemaining(payload, cursor + 1, length))
		{
			return false;
		}

		char[] chars = new char[length];
		for (int i = 0; i < length; i++)
		{
			int code = payload[cursor + 1 + i];
			if (code < char.MinValue || code > char.MaxValue)
			{
				return false;
			}

			chars[i] = (char)code;
		}

		cursor += 1 + length;
		value = new string(chars);
		return true;
	}

	private static void ValidateProtocolCount(int count, int maximum, string parameterName)
	{
		if (count < 0 || count > maximum)
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				count,
				$"Protocol list count must be between 0 and {maximum}.");
		}
	}

	private static void AppendFnvString(ref uint hash, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		AppendFnvUInt32(ref hash, checked((uint)bytes.Length));
		foreach (byte valueByte in bytes)
		{
			AppendFnvByte(ref hash, valueByte);
		}
	}

	private static void AppendFnvUInt32(ref uint hash, uint value)
	{
		for (int shift = 0; shift < 32; shift += 8)
		{
			AppendFnvByte(ref hash, unchecked((byte)(value >> shift)));
		}
	}

	private static void AppendFnvUInt64(ref uint hash, ulong value)
	{
		for (int shift = 0; shift < 64; shift += 8)
		{
			AppendFnvByte(ref hash, unchecked((byte)(value >> shift)));
		}
	}

	private static void AppendFnvByte(ref uint hash, byte value)
	{
		hash = unchecked((hash ^ value) * Fnv1aPrime);
	}
}
