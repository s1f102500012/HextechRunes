namespace HextechRunes;

internal static class HextechMonsterHexRoller
{
	public static IReadOnlyList<MonsterHexKind> BuildActPool(
		HextechRarityTier rarity,
		IEnumerable<MonsterHexKind>? knownHexes,
		IEnumerable<MonsterHexKind>? extraExcludedHexes = null,
		IReadOnlySet<string>? disabledMonsterHexIds = null)
	{
		IReadOnlyList<MonsterHexKind> rarityPool = GetConfiguredRarityPool(rarity, disabledMonsterHexIds);
		HashSet<MonsterHexKind> excluded = ToSet(knownHexes);
		if (extraExcludedHexes != null)
		{
			excluded.UnionWith(extraExcludedHexes);
		}

		List<MonsterHexKind> pool = rarityPool
			.Where(kind => !excluded.Contains(kind))
			.ToList();
		return pool.Count > 0 ? pool : rarityPool.ToArray();
	}

	public static IReadOnlyList<MonsterHexKind> ResolveNewMonsterHexes(
		int newEnemyHexCount,
		IEnumerable<MonsterHexKind> previousHexes,
		MonsterHexKind? primaryMonsterHex,
		Func<IReadOnlySet<MonsterHexKind>, int, MonsterHexKind?> chooseExtraHex)
	{
		if (newEnemyHexCount <= 0)
		{
			return [];
		}

		List<MonsterHexKind> resolvedNewHexes = [];
		HashSet<MonsterHexKind> seen = ToSet(previousHexes);

		if (primaryMonsterHex.HasValue && seen.Add(primaryMonsterHex.Value))
		{
			resolvedNewHexes.Add(primaryMonsterHex.Value);
		}

		for (int ordinal = 1; resolvedNewHexes.Count < newEnemyHexCount; ordinal++)
		{
			MonsterHexKind? extraHex = chooseExtraHex(seen, ordinal);
			if (!extraHex.HasValue || !seen.Add(extraHex.Value))
			{
				break;
			}

			resolvedNewHexes.Add(extraHex.Value);
		}

		return resolvedNewHexes;
	}

	public static IReadOnlyList<MonsterHexKind> CombineActiveHexes(
		IEnumerable<MonsterHexKind> previousHexes,
		IEnumerable<MonsterHexKind> newHexes)
	{
		// Distinct 按首次出现的顺序产出,结果顺序与输入一致。
		return previousHexes.Concat(newHexes).Distinct().ToList();
	}

	public static IReadOnlyList<MonsterHexKind> BuildRerollPool(
		HextechRarityTier rarity,
		IEnumerable<MonsterHexKind> knownHexes,
		MonsterHexKind? currentHex,
		IReadOnlySet<ModelId> excludedIconRelicIds,
		Func<MonsterHexKind, ModelId> getIconRelicId,
		IReadOnlySet<string>? disabledMonsterHexIds = null)
	{
		IReadOnlyList<MonsterHexKind> rarityPool = GetConfiguredRarityPool(rarity, disabledMonsterHexIds);
		HashSet<MonsterHexKind> alreadyChosen = knownHexes
			.Where(kind => kind != currentHex)
			.ToHashSet();

		List<MonsterHexKind> pool = rarityPool
			.Where(kind => kind != currentHex)
			.Where(kind => !alreadyChosen.Contains(kind))
			.Where(kind => !excludedIconRelicIds.Contains(getIconRelicId(kind)))
			.ToList();
		if (pool.Count > 0)
		{
			return pool;
		}

		pool = rarityPool
			.Where(kind => kind != currentHex)
			.Where(kind => !alreadyChosen.Contains(kind))
			.ToList();
		if (pool.Count > 0)
		{
			return pool;
		}

		pool = rarityPool
			.Where(kind => kind != currentHex)
			.ToList();
		return pool.Count > 0 ? pool : rarityPool.ToArray();
	}

	/// <summary>本稀有度下本局可抽到的敌方海克斯:去掉配置禁用项。抽取、重掷与远端校验共用。</summary>
	public static IReadOnlyList<MonsterHexKind> GetConfiguredRarityPool(HextechRarityTier rarity, IReadOnlySet<string>? disabledMonsterHexIds)
	{
		return ApplyConfig(MonsterHexCatalog.GetMonsterHexesForRarity(rarity), disabledMonsterHexIds);
	}

	private static HashSet<MonsterHexKind> ToSet(IEnumerable<MonsterHexKind>? hexes)
	{
		return hexes?.ToHashSet() ?? [];
	}

	private static IReadOnlyList<MonsterHexKind> ApplyConfig(
		IReadOnlyList<MonsterHexKind> rarityPool,
		IReadOnlySet<string>? disabledMonsterHexIds)
	{
		if (disabledMonsterHexIds == null || disabledMonsterHexIds.Count == 0)
		{
			return rarityPool;
		}

		return rarityPool
			.Where(kind => !disabledMonsterHexIds.Contains(kind.ToString()))
			.ToList();
	}
}
