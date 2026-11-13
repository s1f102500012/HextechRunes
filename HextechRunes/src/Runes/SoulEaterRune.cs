namespace HextechRunes;

public sealed class SoulEaterRune : HextechRelicBase
{
	private const decimal MaxHpGainPercentValue = 0.05m;
	private const decimal MaxHpGainDisplayPercentValue = MaxHpGainPercentValue * 100m;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("MaxHpGainPercent", MaxHpGainPercentValue),
		new DynamicVar("MaxHpGainDisplayPercent", MaxHpGainDisplayPercentValue)
	];

	// 旧版本存档兼容占位：原为本场已获得生命与本场上限记账，已不再使用；名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedHpGainedThisCombat
	{
		get => 0;
		set { }
	}

	// 旧版本存档兼容占位，同上。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedMaxHpGainCapThisCombat
	{
		get => 0;
		set { }
	}

#if STS2_113_OR_NEWER
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
#else
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
#endif
	{
		if (wasRemovalPrevented
			|| Owner.Creature.IsDead
			|| target.Side == Owner.Creature.Side
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target))
		{
			return;
		}

		int rewardMaxHp = GetRewardMaxHpForDeath(target);
		int hpGain = Math.Max(1, FloorToInt(rewardMaxHp * DynamicVars["MaxHpGainPercent"].BaseValue));
		// 吸魂特效不在这里派发:Hook.AfterDeath 链条会被前面的监听器拖住(非最后一只怪时魂"卡一下"
		// 才飞出),已移到 NCreature.StartDeathAnim 的 postfix(死亡动画开始瞬间,见 HextechCombatVfxHooks)。
		Flash();
		await CreatureCmd.GainMaxHp(Owner.Creature, hpGain);
	}

	internal static int GetRewardMaxHpForDeath(Creature target)
	{
		if (!IsTransientInfiniteHpState(target))
		{
			return target.MaxHp;
		}

		int initialMaxHp = GetScaledInitialMonsterMaxHp(target);
		return initialMaxHp > 0
			? Math.Min(target.MaxHp, initialMaxHp)
			: target.MaxHp;
	}

	private static bool IsTransientInfiniteHpState(Creature target)
	{
		return target.HpDisplay is HpDisplay.InfiniteWithNumbers or HpDisplay.InfiniteWithoutNumbers
			|| target.MaxHp >= HextechCreatureStatLimits.StatHardCap;
	}

	private static int GetScaledInitialMonsterMaxHp(Creature target)
	{
		int initialMaxHp = target.MonsterMaxHpBeforeModification ?? target.Monster?.MaxInitialHp ?? 0;
		if (initialMaxHp <= 0)
		{
			return 0;
		}

		if (target.CombatState == null || target.CombatState.Players.Count <= 1)
		{
			return initialMaxHp;
		}

		decimal scaledMaxHp = Creature.ScaleHpForMultiplayer(
			initialMaxHp,
			target.CombatState.Encounter,
			target.CombatState.Players.Count,
			target.CombatState.RunState.CurrentActIndex);
		return Math.Clamp(FloorToInt(scaledMaxHp), 1, HextechCreatureStatLimits.StatHardCap);
	}
}
