namespace HextechRunes;

internal static partial class HextechChoiceCodec
{
	private static void AppendDisabledPlayerRuneConfig(List<int> payload, IReadOnlySet<string> disabledPlayerRuneIds)
	{
		IReadOnlyList<ModelId> ids = PlayerRuneIdsByOrdinal;
		int wordCount = ids.Count / PlayerRuneConfigBitsPerWord
			+ (ids.Count % PlayerRuneConfigBitsPerWord == 0 ? 0 : 1);
		ValidateProtocolCount(wordCount, MaxPlayerRuneConfigBitsetWords, nameof(disabledPlayerRuneIds));
		int[] words = new int[wordCount];
		for (int i = 0; i < ids.Count; i++)
		{
			if (!disabledPlayerRuneIds.Contains(ids[i].Entry))
			{
				continue;
			}

			words[i / PlayerRuneConfigBitsPerWord] |= 1 << (i % PlayerRuneConfigBitsPerWord);
		}

		payload.Add(PlayerRuneConfigBitsetVersion);
		payload.Add(wordCount);
		payload.AddRange(words);
	}

	private static bool TryDecodeDisabledPlayerRuneConfig(List<int> payload, int cursor, out HashSet<string> disabledPlayerRuneIds, out int nextCursor)
	{
		disabledPlayerRuneIds = [];
		nextCursor = cursor;
		if (payload.Count <= cursor)
		{
			return true;
		}

		if (payload[cursor] != PlayerRuneConfigBitsetVersion)
		{
			return true;
		}

		cursor++;
		if (payload.Count <= cursor)
		{
			return false;
		}

		int wordCount = payload[cursor++];
		if (wordCount < 0
			|| wordCount > MaxPlayerRuneConfigBitsetWords
			|| !HasRemaining(payload, cursor, wordCount))
		{
			return false;
		}

		IReadOnlyList<ModelId> ids = PlayerRuneIdsByOrdinal;
		for (int i = 0; i < ids.Count; i++)
		{
			int wordIndex = i / PlayerRuneConfigBitsPerWord;
			int bitIndex = i % PlayerRuneConfigBitsPerWord;
			if (wordIndex < wordCount && (payload[cursor + wordIndex] & (1 << bitIndex)) != 0)
			{
				disabledPlayerRuneIds.Add(ids[i].Entry);
			}
		}

		nextCursor = cursor + wordCount;
		return true;
	}

	private static void AppendRunConfigurationSnapshot(List<int> payload, HextechRunConfigurationSnapshot snapshot)
	{
		payload.Add(RunConfigurationSnapshotVersion);
		payload.AddRange(HextechRuneConfiguration.NormalizePlayerHexCounts(snapshot.PlayerHexCountsByAct));
		payload.AddRange(HextechHexCountState.NormalizeEnemyCounts(snapshot.EnemyHexCountsByAct));
		payload.Add(HextechRuneConfiguration.ClampRerollLimit(snapshot.PlayerRuneRerollLimit));
		payload.Add(HextechRuneConfiguration.ClampRerollLimit(snapshot.MonsterHexRerollLimit));
		foreach (HextechRarityWeights weights in snapshot.RuneRarityWeightsByAct)
		{
			AppendRarityWeights(payload, weights);
		}
		payload.Add(snapshot.PreventConsecutiveSilverRunes ? 1 : 0);
		payload.Add(HextechRuneConfiguration.ClampGoldenRerollChancePercent(snapshot.GoldenRerollChancePercent));
		AppendRarityWeights(payload, snapshot.ForgeRarityWeights);
		payload.Add(HextechRuneConfiguration.ClampRandomForgeShopPrice(snapshot.RandomForgeShopPrice));
		payload.Add(snapshot.RandomForgeDirectGrant ? 1 : 0);

		MonsterHexKind[] disabledMonsterHexes = snapshot.DisabledMonsterHexIds
			.Select(static id => Enum.TryParse(id, out MonsterHexKind kind) ? (MonsterHexKind?)kind : null)
			.OfType<MonsterHexKind>()
			.OrderBy(static kind => (int)kind)
			.ToArray();
		ValidateProtocolCount(disabledMonsterHexes.Length, MaxDisabledMonsterHexes, nameof(snapshot));
		payload.Add(disabledMonsterHexes.Length);
		payload.AddRange(disabledMonsterHexes.Select(static kind => (int)kind));

		HextechStableModelIdListCodec.Append(
			payload,
			snapshot.DisabledForgeIds
				.Select(static entry => new ModelId(ModInfo.Id, entry))
				.OrderBy(static id => id.Entry, StringComparer.Ordinal),
			MaxDisabledForgeIds);

		// 未版本化的尾部可选 int,按追加顺序解码,避免改 snapshot 版本号/定长计数:
		// 1) 模组总开关:旧 payload 没有时解码端回退到 fallback(默认开启);
		// 2) 混沌海克斯概率:旧 payload 没有时回退到 DefaultChaosRuneChancePercent。
		// 以后再加尾部字段只能继续往后追加,不能插入或重排。
		payload.Add(snapshot.ModEnabled ? 1 : 0);
		payload.Add(HextechRuneConfiguration.ClampChaosRuneChancePercent(snapshot.ChaosRuneChancePercent));
	}

	private static bool TryDecodeRunConfigurationSnapshot(
		List<int> payload,
		int cursor,
		HextechRunConfigurationSnapshot fallback,
		out HextechRunConfigurationSnapshot snapshot)
	{
		snapshot = fallback;
		if (payload.Count <= cursor)
		{
			return true;
		}

		int snapshotVersion = payload[cursor];
		if (snapshotVersion != RunConfigurationSnapshotVersion
			&& snapshotVersion != PreviousSingleRarityRunConfigurationSnapshotVersion
			&& snapshotVersion != PreviousRunConfigurationSnapshotVersion
			&& snapshotVersion != LegacyRerollRunConfigurationSnapshotVersion
			&& snapshotVersion != LegacyRunConfigurationSnapshotVersion)
		{
			return true;
		}

		cursor++;
		int fixedIntCount = snapshotVersion switch
		{
			RunConfigurationSnapshotVersion => 3 + 3 + 2 + 9 + 1 + 1 + 3 + 1 + 1,
			PreviousSingleRarityRunConfigurationSnapshotVersion => 3 + 3 + 2 + 3 + 1 + 1 + 3 + 1 + 1,
			PreviousRunConfigurationSnapshotVersion => 3 + 3 + 2 + 3 + 1 + 3 + 1 + 1,
			LegacyRerollRunConfigurationSnapshotVersion => 3 + 3 + 2 + 3 + 3 + 3 + 3 + 1 + 1,
			_ => 3 + 3 + 3 + 3 + 3 + 3 + 1
		};
		if (!HasRemaining(payload, cursor, fixedIntCount))
		{
			return false;
		}

		int[] playerHexCounts = HextechRuneConfiguration.NormalizePlayerHexCounts(payload.Skip(cursor).Take(3).ToArray());
		cursor += 3;
		int[] enemyHexCounts = HextechHexCountState.NormalizeEnemyCounts(payload.Skip(cursor).Take(3).ToArray());
		cursor += 3;
		int playerRuneRerollLimit = fallback.PlayerRuneRerollLimit;
		int monsterHexRerollLimit = fallback.MonsterHexRerollLimit;
		if (snapshotVersion is RunConfigurationSnapshotVersion or PreviousSingleRarityRunConfigurationSnapshotVersion or PreviousRunConfigurationSnapshotVersion or LegacyRerollRunConfigurationSnapshotVersion)
		{
			playerRuneRerollLimit = HextechRuneConfiguration.ClampRerollLimit(payload[cursor++]);
			monsterHexRerollLimit = HextechRuneConfiguration.ClampRerollLimit(payload[cursor++]);
		}

		ReadRuneRarityFields(
			payload,
			ref cursor,
			snapshotVersion,
			out HextechRarityWeights[] runeWeightsByAct,
			out bool preventConsecutiveSilverRunes,
			out int goldenRerollChancePercent);

		HextechRarityWeights forgeWeights = ReadRarityWeights(payload, ref cursor);
		int forgePrice = payload[cursor++];
		bool randomForgeDirectGrant = fallback.RandomForgeDirectGrant;
		if (snapshotVersion is RunConfigurationSnapshotVersion or PreviousSingleRarityRunConfigurationSnapshotVersion or PreviousRunConfigurationSnapshotVersion or LegacyRerollRunConfigurationSnapshotVersion)
		{
			randomForgeDirectGrant = payload[cursor++] != 0;
		}

		if (!TryReadDisabledMonsterHexIds(payload, ref cursor, out HashSet<string> disabledMonsterHexIds))
		{
			return false;
		}

		if (!HextechStableModelIdListCodec.TryDecode(payload, cursor, MaxDisabledForgeIds, out List<ModelId> disabledForgeIds, out int forgeListNextCursor))
		{
			return false;
		}

		// 尾部可选 int(见 AppendRunConfigurationSnapshot):模组总开关、混沌海克斯概率。
		bool modEnabled = fallback.ModEnabled;
		if (payload.Count > forgeListNextCursor)
		{
			modEnabled = payload[forgeListNextCursor] != 0;
		}

		int chaosRuneChancePercent = payload.Count > forgeListNextCursor + 1
			? payload[forgeListNextCursor + 1]
			: HextechRuneConfiguration.DefaultChaosRuneChancePercent;

		snapshot = HextechRuneConfiguration.NormalizeSnapshot(new HextechRunConfigurationSnapshot(
			playerHexCounts,
			enemyHexCounts,
			playerRuneRerollLimit,
			monsterHexRerollLimit,
			fallback.DisabledPlayerRuneIds,
			disabledMonsterHexIds,
			disabledForgeIds.Select(static id => id.Entry).ToHashSet(StringComparer.Ordinal),
			runeWeightsByAct,
			preventConsecutiveSilverRunes,
			goldenRerollChancePercent,
			forgeWeights,
			forgePrice,
			randomForgeDirectGrant,
			modEnabled,
			chaosRuneChancePercent));
		return true;
	}

	/// <summary>各快照版本的海克斯稀有度段:当前版为分幕三组权重 + 防连续银 + 金色重掷;更早的版本按旧布局读取。</summary>
	private static void ReadRuneRarityFields(
		List<int> payload,
		ref int cursor,
		int snapshotVersion,
		out HextechRarityWeights[] runeWeightsByAct,
		out bool preventConsecutiveSilverRunes,
		out int goldenRerollChancePercent)
	{
		goldenRerollChancePercent = HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent();
		if (snapshotVersion == RunConfigurationSnapshotVersion)
		{
			runeWeightsByAct =
			[
				ReadRarityWeights(payload, ref cursor),
				ReadRarityWeights(payload, ref cursor),
				ReadRarityWeights(payload, ref cursor)
			];
			preventConsecutiveSilverRunes = payload[cursor++] != 0;
			goldenRerollChancePercent = HextechRuneConfiguration.ClampGoldenRerollChancePercent(payload[cursor++]);
		}
		else if (snapshotVersion is PreviousSingleRarityRunConfigurationSnapshotVersion or PreviousRunConfigurationSnapshotVersion)
		{
			HextechRarityWeights singleWeights = ReadRarityWeights(payload, ref cursor);
			runeWeightsByAct = [ singleWeights, singleWeights, singleWeights ];
			preventConsecutiveSilverRunes = payload[cursor++] != 0;
			if (snapshotVersion == PreviousSingleRarityRunConfigurationSnapshotVersion)
			{
				goldenRerollChancePercent = HextechRuneConfiguration.ClampGoldenRerollChancePercent(payload[cursor++]);
			}
		}
		else
		{
			_ = ReadRarityWeights(payload, ref cursor);
			HextechRarityWeights legacyNormalWeights = ReadRarityWeights(payload, ref cursor);
			_ = ReadRarityWeights(payload, ref cursor);
			runeWeightsByAct = [ legacyNormalWeights, legacyNormalWeights, legacyNormalWeights ];
			preventConsecutiveSilverRunes = HextechRuneConfiguration.GetDefaultPreventConsecutiveSilverRunes();
		}
	}

	/// <summary>禁用敌方海克斯列表:计数 + 枚举编号;本版本不认识的编号跳过(对端可能是更新的版本)。</summary>
	private static bool TryReadDisabledMonsterHexIds(List<int> payload, ref int cursor, out HashSet<string> disabledMonsterHexIds)
	{
		disabledMonsterHexIds = [];
		if (payload.Count <= cursor)
		{
			return false;
		}

		int disabledMonsterHexCount = payload[cursor++];
		if (disabledMonsterHexCount < 0
			|| disabledMonsterHexCount > MaxDisabledMonsterHexes
			|| !HasRemaining(payload, cursor, disabledMonsterHexCount))
		{
			return false;
		}

		for (int i = 0; i < disabledMonsterHexCount; i++)
		{
			int value = payload[cursor + i];
			if (Enum.IsDefined(typeof(MonsterHexKind), value))
			{
				disabledMonsterHexIds.Add(((MonsterHexKind)value).ToString());
			}
		}

		cursor += disabledMonsterHexCount;
		return true;
	}

	private static void AppendRarityWeights(List<int> payload, HextechRarityWeights weights)
	{
		payload.Add(HextechRuneConfiguration.ClampRarityWeight(weights.Silver));
		payload.Add(HextechRuneConfiguration.ClampRarityWeight(weights.Gold));
		payload.Add(HextechRuneConfiguration.ClampRarityWeight(weights.Prismatic));
	}

	private static HextechRarityWeights ReadRarityWeights(List<int> payload, ref int cursor)
	{
		HextechRarityWeights weights = new(payload[cursor], payload[cursor + 1], payload[cursor + 2]);
		cursor += 3;
		return weights;
	}
}
