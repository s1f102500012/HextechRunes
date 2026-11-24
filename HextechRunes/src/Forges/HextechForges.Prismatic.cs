namespace HextechRunes;

public sealed class PrismaticLifeForge : HextechPercentHpForgeBase
{
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => SavedBaseMaxHpValue;
		set => SavedBaseMaxHpValue = value;
	}

	protected override IEnumerable<DynamicVar> CanonicalVars => CreatePercentVars(30m);
}

public sealed class AttackForge : HextechDamageCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateDamageVars(1.2m);
}

public sealed class ProtectionForge : HextechSustainCoefficientForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars => CreateSustainVars(1.2m);
}

public sealed class EnergyForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new EnergyVar(1)
	];

	public override Task AfterEnergyResetLate(Player player)
	{
		if (Owner == null || player != Owner || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		Flash();
		return PlayerCmd.GainEnergy(Stacked(DynamicVars.Energy.BaseValue), Owner);
	}
}

public sealed class RitualForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<RitualPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<RitualPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<RitualPower>(DynamicVars["RitualPower"].BaseValue);
	}
}

public sealed class RegenForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<RegenPower>(4m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<RegenPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<RegenPower>(DynamicVars["RegenPower"].BaseValue);
	}
}

public sealed class BufferForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<BufferPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<BufferPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<BufferPower>(DynamicVars["BufferPower"].BaseValue);
	}
}

public sealed class SlipperyForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<SlipperyPower>(2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<SlipperyPower>()
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<SlipperyPower>(DynamicVars["SlipperyPower"].BaseValue);
	}
}

public sealed class PrismaticArtifactForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<ArtifactPower>(2m)
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

public sealed class FortuneForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new GoldVar(100)
	];

	internal int ExtraGoldRewardAmount => FloorToInt(Stacked(DynamicVars.Gold.BaseValue));

	public override Task AfterCombatEnd(CombatRoom room)
	{
		if (Owner == null)
		{
			return Task.CompletedTask;
		}

		Flash();
		HextechGoldRewardHelper.AddFixedExtraGoldReward(
			room,
			Owner,
			ExtraGoldRewardAmount);
		return Task.CompletedTask;
	}
}

/// <summary>回响锻造器（棱彩，通用）：描述写结果“你每回合打出的第一张牌会多打出一次”，实现是战斗开始时给持有者1层原版回响形态；叠层时每层多覆盖一张牌。</summary>
public sealed class EchoForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<EchoFormPower>(1m)
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<EchoFormPower>(DynamicVars["EchoFormPower"].BaseValue);
	}
}

public sealed class VoidForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<VoidFormPower>(1m)
	];

	public override Task BeforeCombatStart()
	{
		return ApplyStackedPowerAtCombatStart<VoidFormPower>(DynamicVars["VoidFormPower"].BaseValue);
	}
}
