namespace HextechRunes;

public static class HextechRunesApi
{
	public const string PersistentInnateMarkerSavedPropertyName = "SavedCosplayInnateMarker";
	private const EnemyHexFlags AllKnownEnemyHexFlags = EnemyHexFlags.Disabled;
	private const PlayerRuneFlags AllKnownPlayerRuneFlags =
		PlayerRuneFlags.Disabled
		| PlayerRuneFlags.AttributeConversionExclusive
		| PlayerRuneFlags.FirstActExcluded
		| PlayerRuneFlags.ThirdActExcluded
		| PlayerRuneFlags.SelectionExcluded
		| PlayerRuneFlags.GuaranteedFirstPick;

	/// <summary>
	/// 注册外部玩家符文。必须在模组初始化阶段、共享遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterPlayerRune<TRune>(
		HextechRarityTier rarity,
		PlayerRuneFlags flags = PlayerRuneFlags.None,
		PlayerRuneCharacterPool? characterPool = null,
		int characterOrder = 0,
		string tagKey = "COMPREHENSIVE",
		string? assetModId = null)
		where TRune : HextechRelicBase
	{
		RegisterPlayerRune(typeof(TRune), rarity, flags, characterPool, characterOrder, tagKey, assetModId);
	}

	/// <summary>
	/// 注册外部玩家符文。必须在模组初始化阶段、共享遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterPlayerRune(
		Type runeType,
		HextechRarityTier rarity,
		PlayerRuneFlags flags = PlayerRuneFlags.None,
		PlayerRuneCharacterPool? characterPool = null,
		int characterOrder = 0,
		string tagKey = "COMPREHENSIVE",
		string? assetModId = null)
	{
		RegisterPlayerRuneCore(
			runeType,
			typeof(HextechRelicBase),
			rarity,
			flags,
			characterPool,
			characterOrder,
			tagKey,
			assetModId,
			availability: null);
	}

	// 两个公开入口共用：强类型 API 要求 HextechRelicBase，HextechRunesInterop 为不引用本程序集的
	// 模组放宽到 RelicModel。所有校验先于任何副作用，失败的调用不能留下半登记状态；
	// 本体内置类型在参数校验之后、登记窗口检查与任何副作用之前拒绝（只告警、不抛出）：
	// 它们早已登记，不会进池、不会注入 SavedProperty，也不受登记窗口是否关闭影响。
	internal static void RegisterPlayerRuneCore(
		Type runeType,
		Type requiredBaseType,
		HextechRarityTier rarity,
		PlayerRuneFlags flags,
		PlayerRuneCharacterPool? characterPool,
		int characterOrder,
		string tagKey,
		string? assetModId,
		Func<Player, bool>? availability)
	{
		ValidateConcreteModelType(runeType, requiredBaseType, nameof(runeType), "Player rune");
		ValidateRarity(rarity);
		ValidatePlayerRuneFlags(flags);
		if (characterPool.HasValue && !Enum.IsDefined(characterPool.Value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(characterPool),
				characterPool,
				$"Unknown player rune character pool: {characterPool.Value}.");
		}
		if (string.IsNullOrWhiteSpace(tagKey))
		{
			throw new ArgumentException("Player rune tag key must not be empty.", nameof(tagKey));
		}

		if (HextechExternalContentRegistry.RejectBuiltInType(runeType, "player rune"))
		{
			return;
		}

		PlayerRuneRegistration registration = new(runeType, rarity, flags, characterPool, characterOrder, tagKey);
		HextechSavedPropertyBootstrap.EnsureModelTypeRegistrationAllowed(runeType);
		HextechCatalog.EnsureExternalModelIdAvailable(runeType);
		HextechCatalog.EnsureConfigurablePlayerRuneIdEntryAvailable(runeType);
		HextechModelPoolRegistrar.RegisterPlayerRuneModels([ runeType ]);
		HextechSavedPropertyBootstrap.InjectModelType(runeType);
		HextechExternalContentRegistry.RegisterPlayerRune(registration, assetModId, availability);
	}

	/// <summary>
	/// 注册外部敌方海克斯：<typeparamref name="TDisplayRelic"/> 只作展示载体（标题、图标、relics 表的 <c>&lt;词干&gt;.enemyDescription</c>），
	/// 效果是对每个敌人施加一次 <typeparamref name="TPower"/>，层数按强度档位（第一/二/三幕）取 amountAct1/2/3，不大于 0 的档位不施加。
	/// 必须在模组初始化阶段、共享遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="ArgumentException">类型、品级或标志无效；此时不产生任何登记。</exception>
	/// <exception cref="InvalidOperationException">身份与已有敌方海克斯冲突，或模型池注册窗口已经关闭。</exception>
	public static void RegisterEnemyHex<TDisplayRelic, TPower>(
		HextechRarityTier rarity,
		int amountAct1,
		int amountAct2,
		int amountAct3,
		EnemyHexFlags flags = EnemyHexFlags.None,
		string? assetModId = null,
		Func<IRunState, bool>? isAvailable = null)
		where TDisplayRelic : RelicModel
		where TPower : PowerModel
	{
		RegisterEnemyHex(typeof(TDisplayRelic), typeof(TPower), rarity, amountAct1, amountAct2, amountAct3, flags, assetModId, isAvailable);
	}

	/// <inheritdoc cref="RegisterEnemyHex{TDisplayRelic, TPower}"/>
	public static void RegisterEnemyHex(
		Type displayRelicType,
		Type powerType,
		HextechRarityTier rarity,
		int amountAct1,
		int amountAct2,
		int amountAct3,
		EnemyHexFlags flags = EnemyHexFlags.None,
		string? assetModId = null,
		Func<IRunState, bool>? isAvailable = null)
	{
		RegisterEnemyHexCore(displayRelicType, powerType, rarity, amountAct1, amountAct2, amountAct3, flags, assetModId, isAvailable);
	}

	// 强类型 API 与 HextechRunesInterop 共用。与 RegisterPlayerRuneCore 同一顺序：参数校验 → 拒绝内置类型（只告警）→
	// 身份与 ModelId 冲突检查 → 模型池登记（窗口关闭时原版抛 InvalidOperationException）→ 注册表。
	// 池登记之前的步骤都不产生副作用，失败的调用不会留下半登记状态。
	internal static void RegisterEnemyHexCore(
		Type displayRelicType,
		Type powerType,
		HextechRarityTier rarity,
		int amountAct1,
		int amountAct2,
		int amountAct3,
		EnemyHexFlags flags,
		string? assetModId,
		Func<IRunState, bool>? availability)
	{
		ValidateConcreteModelType(displayRelicType, typeof(RelicModel), nameof(displayRelicType), "Enemy hex display relic");
		ValidateConcreteModelType(powerType, typeof(PowerModel), nameof(powerType), "Enemy hex power");
		ValidateRarity(rarity);
		EnemyHexFlags unknownFlags = flags & ~AllKnownEnemyHexFlags;
		if (unknownFlags != EnemyHexFlags.None)
		{
			throw new ArgumentOutOfRangeException(nameof(flags), flags, $"Unknown enemy hex flag bits: {unknownFlags}.");
		}

		if (HextechExternalContentRegistry.RejectBuiltInType(displayRelicType, "enemy hex"))
		{
			return;
		}

		ExternalEnemyHexIdentity.EnsureAvailable(
			displayRelicType,
			HextechMonsterHexRegistry.Registrations,
			HextechExternalContentRegistry.GetEnemyHexRegistrations(),
			HextechExternalContentRegistry.ModelIdResolver);
		HextechCatalog.EnsureExternalModelIdAvailable(displayRelicType);
		HextechModelPoolRegistrar.RegisterEnemyHexDisplayRelicModels([ displayRelicType ]);
		HextechExternalContentRegistry.RegisterEnemyHex(
			new ExternalEnemyHexRegistration(displayRelicType, powerType, rarity, amountAct1, amountAct2, amountAct3, flags),
			assetModId,
			availability);
	}

	/// <summary>
	/// 注册外部事件遗物。必须在模组初始化阶段、事件遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterEventRelic<TRelic>(string? assetModId = null)
		where TRelic : RelicModel
	{
		RegisterEventRelic(typeof(TRelic), assetModId);
	}

	/// <summary>
	/// 注册外部事件遗物。必须在模组初始化阶段、事件遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterEventRelic(Type relicType, string? assetModId = null)
	{
		ValidateConcreteModelType(relicType, typeof(RelicModel), nameof(relicType), "Event relic");

		HextechSavedPropertyBootstrap.EnsureModelTypeRegistrationAllowed(relicType);
		HextechCatalog.EnsureExternalModelIdAvailable(relicType);
		HextechModelPoolRegistrar.RegisterEventRelicModels([ relicType ]);
		HextechSavedPropertyBootstrap.InjectModelType(relicType);
		HextechExternalContentRegistry.RegisterEventRelic(relicType, assetModId);
	}

	/// <summary>
	/// 注册外部锻造。必须在模组初始化阶段、共享遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterForge<TForge>(HextechRarityTier rarity, string? assetModId = null)
		where TForge : HextechForgeBase
	{
		RegisterForge(typeof(TForge), rarity, assetModId);
	}

	/// <summary>
	/// 注册外部锻造。必须在模组初始化阶段、共享遗物池首次枚举前调用。
	/// </summary>
	/// <exception cref="InvalidOperationException">模型池或 SavedProperty 注册窗口已经关闭。</exception>
	public static void RegisterForge(Type forgeType, HextechRarityTier rarity, string? assetModId = null)
	{
		ValidateConcreteModelType(forgeType, typeof(HextechForgeBase), nameof(forgeType), "Forge");
		ValidateRarity(rarity);

		if (HextechExternalContentRegistry.RejectBuiltInType(forgeType, "forge"))
		{
			return;
		}

		HextechSavedPropertyBootstrap.EnsureModelTypeRegistrationAllowed(forgeType);
		HextechCatalog.EnsureExternalModelIdAvailable(forgeType);
		HextechModelPoolRegistrar.RegisterForgeModels([ forgeType ]);
		HextechSavedPropertyBootstrap.InjectModelType(forgeType);
		HextechExternalContentRegistry.RegisterForge(new ForgeRegistration(forgeType, rarity), assetModId);
	}

	public static Task ObtainRandomForges(
		Player player,
		HextechRarityTier rarity,
		int count,
		Func<Type, bool> forgeTypePredicate,
		string source)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(forgeTypePredicate);
		if (string.IsNullOrWhiteSpace(source))
		{
			throw new ArgumentException("Random forge source must not be empty.", nameof(source));
		}

		return HextechForgeGrantHelper.ObtainRandomForges(player, rarity, count, forgeTypePredicate, source);
	}

	public static Task<RelicModel?> SelectRelicOption(
		Player player,
		IReadOnlyList<RelicModel> options,
		string context,
		bool syncMultiplayerChoice = true)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(context))
		{
			throw new ArgumentException("Relic option selection context must not be empty.", nameof(context));
		}
		if (options.Count > HextechStableModelIdListCodec.MaxCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options),
				options.Count,
				$"Relic option count must not exceed {HextechStableModelIdListCodec.MaxCount}.");
		}

		return HextechRelicOptionSelectionCoordinator.SelectRelicOption(player, options, context, syncMultiplayerChoice);
	}

	/// <summary>
	/// 显式登记外部 SavedProperty 载体。必须在模型初始化窗口内调用；视觉资源注册不会隐式执行此操作。
	/// </summary>
	/// <exception cref="InvalidOperationException">官方序列化缓存已初始化，且目标载体未被缓存。</exception>
	public static void RegisterSavedPropertyCarrier<TModel>()
		where TModel : AbstractModel
	{
		RegisterSavedPropertyCarrier(typeof(TModel));
	}

	/// <summary>
	/// 显式登记外部 SavedProperty 载体。必须在模型初始化窗口内调用；视觉资源注册不会隐式执行此操作。
	/// </summary>
	/// <exception cref="InvalidOperationException">官方序列化缓存已初始化，且目标载体未被缓存。</exception>
	public static void RegisterSavedPropertyCarrier(Type modelType)
	{
		ValidateConcreteModelType(modelType, typeof(AbstractModel), nameof(modelType), "SavedProperty carrier");

		HextechSavedPropertyBootstrap.InjectModelType(modelType);
	}

	/// <summary>
	/// 仅注册外部附魔图标；若附魔含 SavedProperty，调用方还必须在初始化窗口内显式登记载体。
	/// </summary>
	public static void RegisterEnchantmentIcon<TEnchantment>(string iconPath)
		where TEnchantment : EnchantmentModel
	{
		RegisterEnchantmentIcon(typeof(TEnchantment), iconPath);
	}

	/// <summary>
	/// 仅注册外部附魔图标；若附魔含 SavedProperty，调用方还必须在初始化窗口内显式登记载体。
	/// </summary>
	public static void RegisterEnchantmentIcon(Type enchantmentType, string iconPath)
	{
		ValidateConcreteModelType(enchantmentType, typeof(EnchantmentModel), nameof(enchantmentType), "Enchantment");
		if (string.IsNullOrWhiteSpace(iconPath))
		{
			throw new ArgumentException("Enchantment icon path must not be empty.", nameof(iconPath));
		}

		HextechExternalContentRegistry.RegisterEnchantmentIcon(enchantmentType, iconPath);
	}

	public static void TrackPersistentInnate(CardModel? card)
	{
		KeywordPersistenceTrackers.CosplayInnate.Track(card);
	}

	public static bool IsPersistentInnateTracked(CardModel? card)
	{
		return KeywordPersistenceTrackers.CosplayInnate.IsTracked(card);
	}

	public static void RestorePersistentInnate(CardModel card)
	{
		KeywordPersistenceTrackers.CosplayInnate.Restore(card);
	}

	internal static void ValidateConcreteModelType(
		Type modelType,
		Type requiredBaseType,
		string parameterName,
		string label)
	{
		ArgumentNullException.ThrowIfNull(modelType, parameterName);
		if (modelType.IsAbstract
			|| modelType.ContainsGenericParameters
			|| !requiredBaseType.IsAssignableFrom(modelType))
		{
			throw new ArgumentException(
				$"{label} type must be a concrete, closed {requiredBaseType.Name}: {modelType.FullName ?? modelType.Name}",
				parameterName);
		}
	}

	private static void ValidateRarity(HextechRarityTier rarity)
	{
		if (!Enum.IsDefined(rarity))
		{
			throw new ArgumentOutOfRangeException(nameof(rarity), rarity, $"Unknown Hextech rarity tier: {rarity}.");
		}
	}

	private static void ValidatePlayerRuneFlags(PlayerRuneFlags flags)
	{
		PlayerRuneFlags unknownFlags = flags & ~AllKnownPlayerRuneFlags;
		if (unknownFlags != PlayerRuneFlags.None)
		{
			throw new ArgumentOutOfRangeException(
				nameof(flags),
				flags,
				$"Unknown player rune flag bits: {unknownFlags}.");
		}
	}

	// ---- 选择界面表现层 ----

	/// <summary>
	/// 登记外部表现层，接管海克斯三选一的界面（见 <see cref="IHextechSelectionPresenter"/>）。最多一个，
	/// 之后的登记只记 Warn。建议在模组初始化时调用；本体只在打开选择界面时读取它。
	/// </summary>
	public static void RegisterSelectionPresenter(IHextechSelectionPresenter presenter) =>
		HextechSelectionPresentation.Register(presenter);

	// ---- 随机锻造器售价修正 / 海克斯归属判断 / 稳定哈希(拓展包等硬依赖模组使用) ----

	private static readonly object ForgeShopPriceModifiersGate = new();

	// 初始化期登记、运行期只读;与 HextechExternalContentRegistry 的外部登记表同一形态。
	private static readonly List<Func<RunState, int, int>> ForgeShopPriceModifiers = [];

	/// <summary>
	/// 登记"随机锻造器"商店售价修正。每次算价时按登记顺序依次调用 <c>modifier(runState, currentPrice)</c>,
	/// 返回值作为新的价格传给下一个修正器;修正器应当只读同步状态(两端算价必须一致),并自行决定下限。
	/// 必须在模组初始化阶段调用。某个修正器抛异常时跳过它并记 Warn,不影响其他修正器与原价。
	/// </summary>
	public static void RegisterForgeShopPriceModifier(Func<RunState, int, int> modifier)
	{
		ArgumentNullException.ThrowIfNull(modifier);
		lock (ForgeShopPriceModifiersGate)
		{
			ForgeShopPriceModifiers.Add(modifier);
		}
	}

	/// <summary>
	/// 本体算价入口(HextechForgeShopPriceHelper.GetRandomForgeShopPriceFor)在得到基础价后调用。
	/// <paramref name="runState"/> 为空时(商店算价时 shopRelic.Owner 可能为空)回退到当前对局;
	/// 没有登记任何修正器时原样返回,不触碰对局状态。
	/// </summary>
	internal static int ApplyForgeShopPriceModifiers(RunState? runState, int price)
	{
		Func<RunState, int, int>[] modifiers;
		lock (ForgeShopPriceModifiersGate)
		{
			if (ForgeShopPriceModifiers.Count == 0)
			{
				return price;
			}

			modifiers = ForgeShopPriceModifiers.ToArray();
		}

		RunState? state = runState ?? RunManager.Instance?.DebugOnlyGetState();
		if (state == null)
		{
			return price;
		}

		foreach (Func<RunState, int, int> modifier in modifiers)
		{
			try
			{
				price = modifier(state, price);
			}
			catch (Exception ex)
			{
				// 真实边界:修正器来自外部模组。
				if (HextechRunLogBudget.TryConsume("api.forge-shop-price-modifier", 3))
				{
					HextechLog.Warn(
						"ExternalContent",
						$"Forge shop price modifier {modifier.Method.DeclaringType?.FullName}.{modifier.Method.Name} threw; skipped: {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		return price;
	}

	/// <summary>
	/// 该遗物是否属于海克斯注册表管理的内容(玩家海克斯、锻造器、商店锻造器、敌方海克斯展示遗物),
	/// 包括其他模组经本 API 或 <c>HextechRunesInterop</c> 登记的符文;不要用 <c>is HextechRelicBase</c> 代替。
	/// </summary>
	public static bool IsHextechRelic(RelicModel? relic)
	{
		return HextechCatalog.IsHextechCustomRelic(relic);
	}

	/// <summary>
	/// 让 Steam 为该创意工坊物品统计游玩时长与会话数;在模组入口调用一次即可,重复调用会被忽略。
	/// Steam 未初始化或调用失败时只记日志,不抛异常。
	/// </summary>
	public static void StartWorkshopPlaytimeTracking(ulong publishedFileId)
	{
		HextechWorkshopPlaytime.Start(publishedFileId);
	}

	/// <summary>
	/// 运行种子稳定随机:在 [0, <paramref name="count"/>) 中取一个下标,不消耗任何共享 RNG。
	/// 输入为本局种子、当前幕、当前总层数与盐(按顺序以 "|" 连接),两端输入一致就得到同一结果,可在联机两端对称执行。
	/// 同一层内重复抽取须在盐里放入区分量(序号、玩家等)。算法与本体 HextechStableRandom 相同且保持不变。
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> 不大于 0。</exception>
	public static int StableIndex(RunState runState, int count, params string?[] saltParts)
	{
		ArgumentNullException.ThrowIfNull(runState);
		return HextechStableRandom.Index(runState, count, saltParts);
	}
}
