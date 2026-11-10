namespace HextechRunes;

public sealed class SellOffRune : HextechRelicBase
{
	private bool _autoPlaying;
	private int _autoPlayTargetsThisCombat;
	private readonly Queue<CardModel> _pendingDiscardedCards = new();

	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，按现状保留；
	// 联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_autoPlayTargetsThisCombat = 0;
		}

		return base.BeforeCombatStart();
	}

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromKeyword(CardKeyword.Sly),
		HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
	{
		if (ShouldExhaustUnplayableDiscard(card))
		{
			Flash();
			await CardCmd.Exhaust(choiceContext, card);
			return;
		}

		if (!CanAutoPlayDiscardedCard(card))
		{
			return;
		}

		_pendingDiscardedCards.Enqueue(card);
		if (_autoPlaying)
		{
			return;
		}

		_autoPlaying = true;
		try
		{
			while (_pendingDiscardedCards.TryDequeue(out CardModel? discardedCard))
			{
				if (!CanAutoPlayDiscardedCard(discardedCard))
				{
					continue;
				}

				await AutoPlayDiscardedCard(choiceContext, discardedCard);
			}
		}
		finally
		{
			_pendingDiscardedCards.Clear();
			_autoPlaying = false;
		}
	}

	// 带“无法打出”关键词的诅咒牌和状态牌打不出去，改为直接消耗；能打出的状态牌不受影响。
	private bool ShouldExhaustUnplayableDiscard(CardModel card)
	{
		return !Owner.Creature.IsDead
			&& card.Owner == Owner
			&& card.Type is CardType.Curse or CardType.Status
			&& card.Keywords.Contains(CardKeyword.Unplayable)
			&& !card.IsSlyThisTurn
			&& card.Pile?.Type == PileType.Discard;
	}

	private bool CanAutoPlayDiscardedCard(CardModel card)
	{
		return !Owner.Creature.IsDead
			&& card.Owner == Owner
			&& card.Type is CardType.Attack or CardType.Skill or CardType.Power
			&& !card.IsSlyThisTurn;
	}

	private async Task AutoPlayDiscardedCard(PlayerChoiceContext choiceContext, CardModel card)
	{
		Flash();
		card.ExhaustOnNextPlay = true;
		if (card.Pile?.Type != PileType.Hand)
		{
			await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, this, skipVisuals: true);
		}

		HextechCombatState combatState = Owner.Creature.CombatState!;
		int targetOrdinal = ConsumeCombatProcOrdinal(nameof(SellOffRune), ref _autoPlayTargetsThisCombat);
		Creature? target = RequiresEnemyTarget(card)
			? HextechRuneTargeting.PickRandomHittableEnemy(
				Owner,
				combatState,
				"sell-off",
				combatState.RoundNumber.ToString(),
				targetOrdinal.ToString(),
				card.Id.Entry)
			: null;
		await HextechAutoPlayHelper.AutoPlayOrMoveToResultPile(choiceContext, card, target);
	}

	private static bool RequiresEnemyTarget(CardModel card)
	{
		return card.TargetType is TargetType.AnyEnemy;
	}
}
