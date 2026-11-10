namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal async Task TryApplyServantMasterIllusion(Creature creature, Creature? applier, CardModel? cardSource)
	{
		if (CombatTracking.HandlingServantMasterIllusion
			|| creature.Side != CombatSide.Enemy
			|| !creature.IsAlive
			|| creature.CombatState?.RunState != RunState
			|| !creature.HasPower<MinionPower>()
			|| creature.HasPower<IllusionPower>())
		{
			return;
		}

		try
		{
			CombatTracking.HandlingServantMasterIllusion = true;
			await PowerCmd.Apply<IllusionPower>(creature, 1m, applier ?? creature, cardSource);
		}
		finally
		{
			CombatTracking.HandlingServantMasterIllusion = false;
		}
	}

	private bool TrackPlayerAttackCardPlayedThisTurn(CardPlay cardPlay)
	{
		return HextechCombatProcTracker.TrackPlayerAttackCardPlayedThisTurn(CombatTracking, cardPlay);
	}

	internal void RefreshPlayerAttackCostDoublingPreviews(IEnumerable<Creature> playerCreatures)
	{
		if (!HextechEnemyHexEffects.HasActiveAttackCostPreviewEffect(this))
		{
			return;
		}

		foreach (Creature playerCreature in playerCreatures)
		{
			Player? player = playerCreature.Player;
			if (player == null
				|| playerCreature.CombatState?.RunState != RunState)
			{
				continue;
			}

			foreach (CardModel card in PileType.Hand.GetPile(player).Cards)
			{
				if (card.Type == CardType.Attack && !card.EnergyCost.CostsX)
				{
					HextechPresentation.TryRun("AttackCostPreview", $"Cost visual refresh failed for {card.Id}", card.InvokeEnergyCostChanged);
				}
			}
		}
	}

	internal int GetPlayerAttacksPlayedThisTurn(CardModel card)
	{
		return HextechCombatProcTracker.GetPlayerAttacksPlayedThisTurn(CombatTracking, card);
	}

	public decimal ModifyEnemyHealAmount(Creature creature, decimal amount)
	{
		if (creature.Side != CombatSide.Enemy)
		{
			return amount;
		}

		HextechEnemyHexContext context = new(this);
		HextechEnemyHexEffect[] activeEffects = HextechEnemyHexEffects.GetActive(this).ToArray();
		decimal multiplier = HextechEnemyCoefficientHelper.CombineMultipliersByHex(
			activeEffects.Select(effect => (
				effect.Kind,
				effect.ModifyEnemyHealMultiplicative(context, creature, amount))));
		amount *= multiplier;
		foreach (HextechEnemyHexEffect effect in activeEffects.OrderBy(static effect => effect.EnemyHealOrder))
		{
			amount = effect.ModifyEnemyHealAmount(context, creature, amount);
		}

		return amount;
	}
}
