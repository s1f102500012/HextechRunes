namespace HextechRunes;

internal static partial class HextechRuneConfiguration
{
	private const string ConfigFileName = "rune_config.json";
	private const int CurrentConfigVersion = 40;
	// v15(0.8.4):一次性强制重置——旧版本配置载入时整体丢弃回默认(含禁用池/数量/权重/重随/价格/总开关)。
	private const int ForceResetBelowConfigVersion = 15;
	private const int HexActCount = 3;
	private const int MinActHexCount = 0;
	private const int MaxActHexCount = 6;
	public const int InfiniteRerollLimit = -1;
	private const int MinFiniteRerollLimit = 0;
	private const int MaxFiniteRerollLimit = 9;
	private const int MinRarityWeight = 0;
	private const int MaxRarityWeight = 999;
	private const int MinRandomForgeShopPrice = 0;
	private const int MaxRandomForgeShopPrice = 9999;
	private const int DefaultRandomForgeShopPrice = 250;
	private const bool DefaultRandomForgeDirectGrant = false;
	private const bool DefaultPreventConsecutiveSilverRunes = true;
	private const int DefaultGoldenRerollChancePercent = 5;
	private const int MinGoldenRerollChancePercent = 0;
	private const int MaxGoldenRerollChancePercent = 100;
	// 混沌海克斯概率:record 默认参数与联机/分享码的缺省值都引用它,因此是 const 且 internal。
	internal const int DefaultChaosRuneChancePercent = 33;
	private const int MinChaosRuneChancePercent = 0;
	private const int MaxChaosRuneChancePercent = 100;
	// 模组总开关默认开启:关闭后本局表现得与原版一致(开局时快照,联机按房主)。
	internal const bool DefaultModEnabled = true;
	private const int DefaultPlayerRuneRerollLimit = 1;
	private const int DefaultMonsterHexRerollLimit = 1;

	private static readonly object SyncRoot = new();
	private static RuneConfig _config = new();
	private static bool _loaded;

	public static void Initialize()
	{
		EnsureLoaded();
	}

	public static bool IsPlayerRuneEnabled(string id)
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			return !_config.DisabledPlayerRuneIds.Contains(id);
		}
	}

	public static IReadOnlySet<string> GetDisabledPlayerRuneIds()
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			return _config.DisabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal);
		}
	}

	public static IReadOnlySet<string> GetDisabledForgeIds()
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			return NormalizeDisabledForgeIds(_config.DisabledForgeIds);
		}
	}

	/// <summary>
	/// 当前配置的独立副本(调用方可以改,不影响内存配置)。内存配置在载入与保存时都已经过
	/// <see cref="NormalizeSnapshot"/>,这里只复制。
	/// </summary>
	public static HextechRunConfigurationSnapshot GetSnapshot()
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			return ReadSnapshot(_config);
		}
	}

	private static HextechRunConfigurationSnapshot ReadSnapshot(RuneConfig config)
	{
		return new HextechRunConfigurationSnapshot(
			config.PlayerHexCountsByAct!.ToArray(),
			config.EnemyHexCountsByAct!.ToArray(),
			config.PlayerRuneRerollLimit,
			config.MonsterHexRerollLimit,
			config.DisabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal),
			config.DisabledMonsterHexIds.ToHashSet(StringComparer.Ordinal),
			config.DisabledForgeIds.ToHashSet(StringComparer.Ordinal),
			ToRarityWeightsByAct(config.RuneRarityWeightsByAct, DefaultRuneRarityWeightsByAct),
			config.PreventConsecutiveSilverRunes,
			config.GoldenRerollChancePercent,
			ToRarityWeights(config.ForgeRarityWeights, DefaultForgeRarityWeights),
			config.RandomForgeShopPrice,
			config.RandomForgeDirectGrant,
			config.ModEnabled,
			config.ChaosRuneChancePercent);
	}

	/// <summary>把已规范化的快照写回内存配置(不落盘)。</summary>
	private static void StoreNormalizedSnapshot(RuneConfig config, HextechRunConfigurationSnapshot normalized)
	{
		config.PlayerHexCountsByAct = normalized.PlayerHexCountsByAct;
		config.EnemyHexCountsByAct = normalized.EnemyHexCountsByAct;
		config.PlayerRuneRerollLimit = normalized.PlayerRuneRerollLimit;
		config.MonsterHexRerollLimit = normalized.MonsterHexRerollLimit;
		config.DisabledPlayerRuneIds = normalized.DisabledPlayerRuneIds;
		config.DisabledMonsterHexIds = normalized.DisabledMonsterHexIds;
		config.DisabledForgeIds = normalized.DisabledForgeIds;
		config.RuneRarityWeightsByAct = FromRarityWeightsByAct(normalized.RuneRarityWeightsByAct);
		config.RuneRarityWeights = null;
		config.PreventConsecutiveSilverRunes = normalized.PreventConsecutiveSilverRunes;
		config.GoldenRerollChancePercent = normalized.GoldenRerollChancePercent;
		config.ChaosRuneChancePercent = normalized.ChaosRuneChancePercent;
		config.NormalRuneRarityWeights = null;
		config.ForgeRarityWeights = FromRarityWeights(normalized.ForgeRarityWeights);
		config.RandomForgeShopPrice = normalized.RandomForgeShopPrice;
		config.RandomForgeDirectGrant = normalized.RandomForgeDirectGrant;
		config.ModEnabled = normalized.ModEnabled;
	}

	internal static HashSet<string> NormalizeDisabledPlayerRuneIds(IEnumerable<string>? ids)
	{
		return HextechPlayerRuneConfigIds.Normalize(ids);
	}

	internal static HashSet<string> NormalizeDisabledMonsterHexIds(IEnumerable<string>? ids)
	{
		HashSet<string> validIds = HextechContentRegistry.MonsterHexMetadata.EnabledKindsByRarity
			.Values
			.SelectMany(static kinds => kinds)
			.Select(static kind => kind.ToString())
			.ToHashSet(StringComparer.Ordinal);
		return NormalizeStringIds(ids, validIds);
	}

	internal static HashSet<string> NormalizeDisabledForgeIds(IEnumerable<string>? ids)
	{
		return NormalizeConfigStringIds(ids);
	}

	public static IReadOnlySet<string> GetDefaultDisabledPlayerRuneIds()
	{
		return HextechCatalog.GetDefaultDisabledPlayerRuneIds()
			.Select(static id => id.Entry)
			.ToHashSet(StringComparer.Ordinal);
	}

	public static IReadOnlySet<string> GetDefaultDisabledMonsterHexIds()
	{
		return new HashSet<string>(StringComparer.Ordinal)
		{
			MonsterHexKind.GetExcited.ToString(),
			MonsterHexKind.ShoulderVaku.ToString()
		};
	}

	public static IReadOnlySet<string> GetDefaultDisabledForgeIds()
	{
		return new HashSet<string>(StringComparer.Ordinal);
	}

	public static void SaveSnapshot(HextechRunConfigurationSnapshot snapshot)
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			_config.ConfigVersion = CurrentConfigVersion;
			StoreNormalizedSnapshot(_config, NormalizeSnapshot(snapshot));
			SaveConfig(_config);
		}
	}

	// 模组总开关的当前(实时)配置值。运行中应优先读「本局冻结快照」,仅在无 run 场景(菜单外/商店初始化兜底)用它。
	public static bool GetModEnabled()
	{
		EnsureLoaded();
		lock (SyncRoot)
		{
			return _config.ModEnabled;
		}
	}

	internal static HashSet<string> NormalizeStringIds(IEnumerable<string>? ids, IReadOnlySet<string> validIds)
	{
		return NormalizeConfigStringIds(ids).Where(validIds.Contains).ToHashSet(StringComparer.Ordinal);
	}

	// 去空白、去重、排序;不按注册表过滤(未知 ID 原样保留)。
	// ToHashSet 自己去重;排序不是多余的:只插入不删除的 HashSet 按插入顺序枚举,
	// 排好序才能让 rune_config.json 的数组顺序与日志输出稳定。
	internal static HashSet<string> NormalizeConfigStringIds(IEnumerable<string>? ids)
	{
		return (ids ?? [])
			.Where(static id => !string.IsNullOrWhiteSpace(id))
			.Select(static id => id.Trim())
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToHashSet(StringComparer.Ordinal);
	}
}
