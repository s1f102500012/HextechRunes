namespace HextechRunes;

public sealed class LifeFlowRune : TurnScopedRelicBase
{
	private const decimal HealPercentValue = 0.05m;

	private int _procsThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedProcsThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent();
			return _procsThisTurn;
		}
		set
		{
			_procsThisTurn = Math.Max(0, value);
			InvokeDisplayAmountChanged();
			UpdateTurnScopedStateIdentity();
		}
	}

	public override bool ShowCounter => IsInLiveCombat;

	public override int DisplayAmount => !IsCanonical ? Math.Max(0, DynamicVars["MaxProcsPerTurn"].IntValue - _procsThisTurn) : 0;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("HealPercent", HealPercentValue),
		new DynamicVar("MaxProcsPerTurn", 3m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsIroncladPlayer(player);
	}

	public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
	{
		EnsureTurnScopedStateCurrent();
		if (!IsOwnedCard(card)
			|| Owner.Creature.IsDead
			|| !TryConsumeTurnProc(ref _procsThisTurn, DynamicVars["MaxProcsPerTurn"].IntValue))
		{
			return Task.CompletedTask;
		}

		int healAmount = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * DynamicVars["HealPercent"].BaseValue));
		Flash();
		return CreatureCmd.Heal(Owner.Creature, healAmount);
	}

	protected override void ResetTurnScopedState()
	{
		_procsThisTurn = 0;
		InvokeDisplayAmountChanged();
	}
}
