namespace HextechRunes;

// 当前生效敌方海克斯的缓存。失效键：阶段序号、是否处于战斗房间（决定未结算阶段能否在战斗中恢复）、ActState.Version。
// 结果只由这三项与 ActState 内容决定，ActState 的每次写入都会递增 Version；按注册顺序排好的效果数组挂在同一组键下，
// 键变化时与海克斯列表一起作废。缓存出的列表和数组只读，调用方不得写入。
internal sealed class HextechActiveMonsterHexCache
{
	private IReadOnlyList<MonsterHexKind>? _activeHexes;
	private HashSet<MonsterHexKind>? _activeHexSet;
	private HextechEnemyHexEffect[]? _activeEffects;
	private int[]? _activeEffectOrders;
	private int _actIndex = int.MinValue;
	private bool _combatRecovery;
	private int _version = int.MinValue;

	// combatRecovery 必须等于 owner.ShouldRecoverMonsterHexInCombat(actIndex)；actIndex 是当前阶段时它只取决于是否在战斗房间。
	public IReadOnlyList<MonsterHexKind> Get(
		HextechMayhemModifier owner,
		HextechMayhemActState actState,
		int actIndex,
		bool combatRecovery)
	{
		int version = actState.Version;
		if (_activeHexes != null
			&& _activeHexSet != null
			&& _actIndex == actIndex
			&& _combatRecovery == combatRecovery
			&& _version == version)
		{
			return _activeHexes;
		}

		_activeHexes = actState.GetActiveMonsterHexes(actIndex, owner.ShouldRecoverMonsterHexInCombat);
		_activeHexSet = _activeHexes.ToHashSet();
		_activeEffects = null;
		_activeEffectOrders = null;
		_actIndex = actIndex;
		_combatRecovery = combatRecovery;
		_version = version;
		return _activeHexes;
	}

	public bool Contains(
		HextechMayhemModifier owner,
		HextechMayhemActState actState,
		int actIndex,
		bool combatRecovery,
		MonsterHexKind hex)
	{
		_ = Get(owner, actState, actIndex, combatRecovery);
		return _activeHexSet!.Contains(hex);
	}

	// 与逐个效果按注册顺序判断 Contains 的结果逐元素一致；orderIndexes[i] 是 effects[i] 在注册顺序中的位置，严格递增。
	public HextechEnemyHexEffect[] GetActiveEffects(
		HextechMayhemModifier owner,
		HextechMayhemActState actState,
		int actIndex,
		bool combatRecovery,
		out int[] orderIndexes)
	{
		_ = Get(owner, actState, actIndex, combatRecovery);
		if (_activeEffects == null || _activeEffectOrders == null)
		{
			_activeEffects = HextechEnemyHexEffects.CreateActiveEffects(_activeHexSet!, out int[] orders);
			_activeEffectOrders = orders;
		}

		orderIndexes = _activeEffectOrders;
		return _activeEffects;
	}
}
