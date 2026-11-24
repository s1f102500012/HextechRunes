using MegaCrit.Sts2.Core.GameActions;

namespace HextechRunes;

internal static partial class HextechChoiceCodec
{
	public static PlayerChoiceResult CreateEnemyHexAdjustment(
		int operationToken,
		EnemyHexAdjustmentPayload payload)
	{
		if (payload.Sequence < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(payload), payload.Sequence, "Enemy adjustment sequence must be non-negative.");
		}

		ValidateProtocolCount(payload.MonsterHexes.Count, MaxChoiceListCount, nameof(payload));
		ValidateProtocolCount(payload.RerollCounts.Count, MaxChoiceListCount, nameof(payload));
		List<int> indexes =
		[
			Magic,
			ChoiceKindEnemyHexAdjustment,
			payload.ActIndex,
			payload.Sequence,
			operationToken,
			EnemyHexAdjustmentListVersion,
			payload.IsFinal ? 1 : 0,
			payload.MonsterHexes.Count
		];
		indexes.AddRange(payload.MonsterHexes.Select(static hex => hex.HasValue ? (int)hex.Value : -1));
		indexes.Add(payload.RerollCounts.Count);
		indexes.AddRange(payload.RerollCounts.Select(static count => Math.Max(0, count)));
		return PlayerChoiceResult.FromIndexes(indexes);
	}

	public static bool TryDecodeEnemyHexAdjustment(
		PlayerChoiceResult result,
		int expectedOperationToken,
		int expectedActIndex,
		int expectedSequence,
		out EnemyHexAdjustmentPayload payload)
	{
		payload = default;
		if (expectedSequence < 0
			|| !TryDecodeEnemyHexAdjustmentCore(
				result,
				expectedOperationToken,
				expectedActIndex,
				out EnemyHexAdjustmentPayload decoded)
			|| decoded.Sequence != expectedSequence)
		{
			return false;
		}

		payload = decoded;
		return true;
	}

	private static bool TryDecodeEnemyHexAdjustmentCore(
		PlayerChoiceResult result,
		int expectedOperationToken,
		int expectedActIndex,
		out EnemyHexAdjustmentPayload payload)
	{
		payload = default;
		if (!TryGetIndexPayload(result, out List<int> indexes)
			|| indexes.Count < 8
			|| indexes[0] != Magic
			|| indexes[1] != ChoiceKindEnemyHexAdjustment
			|| indexes[2] != expectedActIndex
			|| indexes[3] < 0
			|| indexes[4] != expectedOperationToken
			|| indexes[5] != EnemyHexAdjustmentListVersion)
		{
			return false;
		}

		bool isFinal = indexes[6] != 0;
		int hexCount = indexes[7];
		int cursor = 8;
		if (hexCount < 0
			|| hexCount > MaxChoiceListCount
			|| !HasRemaining(indexes, cursor, hexCount)
			|| !HasRemaining(indexes, cursor + hexCount, 1))
		{
			return false;
		}

		List<MonsterHexKind?> monsterHexes = new(hexCount);
		for (int i = 0; i < hexCount; i++)
		{
			int rawHex = indexes[cursor + i];
			if (rawHex < 0)
			{
				monsterHexes.Add(null);
				continue;
			}

			if (!MonsterHexCatalog.TryGetRegisteredMonsterHex(rawHex, out MonsterHexKind hex))
			{
				return false;
			}

			monsterHexes.Add(hex);
		}

		cursor += hexCount;
		int rerollCount = indexes[cursor];
		cursor++;
		if (rerollCount < 0
			|| rerollCount > MaxChoiceListCount
			|| !HasRemaining(indexes, cursor, rerollCount))
		{
			return false;
		}

		List<int> rerollCounts = indexes.Skip(cursor).Take(rerollCount).Select(static count => Math.Max(0, count)).ToList();
		while (rerollCounts.Count < monsterHexes.Count)
		{
			rerollCounts.Add(0);
		}

		payload = new EnemyHexAdjustmentPayload(
			indexes[2],
			indexes[3],
			monsterHexes,
			rerollCounts,
			isFinal);
		return true;
	}
}
