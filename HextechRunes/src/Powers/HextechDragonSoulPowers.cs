namespace HextechRunes;

public sealed class HextechOceanDragonSoulPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side != Owner.Side || Amount <= 0m || !Owner.IsAlive)
		{
			return;
		}

		Flash();
		await CreatureCmd.Heal(Owner, Amount);
	}
}

public sealed class HextechInfernalDragonSoulPower : HextechPowerBase
{
	private bool _triggeredThisTurn;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side == Owner.Side)
		{
			_triggeredThisTurn = false;
		}

		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (HextechCombatProcTracker.HasOwnerTurnProcTriggered(_triggeredThisTurn)
			|| Amount <= 0m
			|| !Owner.IsAlive
			|| !cardPlay.IsFirstInSeries
			|| cardPlay.IsAutoPlay
			|| cardPlay.Card.Owner?.Creature != Owner
			|| cardPlay.Card.Type != CardType.Attack)
		{
			return;
		}

		List<Creature> targets = GetTargets(cardPlay).ToList();
		if (targets.Count == 0)
		{
			return;
		}

		if (!HextechCombatProcTracker.TryConsumeOwnerTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<HextechBurnPower>(targets, Amount, Owner, cardPlay.Card);
	}

	private IEnumerable<Creature> GetTargets(CardPlay cardPlay)
	{
		if (cardPlay.Target is { Side: CombatSide.Enemy, IsAlive: true } target)
		{
			yield return target;
			yield break;
		}

		if (cardPlay.Card.TargetType != TargetType.AllEnemies || Owner.CombatState == null)
		{
			yield break;
		}

		foreach (Creature enemy in Owner.CombatState.HittableEnemies)
		{
			yield return enemy;
		}
	}
}

public sealed class HextechDragonSoulPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterEnergyResetLate(Player player)
	{
		if (player.Creature != Owner || Amount <= 0m || !Owner.IsAlive)
		{
			return;
		}

		await PlayerCmd.GainEnergy(Amount, player);
		Flash();
	}
}

public sealed class HextechMountainDragonSoulPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Side || Amount <= 0m || !Owner.IsAlive)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<PlatingPower>(Owner, Amount, Owner, null);
	}
}

public sealed class HextechChemtechDragonSoulPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Side || Amount <= 0m || !Owner.IsAlive || Owner.Player is not Player player)
		{
			return;
		}

		List<PotionModel> candidates = HextechGameApiCompat.GetPotionOptions(player).ToList();
		if (candidates.Count == 0)
		{
			return;
		}

		Flash();
		for (int i = 0; i < (int)Amount; i++)
		{
			PotionModel potion = HextechStableRandom.Pick(
				candidates,
				(RunState)player.RunState,
				HextechStableRandom.PotionKey,
				"chemtech-dragon-soul-potion",
				HextechStableRandom.PlayerKey(player),
				combatState.RoundNumber.ToString(),
				i.ToString()).ToMutable();
			await PotionCmd.TryToProcure(potion, player);
		}
	}
}

public sealed class HextechCloudDragonSoulPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override decimal ModifyHandDraw(Player player, decimal count)
	{
		return player.Creature == Owner && Owner.IsAlive ? count + Amount : count;
	}
}
