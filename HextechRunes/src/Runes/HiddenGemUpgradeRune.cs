namespace HextechRunes;

public sealed class HiddenGemUpgradeRune : CardUpgradeRuneBase<HiddenGem>
{
	internal const PileType ReplayTargetPile = PileType.Hand;

	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，跨战斗累加、读档归零，
	// 改成每场清零会改变单机的随机结果，按现状保留；联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	private int _localUpgradedPlayOrdinal;

	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_localUpgradedPlayOrdinal = 0;
		}

		return base.BeforeCombatStart();
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return true;
	}

	internal static bool ShouldUseUpgradedPlay(CardModel card)
	{
		return card is HiddenGem && card.Owner?.GetRelic<HiddenGemUpgradeRune>() != null;
	}

	internal static async Task PlayUpgraded(PlayerChoiceContext choiceContext, HiddenGem card, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);

		List<CardModel> drawCards = PileType.Draw.GetPile(card.Owner).Cards.ToList();
		if (drawCards.Count == 0)
		{
			return;
		}

		List<CardModel> candidates = drawCards.Where(IsEligibleReplayTarget).ToList();
		if (candidates.Count == 0)
		{
			return;
		}

		List<CardModel> preferred = candidates
			.Where(static candidate => candidate.Type is CardType.Attack or CardType.Skill or CardType.Power)
			.ToList();
		IEnumerable<CardModel> pool = preferred.Count == 0 ? candidates : preferred;
		HiddenGemUpgradeRune? rune = card.Owner.GetRelic<HiddenGemUpgradeRune>();
		if (rune == null)
		{
			return;
		}

		int ordinal = rune.ConsumeCombatProcOrdinal(ref rune._localUpgradedPlayOrdinal);
		CardModel selected = HextechStableRandom.Pick(
			pool,
			(RunState)card.Owner.RunState,
			HextechStableRandom.CardKey,
			"hidden-gem-upgrade-play",
			HextechStableRandom.PlayerKey(card.Owner),
			card.Owner.Creature.CombatState?.RoundNumber.ToString() ?? "-1",
			ordinal.ToString(),
			HextechStableRandom.CardKey(card),
			HextechStableRandom.CardPileKey(drawCards));
		selected.BaseReplayCount += card.DynamicVars["Replay"].IntValue;
		rune.Flash();
		await CardPileCmd.Add(selected, ReplayTargetPile);
	}

	internal static bool IsEligibleReplayTarget(CardModel candidate)
	{
		return !candidate.Keywords.Contains(CardKeyword.Unplayable)
			&& candidate.Type is not CardType.Status and not CardType.Curse
			&& candidate.GetEnchantedReplayCount() < 1;
	}

	[HarmonyPatch(typeof(HiddenGem), "OnPlay", typeof(PlayerChoiceContext), typeof(CardPlay))]
	[HextechPatch("rune.hidden-gem", "升级未掘宝石", Rune = typeof(HiddenGemUpgradeRune))]
	private static class HiddenGemPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(HiddenGem __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
		{
			if (!HiddenGemUpgradeRune.ShouldUseUpgradedPlay(__instance))
			{
				return true;
			}

			__result = HiddenGemUpgradeRune.PlayUpgraded(choiceContext, __instance, cardPlay);
			return false;
		}
	}
}
