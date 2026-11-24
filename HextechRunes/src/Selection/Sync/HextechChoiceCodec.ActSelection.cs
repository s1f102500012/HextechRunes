using MegaCrit.Sts2.Core.GameActions;

namespace HextechRunes;

internal static partial class HextechChoiceCodec
{
	public static PlayerChoiceResult CreateActRoll(
		int actIndex,
		HextechRarityTier rarity,
		MonsterHexKind? monsterHex,
		bool hostUsesBetterMultiplayerScaling,
		IReadOnlyList<int> enemyHexCountsByAct,
		IReadOnlySet<string> disabledPlayerRuneIds,
		HextechRunConfigurationSnapshot runConfigurationSnapshot)
	{
		HextechRunConfigurationSnapshot normalizedSnapshot = HextechRuneConfiguration.NormalizeSnapshot(runConfigurationSnapshot with
		{
			EnemyHexCountsByAct = enemyHexCountsByAct.ToArray(),
			DisabledPlayerRuneIds = disabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal)
		});
		List<int> payload =
		[
			Magic,
			ChoiceKindActRoll,
			actIndex,
			(int)rarity,
			monsterHex.HasValue ? (int)monsterHex.Value : -1,
			hostUsesBetterMultiplayerScaling ? 1 : 0
		];
		int[] normalizedCounts = HextechHexCountState.NormalizeEnemyCounts(enemyHexCountsByAct);
		payload.AddRange(normalizedCounts);
		AppendDisabledPlayerRuneConfig(payload, disabledPlayerRuneIds);
		AppendRunConfigurationSnapshot(payload, normalizedSnapshot);
		return PlayerChoiceResult.FromIndexes(payload);
	}

	public static bool TryDecodeActRoll(
		PlayerChoiceResult result,
		int expectedActIndex,
		out HextechRarityTier rarity,
		out MonsterHexKind? monsterHex,
		out bool hostUsesBetterMultiplayerScaling,
		out int[] enemyHexCountsByAct,
		out HashSet<string> disabledPlayerRuneIds)
	{
		return TryDecodeActRoll(
			result,
			expectedActIndex,
			out rarity,
			out monsterHex,
			out hostUsesBetterMultiplayerScaling,
			out enemyHexCountsByAct,
			out disabledPlayerRuneIds,
			out _);
	}

	public static bool TryDecodeActRoll(
		PlayerChoiceResult result,
		int expectedActIndex,
		out HextechRarityTier rarity,
		out MonsterHexKind? monsterHex,
		out bool hostUsesBetterMultiplayerScaling,
		out int[] enemyHexCountsByAct,
		out HashSet<string> disabledPlayerRuneIds,
		out HextechRunConfigurationSnapshot runConfigurationSnapshot)
	{
		rarity = default;
		monsterHex = null;
		hostUsesBetterMultiplayerScaling = false;
		enemyHexCountsByAct = HextechRuneConfiguration.GetDefaultEnemyHexCountsByAct();
		disabledPlayerRuneIds = [];
		runConfigurationSnapshot = HextechRuneConfiguration.GetDefaultSnapshot();
		if (!TryGetIndexPayload(result, out List<int> payload)
			|| payload.Count < 5
			|| payload[0] != Magic
			|| payload[1] != ChoiceKindActRoll
			|| payload[2] != expectedActIndex)
		{
			return false;
		}

		if (!Enum.IsDefined(typeof(HextechRarityTier), payload[3]))
		{
			return false;
		}

		if (payload[4] >= 0)
		{
			if (!MonsterHexCatalog.TryGetRegisteredMonsterHex(payload[4], out MonsterHexKind decodedHex))
			{
				return false;
			}

			monsterHex = decodedHex;
		}

		rarity = (HextechRarityTier)payload[3];
		hostUsesBetterMultiplayerScaling = payload.Count >= 6 && payload[5] != 0;
		if (payload.Count >= 9)
		{
			enemyHexCountsByAct = HextechHexCountState.NormalizeEnemyCounts(payload.Skip(6).Take(3).ToArray());
			if (!TryDecodeDisabledPlayerRuneConfig(payload, 9, out disabledPlayerRuneIds, out int nextCursor))
			{
				return false;
			}

			runConfigurationSnapshot = HextechRuneConfiguration.NormalizeSnapshot(runConfigurationSnapshot with
			{
				EnemyHexCountsByAct = enemyHexCountsByAct,
				DisabledPlayerRuneIds = disabledPlayerRuneIds
			});
			return TryDecodeRunConfigurationSnapshot(payload, nextCursor, runConfigurationSnapshot, out runConfigurationSnapshot);
		}

		return true;
	}

	public static PlayerChoiceResult CreateActSelectionApplied(int actIndex, int choiceOrdinal)
	{
		return PlayerChoiceResult.FromIndexes([ Magic, ChoiceKindActSelectionApplied, actIndex, choiceOrdinal, 1 ]);
	}

	public static bool TryDecodeActSelectionApplied(PlayerChoiceResult result, int expectedActIndex, int expectedChoiceOrdinal)
	{
		return TryGetIndexPayload(result, out List<int> payload)
			&& payload.Count >= 5
			&& payload[0] == Magic
			&& payload[1] == ChoiceKindActSelectionApplied
			&& payload[2] == expectedActIndex
			&& payload[3] == expectedChoiceOrdinal
			&& payload[4] == 1;
	}
}
