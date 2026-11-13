namespace HextechRunes;

[Flags]
public enum PlayerRuneFlags
{
	None = 0,
	Disabled = 1,
	AttributeConversionExclusive = 2,
	FirstActExcluded = 4,
	ThirdActExcluded = 8,
	SelectionExcluded = 16,
	// 每幕首次三选一保底出现一个（见 HextechRunePoolBuilder.ApplyGuaranteedFirstPick）。
	GuaranteedFirstPick = 32,
	// 只能由特定流程发放：不进抽选池、设置界面列表与图鉴。仅供本体内部使用，不对外部注册开放。
	Hidden = 64
}

public enum PlayerRuneCharacterPool
{
	Ironclad,
	Silent,
	Regent,
	Defect,
	Necrobinder,
	// 原版角色「愚者」只存在于 0.113.0；旧变体保留该池与其登记（身份一致），但没有对应角色、界面不列出。
	Fool
}

public readonly record struct PlayerRuneRegistration(
	Type Type,
	HextechRarityTier Rarity,
	PlayerRuneFlags Flags = PlayerRuneFlags.None,
	PlayerRuneCharacterPool? CharacterPool = null,
	int CharacterOrder = 0,
	string TagKey = HextechPlayerRuneRegistry.DefaultTagKey);
