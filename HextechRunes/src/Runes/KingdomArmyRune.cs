namespace HextechRunes;

public sealed class KingdomArmyRune : HextechRelicBase
{
	private int _generatedMinionsThisCombat;
	// 生成仆从牌期间嵌套进来的铸造不再生成。否则与凝辉(生成牌→辉星)、王令(辉星→铸造)构成无终点的循环,
	// 遥测里三件同持的对局集中卡在拿到第三件后的第一场战斗。
	private bool _generating;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<MinionStrike>(),
		HoverTipFactory.FromCard<MinionDiveBomb>(),
		HoverTipFactory.FromCard<MinionSacrifice>()
	];

	public override bool IsAvailableForPlayer(Player player) => IsRegentPlayer(player);

	public override Task BeforeCombatStart()
	{
		_generatedMinionsThisCombat = 0;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_generatedMinionsThisCombat = 0;
		return Task.CompletedTask;
	}

	public override async Task AfterForge(decimal amount, Player forger, AbstractModel? source)
	{
		if (_generating || forger != Owner || amount <= 0m || Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null || Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		// 按铸造事件取一次同步序号，与铸造数值或场上的君王之剑数量无关。
		int ordinal = ConsumeCombatProcOrdinal(ref _generatedMinionsThisCombat);
		CardModel card = HextechStableCombatSpawns.CreateMinionCard(combatState, Owner, "kingdom-army", ordinal);
		Flash();
		_generating = true;
		try
		{
			await HextechCardGeneration.AddGeneratedCardToCombat(card, PileType.Hand, addedByPlayer: true);
		}
		finally
		{
			_generating = false;
		}
	}
}
