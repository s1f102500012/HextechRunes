namespace HextechRunes;

internal static partial class HextechRuneConfiguration
{
	private static readonly int[] DefaultPlayerHexCountsByAct = [ 1, 1, 1 ];
	private static readonly int[] DefaultEnemyHexCountsByAct = [ 1, 2, 3 ];
	private static readonly HextechRarityWeights DefaultRuneRarityWeights = new(1, 1, 1);
	private static readonly HextechRarityWeights[] DefaultRuneRarityWeightsByAct =
	[
		DefaultRuneRarityWeights,
		DefaultRuneRarityWeights,
		DefaultRuneRarityWeights
	];
	private static readonly HextechRarityWeights DefaultForgeRarityWeights = new(65, 25, 10);

	public static int[] GetDefaultPlayerHexCountsByAct()
	{
		return NormalizePlayerHexCounts(null);
	}

	public static int[] GetDefaultEnemyHexCountsByAct()
	{
		return NormalizeEnemyHexCounts(null);
	}

	public static int ClampActHexCount(int count)
	{
		return Math.Clamp(count, MinActHexCount, MaxActHexCount);
	}

	public static int ClampRerollLimit(int limit)
	{
		return limit == InfiniteRerollLimit
			? InfiniteRerollLimit
			: Math.Clamp(limit, MinFiniteRerollLimit, MaxFiniteRerollLimit);
	}

	public static int StepRerollLimit(int current, int delta)
	{
		current = ClampRerollLimit(current);
		if (delta > 0)
		{
			return current == InfiniteRerollLimit || current >= MaxFiniteRerollLimit
				? InfiniteRerollLimit
				: current + 1;
		}

		if (delta < 0)
		{
			return current == InfiniteRerollLimit
				? MaxFiniteRerollLimit
				: Math.Max(MinFiniteRerollLimit, current - 1);
		}

		return current;
	}

	public static int GetDefaultMonsterHexRerollLimit()
	{
		return DefaultMonsterHexRerollLimit;
	}

	public static int ClampRarityWeight(int weight)
	{
		return Math.Clamp(weight, MinRarityWeight, MaxRarityWeight);
	}

	public static int ClampRandomForgeShopPrice(int price)
	{
		return Math.Clamp(price, MinRandomForgeShopPrice, MaxRandomForgeShopPrice);
	}

	public static HextechRarityWeights GetDefaultRuneRarityWeights()
	{
		return DefaultRuneRarityWeights;
	}

	public static HextechRarityWeights[] GetDefaultRuneRarityWeightsByAct()
	{
		return DefaultRuneRarityWeightsByAct.ToArray();
	}

	public static bool GetDefaultPreventConsecutiveSilverRunes()
	{
		return DefaultPreventConsecutiveSilverRunes;
	}

	public static int GetDefaultGoldenRerollChancePercent()
	{
		return DefaultGoldenRerollChancePercent;
	}

	public static int ClampGoldenRerollChancePercent(int percent)
	{
		return Math.Clamp(percent, MinGoldenRerollChancePercent, MaxGoldenRerollChancePercent);
	}

	public static int ClampChaosRuneChancePercent(int percent)
	{
		return Math.Clamp(percent, MinChaosRuneChancePercent, MaxChaosRuneChancePercent);
	}

	public static HextechRarityWeights GetDefaultForgeRarityWeights()
	{
		return DefaultForgeRarityWeights;
	}

	public static int GetDefaultRandomForgeShopPrice()
	{
		return DefaultRandomForgeShopPrice;
	}

	internal static HextechRunConfigurationSnapshot GetDefaultSnapshot()
	{
		return NormalizeSnapshot(new HextechRunConfigurationSnapshot(
			DefaultPlayerHexCountsByAct,
			DefaultEnemyHexCountsByAct,
			DefaultPlayerRuneRerollLimit,
			DefaultMonsterHexRerollLimit,
			GetDefaultDisabledPlayerRuneIds().ToHashSet(StringComparer.Ordinal),
			GetDefaultDisabledMonsterHexIds().ToHashSet(StringComparer.Ordinal),
			GetDefaultDisabledForgeIds().ToHashSet(StringComparer.Ordinal),
			DefaultRuneRarityWeightsByAct,
			DefaultPreventConsecutiveSilverRunes,
			DefaultGoldenRerollChancePercent,
			DefaultForgeRarityWeights,
			DefaultRandomForgeShopPrice,
			DefaultRandomForgeDirectGrant,
			DefaultModEnabled,
			DefaultChaosRuneChancePercent));
	}

	internal static HextechRunConfigurationSnapshot NormalizeSnapshot(HextechRunConfigurationSnapshot snapshot)
	{
		return new HextechRunConfigurationSnapshot(
			NormalizePlayerHexCounts(snapshot.PlayerHexCountsByAct),
			NormalizeEnemyHexCounts(snapshot.EnemyHexCountsByAct),
			ClampRerollLimit(snapshot.PlayerRuneRerollLimit),
			ClampRerollLimit(snapshot.MonsterHexRerollLimit),
			NormalizeDisabledPlayerRuneIds(snapshot.DisabledPlayerRuneIds),
			NormalizeDisabledMonsterHexIds(snapshot.DisabledMonsterHexIds),
			NormalizeDisabledForgeIds(snapshot.DisabledForgeIds),
			NormalizeRarityWeightsByAct(snapshot.RuneRarityWeightsByAct, DefaultRuneRarityWeightsByAct),
			snapshot.PreventConsecutiveSilverRunes,
			ClampGoldenRerollChancePercent(snapshot.GoldenRerollChancePercent),
			NormalizeRarityWeights(snapshot.ForgeRarityWeights, DefaultForgeRarityWeights),
			ClampRandomForgeShopPrice(snapshot.RandomForgeShopPrice),
			snapshot.RandomForgeDirectGrant,
			snapshot.ModEnabled,
			ClampChaosRuneChancePercent(snapshot.ChaosRuneChancePercent),
			snapshot.GodslayerTrial);
	}

	internal static HextechRarityWeights NormalizeRarityWeights(HextechRarityWeights weights, HextechRarityWeights fallback)
	{
		HextechRarityWeights normalized = new(
			ClampRarityWeight(weights.Silver),
			ClampRarityWeight(weights.Gold),
			ClampRarityWeight(weights.Prismatic));
		return normalized.Total > 0 ? normalized : fallback;
	}

	internal static HextechRarityWeights[] NormalizeRarityWeightsByAct(
		IReadOnlyList<HextechRarityWeights>? weightsByAct,
		IReadOnlyList<HextechRarityWeights> fallbackByAct)
	{
		HextechRarityWeights[] normalized = new HextechRarityWeights[HexActCount];
		for (int actIndex = 0; actIndex < normalized.Length; actIndex++)
		{
			HextechRarityWeights fallback = fallbackByAct[Math.Min(actIndex, fallbackByAct.Count - 1)];
			HextechRarityWeights weights = weightsByAct != null && actIndex < weightsByAct.Count
				? weightsByAct[actIndex]
				: fallback;
			normalized[actIndex] = NormalizeRarityWeights(weights, fallback);
		}

		return normalized;
	}

	internal static int[] NormalizePlayerHexCounts(IReadOnlyList<int>? counts)
	{
		return NormalizeActHexCounts(counts, DefaultPlayerHexCountsByAct);
	}

	private static int[] NormalizeEnemyHexCounts(IReadOnlyList<int>? counts)
	{
		return NormalizeActHexCounts(counts, DefaultEnemyHexCountsByAct);
	}

	private static int[] NormalizeActHexCounts(IReadOnlyList<int>? counts, IReadOnlyList<int> defaults)
	{
		int[] normalized = defaults.ToArray();
		if (counts == null)
		{
			return normalized;
		}

		for (int i = 0; i < Math.Min(HexActCount, counts.Count); i++)
		{
			normalized[i] = ClampActHexCount(counts[i]);
		}

		return normalized;
	}
}
