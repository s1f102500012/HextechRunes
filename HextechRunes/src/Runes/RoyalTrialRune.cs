namespace HextechRunes;

public sealed class RoyalTrialRune : HextechRelicBase
{
	private int _generatedMinionsThisCombat;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(2)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<SovereignBlade>(),
		HoverTipFactory.FromCard<MinionStrike>(),
		HoverTipFactory.FromCard<MinionDiveBomb>(),
		HoverTipFactory.FromCard<MinionSacrifice>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		_generatedMinionsThisCombat = 0;
		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (Owner.Creature.IsDead
			|| !ShouldGenerateMinions(cardPlay, Owner)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		List<CardModel> cards = new(DynamicVars.Cards.IntValue);
		for (int i = 0; i < DynamicVars.Cards.IntValue; i++)
		{
			cards.Add(CreateRandomMinionCard(Owner, combatState));
		}

		Flash();
		await HextechCardGeneration.AddGeneratedCardsToCombat(cards, PileType.Hand, addedByPlayer: true);
	}

	// 重放（PlayIndex > 0，如原版剑圣给君王之剑加的重放）每次都派发 AfterCardPlayed，每次都生成；
	// 自动打出不算"你打出"。仆从牌序号在每次触发时消费，两端按同样的出牌序列推进。
	internal static bool ShouldGenerateMinions(CardPlay cardPlay, Player owner)
	{
		return !cardPlay.IsAutoPlay
			&& cardPlay.Card.Owner == owner
			&& cardPlay.Card is SovereignBlade;
	}

	private CardModel CreateRandomMinionCard(Player owner, HextechCombatState combatState)
	{
		int ordinal = ConsumeCombatProcOrdinal(ref _generatedMinionsThisCombat);
		return HextechStableCombatSpawns.CreateMinionCard(
			combatState,
			owner,
			"royal-trial",
			ordinal);
	}
}
