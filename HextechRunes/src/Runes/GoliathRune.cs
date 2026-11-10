namespace HextechRunes;

public sealed class GoliathRune : HextechRelicBase, IHextechMaxHpScalingRune, IHextechHealingMultiplierProvider
{
	private const decimal ScaleValue = 1.35m;
	private const decimal StatMultiplierValue = 1.2m;

	private int _baseMaxHp;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => _baseMaxHp;
		set => _baseMaxHp = Math.Max(0, value);
	}

	public int BaseMaxHp
	{
		get => _baseMaxHp;
		set => _baseMaxHp = Math.Max(1, value);
	}

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("DamageMultiplier", StatMultiplierValue),
		new DynamicVar("SustainMultiplier", StatMultiplierValue),
		new DynamicVar("Scale", ScaleValue)
	];

	public decimal MaxHpScale => DynamicVars["Scale"].BaseValue;

	internal float BodyScaleDelta => (float)DynamicVars["Scale"].BaseValue - 1f;

	public override async Task AfterObtained()
	{
		// 主符文可能是先获得的另一个系数参与者(星界躯体/百分比锻造器),此时基础值已在它那里,按新乘积重算。
		IHextechMaxHpBaseHolder primary = HextechMaxHpScaling.GetPrimary(Owner) ?? this;
		HextechMaxHpScaling.EnsureBaseInitialized(Owner, primary, assumeAlreadyScaled: false);
		await CreatureCmd.SetMaxHp(Owner.Creature, primary.BaseMaxHp);
		await CreatureCmd.Heal(Owner.Creature, Owner.Creature.MaxHp - Owner.Creature.CurrentHp);
		HextechPlayerBodyScaleHelper.Update(Owner);
	}

	public override Task AfterRoomEntered(AbstractRoom room)
	{
		HextechMaxHpScaling.EnsureScaledBaseInitialized(Owner);
		HextechPlayerBodyScaleHelper.Update(Owner);
		return Task.CompletedTask;
	}

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return IsDamageFromOwnerToEnemyOrPreview(target, dealer, cardSource) ? DynamicVars["DamageMultiplier"].BaseValue : 1m;
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner.Creature ? DynamicVars["SustainMultiplier"].BaseValue : 1m;
	}

	decimal IHextechHealingMultiplierProvider.ModifyHealingMultiplicative(Player player, Creature creature, decimal amount)
	{
		return IsFirstOwnedInstance(player) ? DynamicVars["SustainMultiplier"].BaseValue : 1m;
	}
}
