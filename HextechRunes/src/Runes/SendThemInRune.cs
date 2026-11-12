namespace HextechRunes;

public sealed class SendThemInRune : HextechRelicBase
{
	private int _generatedMinionsThisCombat;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
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

	public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		// 发放闸门之外，外部接口(RelicBundleGrantHelper)、控制台或其他模组可把本符文直接给任意角色，触发时再判角色。
		if (player != Owner || Owner.Creature.IsDead || !IsRegentPlayer(player))
		{
			return;
		}

		int ordinal = ConsumeCombatProcOrdinal(ref _generatedMinionsThisCombat);
		CardModel card = HextechStableCombatSpawns.CreateMinionCard(combatState, Owner, "send-them-in", ordinal);

		Flash();
		await HextechCardGeneration.AddGeneratedCardToCombat(card, PileType.Hand, addedByPlayer: true);
	}
}
