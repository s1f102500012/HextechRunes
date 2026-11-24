namespace HextechRunes;

public sealed class ConstitutionForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(1m),
		new PowerVar<DexterityPower>(1m)
	];

	public override async Task BeforeCombatStart()
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, Stacked(DynamicVars.Strength.BaseValue), Owner.Creature, null);
		await PowerCmd.Apply<DexterityPower>(Owner.Creature, Stacked(DynamicVars.Dexterity.BaseValue), Owner.Creature, null);
	}
}

public sealed class DisasterForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<WeakPower>(1m),
		new PowerVar<VulnerablePower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<WeakPower>(),
		HoverTipFactory.FromPower<VulnerablePower>()
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
		await PowerCmd.Apply<WeakPower>(enemies, Stacked(DynamicVars.Weak.BaseValue), Owner.Creature, null);
		await PowerCmd.Apply<VulnerablePower>(enemies, Stacked(DynamicVars.Vulnerable.BaseValue), Owner.Creature, null);
	}
}

public sealed class GoldLifeForge : HextechForgeBase
{
	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new MaxHpVar(20m)
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

// 血量锻造器:百分比版最大生命(棱彩"生命锻造器"30% 的 1/2),与固定值的生命锻造器并存。
public sealed class GoldHpForge : HextechPercentHpForgeBase
{
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => SavedBaseMaxHpValue;
		set => SavedBaseMaxHpValue = value;
	}

	protected override IEnumerable<DynamicVar> CanonicalVars => CreatePercentVars(15m);
}

public sealed class GoldAttackForge : HextechDamageCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateDamageVars(1.1m);
}

public sealed class GoldProtectionForge : HextechSustainCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateSustainVars(1.1m);
}

public sealed class GoldFocusForge : HextechFocusForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<FocusPower>(1m)
	];
}

public sealed class DrawForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		return player == Owner ? count + Stacked(DynamicVars.Cards.BaseValue) : count;
	}
}

public sealed class RecoveryForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new HealVar(1m)
	];

	public override Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash([Owner.Creature]);
		return CreatureCmd.Heal(Owner.Creature, Stacked(DynamicVars.Heal.BaseValue));
	}
}

public sealed class HourglassForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DamageVar(5m, ValueProp.Unpowered)
	];

	public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
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
			await HextechGameApiCompat.Damage(choiceContext, enemy, Stacked(DynamicVars.Damage.BaseValue), ValueProp.Unpowered, Owner.Creature, null);
		}
	}
}

public sealed class GoldUpgradeForge : HextechUpgradeForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(4)
	];

	protected override string UpgradeRandomSalt => "gold-upgrade-forge";
}

public sealed class SummonForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new SummonVar(2m)
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
			|| !IsNecrobinderPlayer(player))
		{
			return;
		}

		Flash();
		await OstyCmd.Summon(choiceContext, player, Stacked(DynamicVars.Summon.BaseValue), this);
	}
}

public sealed class FleshForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<SleightOfFleshPower>(4m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<SleightOfFleshPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<SleightOfFleshPower>(DynamicVars["SleightOfFleshPower"].BaseValue);
	}
}

public sealed class StarsForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new StarsVar(1)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead || !IsRegentOwner)
		{
			return Task.CompletedTask;
		}

		Flash();
		return PlayerCmd.GainStars(Stacked(DynamicVars.Stars.BaseValue), Owner);
	}
}

public sealed class OrbSlotForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbSlots", 2m)
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
		await OrbCmd.AddSlots(Owner, Math.Max(0, FloorToInt(Stacked(DynamicVars["OrbSlots"].BaseValue))));
	}
}

public sealed class VenomForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<EnvenomPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<EnvenomPower>(DynamicVars["EnvenomPower"].BaseValue);
	}
}

/// <summary>
/// 毒雾锻造器（黄金，通用）：描述写的是结果“回合开始时，给予所有敌人2层中毒”，实现是战斗开始时给持有者2层原版毒雾，
/// 时机、目标与联机表现全部沿用原版能力。
/// </summary>
public sealed class NoxiousFumesForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<NoxiousFumesPower>(2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<NoxiousFumesPower>(DynamicVars["NoxiousFumesPower"].BaseValue);
	}
}

/// <summary>黑暗锻造器（黄金，通用）：描述写结果“每当有一张牌被消耗时，抽1张牌”，实现是战斗开始时给持有者1层原版黑暗之拥。</summary>
public sealed class DarkEmbraceForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<DarkEmbracePower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<DarkEmbracePower>(DynamicVars["DarkEmbracePower"].BaseValue);
	}
}

public sealed class ShrinkForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<ShrinkPower>(2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<ShrinkPower>()
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
		await PowerCmd.Apply<ShrinkPower>(enemies, Stacked(DynamicVars["ShrinkPower"].BaseValue), Owner.Creature, null);
	}
}

public sealed class PlatingForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<PlatingPower>(6m)
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

public sealed class ThornsForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<ThornsPower>(4m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<ThornsPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<ThornsPower>(DynamicVars["ThornsPower"].BaseValue);
	}
}

public sealed class ArtifactForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<ArtifactPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<ArtifactPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<ArtifactPower>(DynamicVars["ArtifactPower"].BaseValue);
	}
}
