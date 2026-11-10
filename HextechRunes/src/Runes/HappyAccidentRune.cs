namespace HextechRunes;

public sealed class HappyAccidentRune : HextechRelicBase
{
	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，跨战斗累加、读档归零，
	// 改成每场清零会改变单机的随机结果，按现状保留；联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	private int _localStatusOrbOrdinal;

	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_localStatusOrbOrdinal = 0;
		}

		return base.BeforeCombatStart();
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbCount", 1m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsDefectPlayer(player);
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState == null)
		{
			return;
		}

		int statusCount = CountStatusCards(Owner.PlayerCombatState?.AllPiles.SelectMany(static pile => pile.Cards) ?? []);
		int orbCount = ResolveOrbCount(statusCount, DynamicVars["OrbCount"].IntValue);
		if (orbCount <= 0)
		{
			return;
		}

		Flash();
		for (int i = 0; i < orbCount; i++)
		{
			int orbOrdinal = ConsumeCombatProcOrdinal(ref _localStatusOrbOrdinal);
			OrbModel orb = HextechStableCombatSpawns.CreateOrb(
				(RunState)Owner.RunState,
				Owner,
				"happy-accident-exhaust-status-orb",
				orbOrdinal,
				Owner.Creature.CombatState.RoundNumber);
			await OrbCmd.Channel(choiceContext, orb, Owner);
		}
	}

	internal static int CountStatusCards(IEnumerable<CardModel> cards)
	{
		return cards.Count(static card => card.Type == CardType.Status);
	}

	internal static int ResolveOrbCount(int statusCount, int orbsPerStatus)
	{
		return Math.Max(0, statusCount) * Math.Max(0, orbsPerStatus);
	}
}
