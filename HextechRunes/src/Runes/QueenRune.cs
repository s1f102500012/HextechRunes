namespace HextechRunes;

public sealed class QueenRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<WeakPower>(2m),
		new PowerVar<VulnerablePower>(2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<WeakPower>(),
		HoverTipFactory.FromPower<VulnerablePower>()
	];

	public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side)
		{
			return;
		}

		Creature[] targets = combatState.HittableEnemies.ToArray();
		if (targets.Length == 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<WeakPower>(targets, DynamicVars.Weak.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<VulnerablePower>(targets, DynamicVars.Vulnerable.BaseValue, Owner.Creature, null);
	}
}
