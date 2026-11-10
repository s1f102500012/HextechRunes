namespace HextechRunes;

public sealed class SerpentsFangRune : HextechRelicBase
{
	// 固定层数而非"等量中毒":后者等于伤害无条件翻倍,强度超模。
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PoisonPerHit", 4m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| result.UnblockedDamage <= 0
			|| HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse
			|| HextechCombatHooks.IsResolvingSleightOfFleshPowerDebuffResponse
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		Flash([target]);
		await PowerCmd.Apply<PoisonPower>(target, DynamicVars["PoisonPerHit"].BaseValue, Owner.Creature, null);
	}
}
