namespace HextechRunes;

public sealed class StrengthForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(1m)
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<StrengthPower>(DynamicVars.Strength.BaseValue);
	}
}

public sealed class DexterityForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<DexterityPower>(1m)
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<DexterityPower>(DynamicVars.Dexterity.BaseValue);
	}
}

public sealed class SilverPlatingForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<PlatingPower>(4m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PlatingPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<PlatingPower>(DynamicVars["PlatingPower"].BaseValue);
	}
}

public sealed class UpgradeForge : HextechUpgradeForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(2)
	];

	protected override string UpgradeRandomSalt => "silver-upgrade-forge";
}

public sealed class FocusForge : HextechFocusForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<FocusPower>(2m)
	];
}

public sealed class LifeForge : HextechForgeBase
{
	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new MaxHpVar(8m)
	];

	public override Task AfterObtained()
	{
		if (Owner == null)
		{
			return Task.CompletedTask;
		}

		Flash();
		return CreatureCmd.GainMaxHp(Owner.Creature, DynamicVars.MaxHp.BaseValue);
	}
}

// 血量锻造器:百分比版最大生命(棱彩"生命锻造器"30% 的 1/4),与固定值的生命锻造器并存。
public sealed class SilverHpForge : HextechPercentHpForgeBase
{
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => SavedBaseMaxHpValue;
		set => SavedBaseMaxHpValue = value;
	}

	protected override IEnumerable<DynamicVar> CanonicalVars => CreatePercentVars(7.5m);
}

public sealed class SilverAttackForge : HextechDamageCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateDamageVars(1.05m);
}

public sealed class SilverProtectionForge : HextechSustainCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateSustainVars(1.05m);
}

public sealed class PocketForge : HextechForgeBase
{
	/// <summary>
	/// 原版 SerializablePotion 只用 4 bit 写 SlotIndex(0~15),所以药水槽总数超过 16 之后,
	/// 高槽位的药水在联机 SyncPlayerData 里会被截成槽 0、被主机丢弃,客户端本地却还留着,
	/// 直接造成 player:potions 分叉(例如口袋锻造器堆到 9 层、22 槽)。总槽位封顶在 16。
	/// </summary>
	internal const int MaxSerializablePotionSlots = 16;

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PotionSlots", 2m)
	];

	public override Task AfterObtained()
	{
		if (Owner == null)
		{
			return Task.CompletedTask;
		}

		Flash();
		int increase = ClampSlotIncrease(Owner.MaxPotionCount, DynamicVars["PotionSlots"].IntValue);
		if (increase > 0)
		{
			Owner.AddToMaxPotionCount(increase);
		}

		return Task.CompletedTask;
	}

	// 旧存档里已经超过 16 槽的,在下一场战斗开始时收回超出部分:原版缩槽会把高槽位的药水挪进空的低槽位,
	// 两端对称执行,不引入新的分叉点。
	public override Task BeforeCombatStart()
	{
		if (Owner != null && Owner.MaxPotionCount > MaxSerializablePotionSlots)
		{
			Owner.SubtractFromMaxPotionCount(Owner.MaxPotionCount - MaxSerializablePotionSlots);
		}

		return Task.CompletedTask;
	}

	internal static int ClampSlotIncrease(int currentMaxPotionCount, int requestedIncrease)
	{
		return Math.Clamp(requestedIncrease, 0, Math.Max(0, MaxSerializablePotionSlots - currentMaxPotionCount));
	}
}

public sealed class PreparedForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(2)
	];

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		if (player != Owner || !IsOwnersFirstTurn)
		{
			return count;
		}

		return count + Stacked(DynamicVars.Cards.BaseValue);
	}
}

public sealed class FireworksForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DamageVar(6m, ValueProp.Unpowered)
	];

	public override async Task BeforeCombatStart()
	{
		if (Owner == null || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return;
		}

		List<Creature> enemies = Owner.Creature.CombatState.HittableEnemies
			.Where(static enemy => enemy.IsAlive)
			.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Flash(enemies);
		foreach (Creature enemy in enemies)
		{
			await HextechGameApiCompat.Damage(new BlockingPlayerChoiceContext(), enemy, Stacked(DynamicVars.Damage.BaseValue), ValueProp.Unpowered, Owner.Creature, null);
		}
	}
}

public sealed class VigorForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<VigorPower>(2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<VigorPower>()
	];

	public override Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash();
		return PowerCmd.Apply<VigorPower>(Owner.Creature, Stacked(DynamicVars["VigorPower"].BaseValue), Owner.Creature, null);
	}
}

public sealed class BlockForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new BlockVar(3m, ValueProp.Unpowered)
	];

	public override Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash([Owner.Creature]);
		return CreatureCmd.GainBlock(Owner.Creature, Stacked(DynamicVars.Block.BaseValue), ValueProp.Unpowered, null);
	}
}

public sealed class NecrobinderForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new SummonVar(6m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner
			|| Owner == null
			|| Owner.Creature.IsDead
			|| !IsOwnersFirstTurn
			|| !IsNecrobinderPlayer(player))
		{
			return;
		}

		Flash();
		await OstyCmd.Summon(choiceContext, player, Stacked(DynamicVars.Summon.BaseValue), this);
	}
}

public sealed class SilverStarsForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new StarsVar(2)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (Owner == null || side != Owner.Creature.Side || !IsOwnersFirstTurn || !IsRegentOwner)
		{
			return Task.CompletedTask;
		}

		Flash();
		return PlayerCmd.GainStars(Stacked(DynamicVars.Stars.BaseValue), Owner);
	}
}

public sealed class SilverOrbForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbCount", 2m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsDefectPlayer(player);
	}

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (Owner == null || side != Owner.Creature.Side || !IsOwnersFirstTurn || !IsDefectOwner)
		{
			return;
		}

		Flash();
		int orbCount = Math.Max(0, FloorToInt(Stacked(DynamicVars["OrbCount"].BaseValue)));
		for (int i = 0; i < orbCount; i++)
		{
			OrbModel orb = HextechStableCombatSpawns.CreateOrb((RunState)Owner.RunState, Owner, "silver-orb-forge", i, combatState.RoundNumber);
			await OrbCmd.Channel(new BlockingPlayerChoiceContext(), orb, Owner);
		}
	}
}

public sealed class ForgingForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new ForgeVar("ForgeAmount", 8)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (Owner == null || side != Owner.Creature.Side || !IsOwnersFirstTurn || !IsRegentOwner)
		{
			return;
		}

		Flash();
		await ForgeCmd.Forge(Stacked(DynamicVars["ForgeAmount"].BaseValue), Owner, this);
	}
}

// 恢复锻造器（白银）：战斗开始时回复生命。黄金同名锻造器 RecoveryForge 是每回合开始回复，二者 ModelId 不同、不互相叠层。
public sealed class SilverRecoveryForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new HealVar(2m)
	];

	public override Task BeforeCombatStart()
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash([Owner.Creature]);
		return CreatureCmd.Heal(Owner.Creature, Stacked(DynamicVars.Heal.BaseValue));
	}
}
