namespace HextechRunes;

public sealed class RedEnvelopeRune : HextechSharedCombatVictoryRuneBase
{
	internal const int BaseForgeChance = 25;
	internal const int ForgeChanceStep = 5;

	// 金币分支的区间；文案写的是字面值（20~50），改数值要同步九语言。
	private const int MinGold = 20;
	private const int MaxGold = 50;

	// 锻造器那一侧走药水掉落式动态掉率:掉一次降 5%,没掉一次升 5%;剩余概率给金币。只存相对 25% 的偏移。
	private int _forgeChanceOffset;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	private int SavedRedEnvelopeForgeChanceOffset
	{
		get => _forgeChanceOffset;
		set => _forgeChanceOffset = HextechDynamicDropChance.ClampOffset(value, BaseForgeChance);
	}

	internal int CurrentForgeChance => HextechDynamicDropChance.CurrentChance(_forgeChanceOffset, BaseForgeChance);

	public override Task ApplySharedCombatVictory(CombatRoom room)
	{
		if (Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash(Array.Empty<Creature>());
		bool forgeDropped = HextechStableRandom.PercentChance(
			(RunState)Owner.RunState,
			CurrentForgeChance,
			"red-envelope-forge",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Relics.Count.ToString());
		_forgeChanceOffset = HextechDynamicDropChance.NextOffset(_forgeChanceOffset, BaseForgeChance, ForgeChanceStep, forgeDropped);
		if (forgeDropped)
		{
			HextechForgeGrantHelper.AddRandomForgeReward(Owner, room);
		}
		else
		{
			HextechGoldRewardHelper.AddStableRangedExtraGoldReward(
				room,
				Owner,
				MinGold,
				MaxGold,
				"red-envelope-gold",
				Owner.Relics.Count.ToString());
		}

		return Task.CompletedTask;
	}
}
