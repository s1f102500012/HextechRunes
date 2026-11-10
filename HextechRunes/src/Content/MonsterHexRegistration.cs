namespace HextechRunes;

internal readonly record struct MonsterHexRegistration(
	MonsterHexKind Kind,
	HextechRarityTier Rarity,
	Type IconRelicType);
