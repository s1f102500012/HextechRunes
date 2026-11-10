namespace HextechRunes;

// 生命部分是持续的最大生命系数(×1.5)而非一次性发放:与巨人化共用 HextechMaxHpScaling 管线,
// 之后的所有最大生命增减都按系数放大,顶栏面板"生命系数"也随之显示 150%。
public sealed class AstralBodyRune : HextechRelicBase, IHextechMaxHpScalingRune
{
	private const decimal DamageMultiplierValue = 0.9m;
	private const decimal DamageReductionPercentValue = (1m - DamageMultiplierValue) * 100m;

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

	public decimal MaxHpScale => 1m + DynamicVars["MaxHpPercent"].BaseValue / 100m;

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("MaxHpPercent", 50m),
		new DynamicVar("DamageMultiplier", DamageMultiplierValue),
		new DynamicVar("DamageReductionPercent", DamageReductionPercentValue)
	];

	public override async Task AfterObtained()
	{
		await HextechMaxHpScaling.ReapplyScale(Owner);
	}

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (!IsDamageFromOwnerToEnemyOrPreview(target, dealer, cardSource))
		{
			return 1m;
		}

		return DynamicVars["DamageMultiplier"].BaseValue;
	}
}
