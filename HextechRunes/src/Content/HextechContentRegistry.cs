namespace HextechRunes;

internal static class HextechContentRegistry
{
	private static readonly object LookupsLock = new();
	private static RegistryLookups? _lookups;
	private static int _lookupsVersion = -1;
	private static bool _lookupsFinal;

	internal static int Version => HextechExternalContentRegistry.Version;

	private static RegistryLookups Lookups
	{
		get
		{
			int version = Version;
			lock (LookupsLock)
			{
				// 外部敌方海克斯的身份取自展示遗物的 ModelId，ModelDb 收录内容之前条目还可能被前置改写（见 HextechModelDbState），
				// 那时建的目录只用这一次；没有外部敌方海克斯时目录只按版本重建，与以前一致。
				if (_lookups == null || _lookupsVersion != version || (!_lookupsFinal && _lookups.HasExternalEnemyHexes))
				{
					_lookupsFinal = HextechModelDbState.IsContentRegistered;
					_lookups = BuildRegistryLookups();
					_lookupsVersion = version;
				}

				return _lookups;
			}
		}
	}

	internal static PlayerRuneMetadataCatalog PlayerRuneMetadata => Lookups.PlayerRuneMetadata;

	internal static MonsterHexMetadataCatalog MonsterHexMetadata => Lookups.MonsterHexMetadata;

	internal static IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<Type>> ForgeTypesByRarity => Lookups.ForgeTypesByRarity;

	internal static IReadOnlyDictionary<Type, HextechRarityTier> ForgeRarityByType => Lookups.ForgeRarityByType;

	internal static IReadOnlyList<Type> AllForgeTypes => Lookups.AllForgeTypes;

	internal static IReadOnlyList<Type> AllCustomRelicTypes => Lookups.AllCustomRelicTypes;

	/// <summary>外部敌方海克斯的展示遗物：与 <see cref="HextechCustomModelRegistry.EnemyHexIconRelicTypes"/> 同口径，进 ModelDb、不进任何玩家获取池。</summary>
	internal static IReadOnlyList<Type> ExternalEnemyHexDisplayRelicTypes => Lookups.ExternalEnemyHexDisplayRelicTypes;

	internal static IReadOnlyList<Type> EventRelicTypes =>
		HextechCustomModelRegistry.EventRelicTypes
			.Concat(HextechExternalContentRegistry.GetEventRelicTypes())
			.ToArray();

	private static RegistryLookups BuildRegistryLookups()
	{
		return new RegistryLookups(
				HextechPlayerRuneRegistry.Registrations
					.Concat(HextechExternalContentRegistry.GetPlayerRuneRegistrations())
					.ToArray(),
				HextechForgeRegistry.Registrations
					.Concat(HextechExternalContentRegistry.GetForgeRegistrations())
					.ToArray(),
				HextechMonsterHexRegistry.Registrations,
				ExternalEnemyHexIdentity.Resolve(
					HextechMonsterHexRegistry.Registrations,
					HextechExternalContentRegistry.GetEnemyHexRegistrations(),
					HextechExternalContentRegistry.ModelIdResolver,
					static message =>
					{
						if (HextechRunLogBudget.TryConsume("external-content.enemy-hex-identity-conflict", 12))
						{
							HextechLog.Warn("ExternalContent", message);
						}
					}));
	}

	// 只做类型层面的派生，不调用 ModelDb：ModelId 的捕获放在 HextechCatalog.ModelIdLookups，两者的构建时机不能合并。
	// 唯一的例外是外部敌方海克斯的身份（ExternalEnemyHexIdentity.Resolve），它本来就由展示遗物的 ModelId 定义，按上面的收录状态重建。
	private sealed class RegistryLookups
	{
		public RegistryLookups(
			IReadOnlyList<PlayerRuneRegistration> runeRegistrations,
			IReadOnlyList<ForgeRegistration> forgeRegistrations,
			IReadOnlyList<MonsterHexRegistration> monsterHexRegistrations,
			IReadOnlyList<ResolvedExternalEnemyHex> externalEnemyHexes)
		{
			PlayerRuneMetadata = new PlayerRuneMetadataCatalog(runeRegistrations);
			MonsterHexMetadata = new MonsterHexMetadataCatalog(monsterHexRegistrations, externalEnemyHexes);
			HasExternalEnemyHexes = externalEnemyHexes.Count > 0;
			ExternalEnemyHexDisplayRelicTypes = externalEnemyHexes
				.Select(static external => external.Registration.DisplayRelicType)
				.ToArray();
			// ToDictionary 同时拒绝重复登记的锻造器类型。
			ForgeRarityByType = forgeRegistrations.ToDictionary(
				static registration => registration.Type,
				static registration => registration.Rarity);
			ForgeTypesByRarity = Enum.GetValues<HextechRarityTier>()
				.ToDictionary(
					static rarity => rarity,
					rarity => (IReadOnlyList<Type>)forgeRegistrations
						.Where(registration => registration.Rarity == rarity)
						.Select(static registration => registration.Type)
						.ToArray());
			AllForgeTypes = Enum.GetValues<HextechRarityTier>()
				.SelectMany(rarity => ForgeTypesByRarity[rarity])
				.ToArray();
			AllCustomRelicTypes = PlayerRuneMetadata.AllTypes
				.Concat(AllForgeTypes)
				.Concat(HextechCustomModelRegistry.ShopOnlyRelicTypes)
				.Concat(HextechCustomModelRegistry.EnemyHexIconRelicTypes)
				.Concat(ExternalEnemyHexDisplayRelicTypes)
				.Distinct()
				.ToArray();
		}

		public bool HasExternalEnemyHexes { get; }
		public IReadOnlyList<Type> ExternalEnemyHexDisplayRelicTypes { get; }

		public PlayerRuneMetadataCatalog PlayerRuneMetadata { get; }
		public MonsterHexMetadataCatalog MonsterHexMetadata { get; }
		public IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<Type>> ForgeTypesByRarity { get; }
		public IReadOnlyDictionary<Type, HextechRarityTier> ForgeRarityByType { get; }
		public IReadOnlyList<Type> AllForgeTypes { get; }
		public IReadOnlyList<Type> AllCustomRelicTypes { get; }
	}
}
