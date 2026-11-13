using static HextechRunes.HextechContentRegistry;

namespace HextechRunes;

internal static partial class HextechCatalog
{
	private static readonly object ModelIdLookupLock = new();
	private static ModelIdLookupCache? _modelIdLookupCache;
	private static int _modelIdLookupCacheVersion = -1;
	// 在 ModelDb 收录内容之前建的缓存可能含尚未定型的外部 ModelId（见 HextechModelDbState），下次访问重建。
	private static bool _modelIdLookupCacheFinal;
	private static IReadOnlySet<ModelId>? _configurablePlayerRuneIds;
	private static int _configurablePlayerRuneIdsVersion = -1;
	private static bool _configurablePlayerRuneIdsFinal;

	public static bool IsHextechRelic(RelicModel? relic)
	{
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.RuneIds.Contains(id);
	}

	public static bool IsHextechForgeRelic(RelicModel? relic)
	{
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.ForgeIds.Contains(id);
	}

	public static bool IsHextechShopRelic(RelicModel? relic)
	{
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.ShopOnlyRelicIds.Contains(id);
	}

	public static bool IsHextechEnemyHexIconRelic(RelicModel? relic)
	{
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.EnemyHexIconRelicIds.Contains(id);
	}

	public static bool IsHextechCustomRelic(RelicModel? relic)
	{
		return IsHextechRelic(relic) || IsHextechForgeRelic(relic) || IsHextechShopRelic(relic) || IsHextechEnemyHexIconRelic(relic);
	}

	/// <summary>
	/// 本模组注册表管理的全部遗物:<see cref="IsHextechCustomRelic"/> 的四类,再加欧洛巴斯强化遗物与经接口登记的事件遗物。
	/// 只读查询,供界面把这些遗物与原版初始遗物区分显示;不参与任何获取池或战斗判定。
	/// </summary>
	public static bool IsHextechManagedRelic(RelicModel? relic)
	{
		return relic != null && IsHextechManagedRelicId(relic.CanonicalId());
	}

	/// <inheritdoc cref="IsHextechManagedRelic"/>
	internal static bool IsHextechManagedRelicId(ModelId id)
	{
		ModelIdLookupCache lookups = ModelIdLookups;
		if (lookups.RuneIds.Contains(id)
			|| lookups.ForgeIds.Contains(id)
			|| lookups.ShopOnlyRelicIds.Contains(id)
			|| lookups.EnemyHexIconRelicIds.Contains(id))
		{
			return true;
		}

		// 事件遗物只有个位数,按类型现查,不进 ModelId 缓存。
		foreach (Type eventRelicType in HextechContentRegistry.EventRelicTypes)
		{
			if (ModelDb.GetId(eventRelicType) == id)
			{
				return true;
			}
		}

		return false;
	}

	public static bool TryGetPlayerRuneRarity(RelicModel? relic, out HextechRarityTier rarity)
	{
		rarity = default;
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.PlayerRuneRarityById.TryGetValue(id, out rarity);
	}

	/// <summary>按规范 ModelId 查已登记玩家符文(内置 + 外部 API)的稀有度;不是玩家符文时返回 false。</summary>
	internal static bool TryGetPlayerRuneRarityById(ModelId id, out HextechRarityTier rarity)
	{
		return ModelIdLookups.PlayerRuneRarityById.TryGetValue(id, out rarity);
	}

	public static bool TryGetForgeRarity(RelicModel? relic, out HextechRarityTier rarity)
	{
		rarity = default;
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalId();
		return ModelIdLookups.ForgeRarityById.TryGetValue(id, out rarity);
	}

	public static string GetPlayerRuneTagKey(RelicModel relic)
	{
		ModelId id = relic.CanonicalId();
		return ModelIdLookups.PlayerRuneTagKeyById.TryGetValue(id, out string? tagKey)
			? tagKey
			: HextechPlayerRuneRegistry.DefaultTagKey;
	}

	public static IReadOnlySet<ModelId> GetMutuallyExclusivePlayerRuneIds(IEnumerable<ModelId> ownedIds)
	{
		HashSet<ModelId> ownedSet = ownedIds.ToHashSet();
		if (!ownedSet.Any(IsAttributeConversionExclusiveRuneId))
		{
			return new HashSet<ModelId>();
		}

		HashSet<ModelId> blockedIds = new();
		foreach (Type runeType in PlayerRuneMetadata.TypesByFlag[PlayerRuneFlags.AttributeConversionExclusive])
		{
			ModelId candidateId = ModelDb.GetId(runeType);
			if (!ownedSet.Contains(candidateId))
			{
				blockedIds.Add(candidateId);
			}
		}

		return blockedIds;
	}

	private static bool IsAttributeConversionExclusiveRuneId(ModelId id)
	{
		return ModelIdLookups.AttributeConversionExclusiveRuneIds.Contains(id);
	}

	private static ModelIdLookupCache ModelIdLookups
	{
		get
		{
			int version = HextechContentRegistry.Version;
			lock (ModelIdLookupLock)
			{
				if (_modelIdLookupCache == null || _modelIdLookupCacheVersion != version || !_modelIdLookupCacheFinal)
				{
					_modelIdLookupCacheFinal = HextechModelDbState.IsContentRegistered;
					_modelIdLookupCache = BuildModelIdLookupCache();
					_modelIdLookupCacheVersion = version;
				}

				return _modelIdLookupCache;
			}
		}
	}

	private static ModelIdLookupCache BuildModelIdLookupCache()
	{
		return new ModelIdLookupCache(
			ToModelIdSet(PlayerRuneMetadata.AllTypes),
			ToModelIdSet(AllForgeTypes),
			ToModelIdSet(HextechCustomModelRegistry.ShopOnlyRelicTypes),
			ToModelIdSet(HextechCustomModelRegistry.EnemyHexIconRelicTypes),
			BuildPlayerRuneRarityById(),
			BuildForgeRarityById(),
			BuildPlayerRuneTagKeyById(),
			ToModelIdSet(PlayerRuneMetadata.TypesByFlag[PlayerRuneFlags.AttributeConversionExclusive]));
	}

	private static IReadOnlySet<ModelId> ToModelIdSet(IEnumerable<Type> modelTypes)
	{
		return modelTypes.Select(ModelDb.GetId).ToHashSet();
	}

	private static IReadOnlyDictionary<ModelId, HextechRarityTier> BuildPlayerRuneRarityById()
	{
		Dictionary<ModelId, HextechRarityTier> byId = new();
		foreach (PlayerRuneRegistration registration in PlayerRuneMetadata.Registrations)
		{
			byId[ModelDb.GetId(registration.Type)] = registration.Rarity;
		}

		return byId;
	}

	private static IReadOnlyDictionary<ModelId, HextechRarityTier> BuildForgeRarityById()
	{
		Dictionary<ModelId, HextechRarityTier> byId = new();
		foreach ((Type forgeType, HextechRarityTier rarity) in ForgeRarityByType)
		{
			byId[ModelDb.GetId(forgeType)] = rarity;
		}

		return byId;
	}

	private static IReadOnlyDictionary<ModelId, string> BuildPlayerRuneTagKeyById()
	{
		Dictionary<ModelId, string> byId = new();
		foreach (PlayerRuneRegistration registration in PlayerRuneMetadata.Registrations)
		{
			byId[ModelDb.GetId(registration.Type)] = registration.TagKey;
		}

		return byId;
	}

	private sealed record ModelIdLookupCache(
		IReadOnlySet<ModelId> RuneIds,
		IReadOnlySet<ModelId> ForgeIds,
		IReadOnlySet<ModelId> ShopOnlyRelicIds,
		IReadOnlySet<ModelId> EnemyHexIconRelicIds,
		IReadOnlyDictionary<ModelId, HextechRarityTier> PlayerRuneRarityById,
		IReadOnlyDictionary<ModelId, HextechRarityTier> ForgeRarityById,
		IReadOnlyDictionary<ModelId, string> PlayerRuneTagKeyById,
		IReadOnlySet<ModelId> AttributeConversionExclusiveRuneIds);
}
