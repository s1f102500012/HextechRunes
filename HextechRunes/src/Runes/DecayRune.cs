namespace HextechRunes;

public sealed class DecayRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<WeakPower>(),
		HoverTipFactory.FromPower<StrengthPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side || Owner.Creature.IsDead)
		{
			return;
		}

		List<Creature> affected = [];
		foreach (Creature enemy in combatState.HittableEnemies)
		{
			decimal weak = enemy.GetPowerAmount<WeakPower>();
			if (weak <= 0m)
			{
				continue;
			}

			decimal strengthLoss = weak * DynamicVars.Strength.BaseValue;
			affected.Add(enemy);
			await HextechPowerCmdCompat.Apply<HextechTemporaryStrengthLossPower>(enemy, strengthLoss, Owner.Creature, null);
		}

		if (affected.Count > 0)
		{
			Flash(affected);
		}
	}
}
