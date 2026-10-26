namespace HextechRunes;

public sealed class ReanimateUpgradeRune : CardUpgradeRuneBase<Reanimate>
{
	private const decimal BaseCostReduction = 1m;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedDeathsThisCombat
	{
		get => 0;
		set { } // 旧存档仍可能包含该字段；新版不再按死亡数额外减费。
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
	{
		// 与原版忧郁一样只排除被阻止的死亡；爪牙和小手回调时可能已经脱离 CombatState，所以取持有者的战斗。
		if (wasRemovalPrevented
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState is not { } combatState
			|| CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		Flash();
		CardModel card = combatState.CreateCard<Reanimate>(Owner);
		await HextechCardGeneration.AddGeneratedCardToCombat(card, PileType.Hand, addedByPlayer: true);
	}

	public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (card.Owner != Owner
			|| card is not Reanimate
			|| card.EnergyCost.CostsX)
		{
			return false;
		}

		decimal reducedCost = Math.Max(0m, originalCost - BaseCostReduction);
		if (reducedCost == originalCost)
		{
			return false;
		}

		modifiedCost = reducedCost;
		return true;
	}
}
