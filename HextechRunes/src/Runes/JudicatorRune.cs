namespace HextechRunes;

public sealed class JudicatorRune : HextechRelicBase
{
	private const decimal DamageMultiplierValue = 1.25m;
	private const decimal DamageBonusPercentValue = (DamageMultiplierValue - 1m) * 100m;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("DamageMultiplier", DamageMultiplierValue),
		new DynamicVar("DamageBonusPercent", DamageBonusPercentValue)
	];

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (!IsDamageFromOwner(dealer, cardSource) || target?.Side != CombatSide.Enemy || target.CurrentHp * 2 >= target.MaxHp)
		{
			return 1m;
		}

		return DynamicVars["DamageMultiplier"].BaseValue;
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (wasRemovalPrevented
			|| Owner.Creature.IsDead
			|| target.Side == Owner.Creature.Side
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target))
		{
			return;
		}

		Flash(Array.Empty<Creature>());
		if (Owner.PlayerCombatState is { } combatState && combatState.Energy < combatState.MaxEnergy)
		{
			await PlayerCmd.SetEnergy(combatState.MaxEnergy, Owner);
		}
	}
}
