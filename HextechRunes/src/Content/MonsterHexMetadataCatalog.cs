namespace HextechRunes;

internal sealed class MonsterHexMetadataCatalog
{
	public MonsterHexMetadataCatalog(
		IReadOnlyList<MonsterHexRegistration> builtInRegistrations,
		IReadOnlyList<ResolvedExternalEnemyHex>? externalEnemyHexes = null)
	{
		IReadOnlyList<ResolvedExternalEnemyHex> externals = externalEnemyHexes ?? [];
		// 内置顺序不变，外部条目（已按数值排好）接在后面：抽取池、效果分发顺序因此与模组加载顺序无关。
		MonsterHexRegistration[] registrations = builtInRegistrations
			.Concat(externals.Select(static external => new MonsterHexRegistration(
				external.Kind,
				external.Registration.Rarity,
				external.Registration.DisplayRelicType)))
			.ToArray();
		// ToDictionary 同时拒绝重复登记的 kind。
		RarityByKind = registrations.ToDictionary(
			static registration => registration.Kind,
			static registration => registration.Rarity);
		IconRelicTypes = registrations.ToDictionary(
			static registration => registration.Kind,
			static registration => registration.IconRelicType);
		AllKinds = RarityByKind.Keys.ToHashSet();
		EnabledKindsByRarity = Enum.GetValues<HextechRarityTier>()
			.ToDictionary(
				static rarity => rarity,
				rarity => (IReadOnlyList<MonsterHexKind>)registrations
					.Where(registration => registration.Rarity == rarity)
					.Select(static registration => registration.Kind)
					.ToArray());
		ExternalByKind = externals.ToDictionary(static external => external.Kind);
		ConfigIdByKind = builtInRegistrations
			.Select(static registration => (registration.Kind, ConfigId: registration.Kind.ToString()))
			.Concat(externals.Select(static external => (external.Kind, external.ConfigId)))
			.ToDictionary(static pair => pair.Kind, static pair => pair.ConfigId);
		KindByConfigId = ConfigIdByKind.ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);
	}

	public IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<MonsterHexKind>> EnabledKindsByRarity { get; }

	public IReadOnlyDictionary<MonsterHexKind, HextechRarityTier> RarityByKind { get; }

	public IReadOnlyDictionary<MonsterHexKind, Type> IconRelicTypes { get; }

	public IReadOnlySet<MonsterHexKind> AllKinds { get; }

	/// <summary>经外部 API 登记的条目，按 kind 查；内置条目不在其中。</summary>
	public IReadOnlyDictionary<MonsterHexKind, ResolvedExternalEnemyHex> ExternalByKind { get; }

	/// <summary>配置 ID：内置是枚举名，外部是展示遗物的 ModelId.Entry。禁用列表、分享码、控制台与遥测都用它。</summary>
	public IReadOnlyDictionary<MonsterHexKind, string> ConfigIdByKind { get; }

	public IReadOnlyDictionary<string, MonsterHexKind> KindByConfigId { get; }
}
