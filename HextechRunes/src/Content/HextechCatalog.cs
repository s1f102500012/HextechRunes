using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Modding;
using static HextechRunes.HextechContentRegistry;

namespace HextechRunes;

internal static partial class HextechCatalog
{
	public readonly record struct RuneSeriesGroup(string LocalizationKey, IReadOnlyList<RelicModel> Relics);

	private readonly record struct CharacterRunePool(string LocalizationKey, IReadOnlyList<Type> RuneTypes);

	// 注册表查询统一经 HextechContentRegistry(文件头 using static),本类不再包一层同名私有属性。
	private static IReadOnlyList<CharacterRunePool> CharacterRunePools =>
	[
		new("IRONCLAD", PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Ironclad]),
		new("SILENT", PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Silent]),
		new("REGENT", PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Regent]),
		new("DEFECT", PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Defect]),
		new("NECROBINDER", PlayerRuneMetadata.TypesByCharacter[PlayerRuneCharacterPool.Necrobinder])
	];

	public static IReadOnlyList<Type> GetAllConfigurableRuneTypes()
	{
		return Enum.GetValues<HextechRarityTier>()
			.SelectMany(GetConfigurablePlayerRuneTypesForRarity)
			.ToArray();
	}

	public static bool IsPlayerRuneTypeConfigurable(Type runeType)
	{
		return PlayerRuneMetadata.IsConfigurable(runeType);
	}

	public static bool IsPlayerRuneTypeVisibleInCollection(Type runeType)
	{
		if (!PlayerRuneMetadata.AllTypes.Contains(runeType))
		{
			return false;
		}

		// 弑神不进任何池子；本机完成弑神后作为纪念出现在图鉴里。
		if (runeType == typeof(GodslayerRune))
		{
			return HextechSignalNoise.HasSlain();
		}

		if (IsPlayerRuneTypeConfigurable(runeType))
		{
			return HextechRuneConfiguration.IsPlayerRuneEnabled(ModelDb.GetId(runeType).Entry);
		}

		return PlayerRuneMetadata.IsVisible(runeType);
	}

	public static IReadOnlyList<Type> GetGenericVisibleRuneTypes()
	{
		PlayerRuneMetadataCatalog metadata = PlayerRuneMetadata;
		return metadata.AllTypes
			.Where(IsPlayerRuneTypeVisibleInCollection)
			.Where(type => !metadata.CharacterSpecificTypes.Contains(type))
			.ToArray();
	}

	// 只决定界面上的来源标签（HEXTECH_POOL.<key>）与配置菜单排序，不参与发放。
	public static string GetPlayerRunePoolKey(RelicModel relic)
	{
		ModelId id = relic.CanonicalId();
		string? externalPoolKey = HextechExternalContentRegistry.GetPlayerRunePoolLabel(id);
		if (externalPoolKey != null)
		{
			return externalPoolKey;
		}

		foreach (CharacterRunePool pool in CharacterRunePools)
		{
			foreach (Type runeType in pool.RuneTypes)
			{
				if (ModelDb.GetId(runeType) == id)
				{
					return pool.LocalizationKey;
				}
			}
		}

		// 额外拓展包沿用内置的"拓展包"标签，免得它为此单独发版；其他外部模组未指定时归为"通用"。
		if (string.Equals(
			HextechExternalContentRegistry.GetAssetModId(id),
			HextechExternalContentRegistry.SponsorPackModId,
			StringComparison.Ordinal))
		{
			return "SPONSOR_PACK";
		}

		return "GENERIC";
	}

	public static IReadOnlyList<Type> GetAllForgeTypes() => AllForgeTypes;

	public static IReadOnlyList<Type> GetAllCustomRelicTypes() => AllCustomRelicTypes;

	public static IReadOnlyList<Type> GetAllCustomCardTypes() => HextechCustomModelRegistry.CustomCardTypes;

	public static IReadOnlyList<Type> GetConfigurablePlayerRuneTypesForRarity(HextechRarityTier rarity)
	{
		return PlayerRuneMetadata.GetConfigurableTypesForRarity(rarity);
	}

	// 配置格式只存 Entry，所以可配置符文的 Entry 必须唯一。校验与结果按注册表版本缓存，外部登记后重建；
	// 冲突时每次调用都重新校验并抛出，不缓存失败。
	public static IReadOnlySet<ModelId> GetConfigurablePlayerRuneIds()
	{
		int version = HextechContentRegistry.Version;
		lock (ModelIdLookupLock)
		{
			if (_configurablePlayerRuneIds == null || _configurablePlayerRuneIdsVersion != version || !_configurablePlayerRuneIdsFinal)
			{
				bool final = HextechModelDbState.IsContentRegistered;
				_configurablePlayerRuneIds = BuildConfigurablePlayerRuneIds();
				_configurablePlayerRuneIdsVersion = version;
				_configurablePlayerRuneIdsFinal = final;
			}

			return _configurablePlayerRuneIds;
		}
	}

	private static IReadOnlySet<ModelId> BuildConfigurablePlayerRuneIds()
	{
		Type[] configurableTypes = GetAllConfigurableRuneTypes().ToArray();
		EnsureUniqueModelIds(configurableTypes, ModelDb.GetId);
		ModelId[] ids = configurableTypes
			.Select(ModelDb.GetId)
			.OrderBy(static id => id.Category, StringComparer.Ordinal)
			.ThenBy(static id => id.Entry, StringComparer.Ordinal)
			.ToArray();
		IGrouping<string, ModelId>? conflictingEntry = ids
			.GroupBy(static id => id.Entry, StringComparer.Ordinal)
			.FirstOrDefault(static group =>
				group.Select(static id => id.Category)
					.Distinct(StringComparer.Ordinal)
					.Skip(1)
					.Any());
		if (conflictingEntry != null)
		{
			string conflictingIds = string.Join(
				", ",
				conflictingEntry.Select(static id => id.ToString()));
			throw new InvalidOperationException(
				$"Configurable player rune ModelIds must have unique Entry values because the existing configuration format stores Entry only: {conflictingIds}");
		}

		return ids.ToHashSet();
	}

	internal static void EnsureConfigurablePlayerRuneIdEntryAvailable(Type runeType)
	{
		EnsureConfigurablePlayerRuneIdEntryAvailable(
			runeType,
			GetAllConfigurableRuneTypes(),
			ModelDb.GetId);
	}

	internal static void EnsureExternalModelIdAvailable(Type modelType)
	{
		IEnumerable<Type> knownModelTypes = EnumerateLoadedAbstractModelTypes()
			.Concat(HextechContentRegistry.AllCustomRelicTypes)
			.Concat(HextechContentRegistry.EventRelicTypes)
			.Concat(HextechCustomModelRegistry.CustomCardTypes)
			.Concat(HextechCustomModelRegistry.AllCustomModifierTypes)
			.Append(modelType);
		EnsureModelIdAvailable(modelType, knownModelTypes, ModelDb.GetId);
	}

	private static IEnumerable<Type> EnumerateLoadedAbstractModelTypes()
	{
		Type abstractModelType = typeof(AbstractModel);
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (assembly.IsDynamic)
			{
				continue;
			}

			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types
					.Where(static type => type != null)
					.Cast<Type>()
					.ToArray();
			}

			foreach (Type type in types)
			{
				if (type.IsClass
					&& !type.IsAbstract
					&& !type.ContainsGenericParameters
					&& abstractModelType.IsAssignableFrom(type))
				{
					yield return type;
				}
			}
		}
	}

	internal static void EnsureConfigurablePlayerRuneIdEntryAvailable(
		Type runeType,
		IEnumerable<Type> existingRuneTypes,
		Func<Type, ModelId> getModelId)
	{
		Type[] candidateTypes = existingRuneTypes
			.Where(type => !HextechModelTypeIdentity.IsSame(type, runeType))
			.Append(runeType)
			.ToArray();
		EnsureUniqueModelIds(candidateTypes, getModelId);

		ModelId incomingId = getModelId(runeType);
		ModelId? conflictingId = candidateTypes
			.Where(type => !HextechModelTypeIdentity.IsSame(type, runeType))
			.Select(getModelId)
			.FirstOrDefault(id =>
				string.Equals(id.Entry, incomingId.Entry, StringComparison.Ordinal)
				&& !string.Equals(id.Category, incomingId.Category, StringComparison.Ordinal));
		if (conflictingId != null)
		{
			throw new InvalidOperationException(
				$"Configurable player rune ModelIds must have unique Entry values because the existing configuration format stores Entry only: {conflictingId}, {incomingId}");
		}
	}

	internal static void EnsureUniqueModelIds(
		IEnumerable<Type> modelTypes,
		Func<Type, ModelId> getModelId)
	{
		Dictionary<ModelId, Type> typesById = new();
		foreach (Type modelType in HextechModelTypeIdentity.Distinct(modelTypes))
		{
			ModelId id = getModelId(modelType);
			if (typesById.TryGetValue(id, out Type? existingType)
				&& !HextechModelTypeIdentity.IsSame(existingType, modelType))
			{
				throw new InvalidOperationException(
					$"Different model types cannot share the same ModelId: "
					+ $"{existingType.FullName} and {modelType.FullName} both resolve to {id}.");
			}

			typesById[id] = modelType;
		}
	}

	internal static void EnsureModelIdAvailable(
		Type modelType,
		IEnumerable<Type> existingModelTypes,
		Func<Type, ModelId> getModelId)
	{
		ModelId incomingId = getModelId(modelType);
		Type? conflictingType = HextechModelTypeIdentity
			.Distinct(existingModelTypes)
			.FirstOrDefault(existingType =>
				!HextechModelTypeIdentity.IsSame(existingType, modelType)
				&& getModelId(existingType) == incomingId);
		if (conflictingType != null)
		{
			throw new InvalidOperationException(
				$"Different model types cannot share the same ModelId: "
				+ $"{conflictingType.FullName} and {modelType.FullName} both resolve to {incomingId}.");
		}
	}

	public static IReadOnlySet<ModelId> GetDefaultDisabledPlayerRuneIds()
	{
		return PlayerRuneMetadata.TypesByFlag[PlayerRuneFlags.Disabled]
			.Where(IsPlayerRuneTypeConfigurable)
			.Select(ModelDb.GetId)
			.ToHashSet();
	}

	// 所有发放路径（选择池、奖励、锻造、宝箱替换）共用的唯一过滤口。
	// 经 HextechRunesInterop 注册的符文可以不继承 HextechRelicBase：它们没有虚方法可覆写，
	// 改由登记时附带的委托表达限制；稀有度不是 Starter 的外部符文不发放，因为原版多处按稀有度
	// 取遗物，只有 Starter 能保证符文不经由自然池以外的原版路径漏出。
	public static bool IsAvailableForPlayer(RelicModel relic, Player player)
	{
		if (relic is HextechRelicBase hextechRelic)
		{
			if (!hextechRelic.IsAvailableForPlayer(player))
			{
				return false;
			}
		}
		else if (IsHextechRelic(relic) && relic.Rarity != RelicRarity.Starter)
		{
			if (HextechRunLogBudget.TryConsume("external-content.non-starter-rune", 12))
			{
				HextechLog.Warn("ExternalContent", $"External player rune {relic.GetType().FullName} has rarity {relic.Rarity}; only Starter runes are granted.");
			}

			return false;
		}

		ModelId id = relic.CanonicalId();
		Func<Player, bool>? availability = HextechExternalContentRegistry.GetPlayerRuneAvailability(id);
		if (availability == null)
		{
			return true;
		}

		// 外部委托抛异常时排除该项并记录日志。委托仍须只依赖同步状态，
		// catch 无法保证读取本机状态的第三方实现会在两端得出相同结果。
		try
		{
			return availability(player);
		}
		catch (Exception ex)
		{
			if (HextechRunLogBudget.TryConsume("external-content.availability-failed", 12))
			{
				HextechLog.Warn("ExternalContent", $"Availability predicate for {relic.GetType().FullName} threw and the rune was excluded: {ex.GetType().Name}: {ex.Message}");
			}

			return false;
		}
	}

	public static bool IsPlayerRuneAllowedInAct(Type runeType, int actIndex)
	{
		return actIndex switch
		{
			0 => !PlayerRuneMetadata.HasFlag(runeType, PlayerRuneFlags.FirstActExcluded),
			2 => IsEndlessModeLoaded() || !PlayerRuneMetadata.HasFlag(runeType, PlayerRuneFlags.ThirdActExcluded),
			_ => true
		};
	}

	private const string EndlessModeModId = "EndlessMode";

	// 只缓存 ModManager 完成初始化之后的结果:模组逐个加载,初始化期间查询会漏掉排在后面的 EndlessMode;
	// 初始化完成后原版不支持运行中加载/卸载模组,结果不再变化。
	private static bool? _endlessModeLoaded;

	/// <summary>
	/// 无尽模式(EndlessMode)是否已加载。使用原版公开 API <see cref="ModManager.GetLoadedMods"/>
	/// 与 <c>Mod.manifest.id</c>(0.107.1–0.111.0 均为 public),不再反射私有成员。
	/// 第 3 幕构建候选池时会对每个候选调用,结果在模组初始化完成后缓存。
	/// </summary>
	internal static bool IsEndlessModeLoaded()
	{
		if (_endlessModeLoaded is bool cached)
		{
			return cached;
		}

		bool loaded = ModManager.GetLoadedMods()
			.Any(static mod => string.Equals(mod.manifest?.id, EndlessModeModId, StringComparison.OrdinalIgnoreCase));
		if (ModManager.State != ModManagerState.None)
		{
			_endlessModeLoaded = loaded;
		}

		return loaded;
	}
}
