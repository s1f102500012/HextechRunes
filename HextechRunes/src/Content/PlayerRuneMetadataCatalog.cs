namespace HextechRunes;

internal sealed class PlayerRuneMetadataCatalog
{
	private readonly IReadOnlyDictionary<Type, PlayerRuneRegistration> _registrationByType;

	public PlayerRuneMetadataCatalog(IReadOnlyList<PlayerRuneRegistration> registrations)
	{
		Registrations = registrations.ToArray();
		// ToDictionary 同时拒绝重复登记的类型。
		_registrationByType = Registrations.ToDictionary(static registration => registration.Type);
		TypesByRarity = Enum.GetValues<HextechRarityTier>()
			.ToDictionary(
				static rarity => rarity,
				rarity => SelectTypes(registration => registration.Rarity == rarity));
		TypesByCharacter = Enum.GetValues<PlayerRuneCharacterPool>()
			.ToDictionary(
				static characterPool => characterPool,
				characterPool => (IReadOnlyList<Type>)Registrations
					.Where(registration => registration.CharacterPool == characterPool)
					.OrderBy(static registration => registration.CharacterOrder)
					.Select(static registration => registration.Type)
					.ToArray());
		TypesByFlag = Enum.GetValues<PlayerRuneFlags>()
			.Where(static flag => flag != PlayerRuneFlags.None)
			.ToDictionary(
				static flag => flag,
				flag => SelectTypes(registration => (registration.Flags & flag) != 0));
		CharacterSpecificTypes = Registrations
			.Where(static registration => registration.CharacterPool.HasValue)
			.Select(static registration => registration.Type)
			.ToHashSet();
		// 按稀有度分组：调整稀有度不应强迫维护者重排原始注册表。
		AllTypes = Enum.GetValues<HextechRarityTier>()
			.SelectMany(rarity => TypesByRarity[rarity])
			.ToArray();
	}

	public IReadOnlyList<PlayerRuneRegistration> Registrations { get; }

	public IReadOnlyList<Type> AllTypes { get; }

	public IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<Type>> TypesByRarity { get; }

	public IReadOnlyDictionary<PlayerRuneCharacterPool, IReadOnlyList<Type>> TypesByCharacter { get; }

	public IReadOnlyDictionary<PlayerRuneFlags, IReadOnlyList<Type>> TypesByFlag { get; }

	public IReadOnlySet<Type> CharacterSpecificTypes { get; }

	public bool TryGetRegistration(Type runeType, out PlayerRuneRegistration registration)
	{
		return _registrationByType.TryGetValue(runeType, out registration);
	}

	public bool IsVisible(Type runeType)
	{
		return _registrationByType.ContainsKey(runeType)
			&& !HasFlag(runeType, PlayerRuneFlags.Disabled | PlayerRuneFlags.Hidden);
	}

	public bool IsConfigurable(Type runeType)
	{
		return _registrationByType.ContainsKey(runeType)
			&& !HasFlag(runeType, PlayerRuneFlags.SelectionExcluded | PlayerRuneFlags.Hidden);
	}

	public bool HasFlag(Type runeType, PlayerRuneFlags flag)
	{
		return _registrationByType.TryGetValue(runeType, out PlayerRuneRegistration registration)
			&& (registration.Flags & flag) != 0;
	}

	public bool TryGetRarity(Type runeType, out HextechRarityTier rarity)
	{
		if (_registrationByType.TryGetValue(runeType, out PlayerRuneRegistration registration))
		{
			rarity = registration.Rarity;
			return true;
		}

		rarity = default;
		return false;
	}

	// 枚举值 Silver=0、Gold=1、Prismatic=2 就是图鉴排序；未登记的排在最后。
	public int GetRaritySortOrder(Type runeType)
	{
		return TryGetRarity(runeType, out HextechRarityTier rarity) ? (int)rarity : 3;
	}

	public IReadOnlyList<Type> GetConfigurableTypesForRarity(HextechRarityTier rarity)
	{
		return TypesByRarity.TryGetValue(rarity, out IReadOnlyList<Type>? runeTypes)
			? runeTypes.Where(IsConfigurable).ToArray()
			: Array.Empty<Type>();
	}

	private IReadOnlyList<Type> SelectTypes(Func<PlayerRuneRegistration, bool> predicate)
	{
		return Registrations
			.Where(predicate)
			.Select(static registration => registration.Type)
			.ToArray();
	}
}
