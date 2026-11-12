namespace HextechRunes;

public sealed class MakeItMineRune : HextechSharedCombatVictoryRuneBase
{
	private int _stacks;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedStacks
	{
		get => _stacks;
		set
		{
			_stacks = Math.Max(0, value);
			InvokeDisplayAmountChanged();
		}
	}

	public override bool ShowCounter => true;

	public override int DisplayAmount => _stacks;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new SummonVar(4m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override Task ApplySharedCombatVictory(CombatRoom room)
	{
		if (Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		SavedStacks++;
		Flash();
		return Task.CompletedTask;
	}

	// "战斗第 1 轮召唤"按设计裁决保留 RoundNumber 口径(持有者第 1 轮的额外回合不算新的一轮)。
	// 额外回合(佩尔之眼等)不推进 RoundNumber 且回合开始 hook 会重入,同一轮只召唤一次,避免叠层召唤双倍。
	private int _lastProcRound = -1;

	public override Task BeforeCombatStart()
	{
		_lastProcRound = -1;
		return Task.CompletedTask;
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState is not HextechCombatState combatState
			|| combatState.RoundNumber > 1
			|| _stacks <= 0
			|| !IsNecrobinderPlayer(player)
			|| !HextechRoundInterval.TryClaimRound(ref _lastProcRound, combatState.RoundNumber))
		{
			return;
		}

		Flash();
		await OstyCmd.Summon(choiceContext, player, _stacks * DynamicVars.Summon.BaseValue, this);
	}
}
