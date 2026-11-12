namespace HextechRunes;

public sealed class PowerShieldRune : TurnScopedRelicBase
{
	private bool _triggeredThisTurn;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>()
	];

	public override async Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
	{
		EnsureTurnScopedStateCurrent();
		if (creature != Owner.Creature
			|| amount <= 0m
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		Flash();
		int strength = GetPlayerActNumberForScaling();
		await PowerCmd.Apply<HextechPowerShieldTemporaryStrengthPower>(Owner.Creature, strength, Owner.Creature, cardSource);
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
