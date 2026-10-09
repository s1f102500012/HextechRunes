namespace HextechRunes;

public sealed class KakaRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<RitualPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<RitualPower>()
	];

	internal static bool BlocksAttack(CardModel card)
	{
		Player? owner = card.Owner;
		return owner != null
			&& card.Type == CardType.Attack
			&& owner.GetRelic<KakaRune>() != null
			&& owner.Creature.CombatState?.RoundNumber == 1;
	}

	// 第 1 回合禁止攻击牌:走原版 Hook.ShouldPlay,CanPlay 会带上 BlockedByHook 与本遗物作为 preventer。
	public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
	{
		return autoPlayType != AutoPlayType.None || card.Owner != Owner || !BlocksAttack(card);
	}

	private bool _ritualGrantedThisCombat;

	public override Task BeforeCombatStart()
	{
		_ritualGrantedThisCombat = false;
		return Task.CompletedTask;
	}

	// 额外回合不推进 RoundNumber、玩家回合开始钩子会重入：第 2 回合的仪式每场只给一次。
	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState?.RoundNumber != 2
			|| _ritualGrantedThisCombat)
		{
			return;
		}

		_ritualGrantedThisCombat = true;
		int act = GetPlayerActNumberForScaling();
		Flash();
		await PowerCmd.Apply<RitualPower>(Owner.Creature, act, Owner.Creature, null);
	}
}
