namespace HextechRunes;

public sealed class ArchmageRune : HextechRelicBase
{
	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，跨战斗累加、读档归零，
	// 改成每场清零会改变单机的随机结果，按现状保留；联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	private int _localFreeCardRollOrdinal;

	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_localFreeCardRollOrdinal = 0;
		}

		return base.BeforeCombatStart();
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("ChancePercent", 33m)
	];

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!CombatManager.Instance.IsInProgress
			|| !IsOwnedSkill(cardPlay.Card)
			|| !RollTrigger(cardPlay.Card, out int rollOrdinal)
			|| PickCardToMakeFree(cardPlay.Card, rollOrdinal) is not CardModel card)
		{
			return Task.CompletedTask;
		}

		card.SetToFreeThisTurn();
		Flash();
		return Task.CompletedTask;
	}

	private bool RollTrigger(CardModel sourceCard, out int rollOrdinal)
	{
		rollOrdinal = ConsumeCombatProcOrdinal(nameof(ArchmageRune), ref _localFreeCardRollOrdinal);
		return HextechStableRandom.PercentChance(
			(RunState)Owner.RunState,
			DynamicVars["ChancePercent"].IntValue,
			"archmage-free-card",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState?.RoundNumber.ToString() ?? "-1",
			rollOrdinal.ToString(),
			HextechStableRandom.CardKey(sourceCard));
	}

	private CardModel? PickCardToMakeFree(CardModel sourceCard, int rollOrdinal)
	{
		return HextechFreeCardPicker.Pick(
			PileType.Hand.GetPile(Owner).Cards,
			(candidates, tier) => PickFromCandidates(candidates, sourceCard, rollOrdinal, tier));
	}

	private CardModel? PickFromCandidates(IReadOnlyList<CardModel> candidates, CardModel sourceCard, int rollOrdinal, string tier)
	{
		int index = HextechStableRandom.Index(
			(RunState)Owner.RunState,
			candidates.Count,
			"archmage-pick-card",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState?.RoundNumber.ToString() ?? "-1",
			rollOrdinal.ToString(),
			HextechStableRandom.CardKey(sourceCard),
			tier,
			HextechStableRandom.CardPileKey(candidates));
		return candidates[index];
	}
}
