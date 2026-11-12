namespace HextechRunes;

public sealed class BigHandsRune : HextechRelicBase
{
	private const decimal SummonBonusPercentValue = (SummonMultiplier - 1m) * 100m;

	internal const decimal SummonMultiplier = 1.5m;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("SummonBonusPercent", SummonBonusPercentValue)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override decimal ModifySummonAmount(Player summoner, decimal amount, AbstractModel? source)
	{
		return summoner == Owner ? amount * SummonMultiplier : amount;
	}
}
