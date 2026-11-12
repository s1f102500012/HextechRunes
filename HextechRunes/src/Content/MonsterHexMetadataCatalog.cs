namespace HextechRunes;

internal sealed class MonsterHexMetadataCatalog
{
	public MonsterHexMetadataCatalog(IReadOnlyList<MonsterHexRegistration> registrations)
	{
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
	}

	public IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<MonsterHexKind>> EnabledKindsByRarity { get; }

	public IReadOnlyDictionary<MonsterHexKind, HextechRarityTier> RarityByKind { get; }

	public IReadOnlyDictionary<MonsterHexKind, Type> IconRelicTypes { get; }

	public IReadOnlySet<MonsterHexKind> AllKinds { get; }
}
