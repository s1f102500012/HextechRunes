namespace HextechRunes;

public sealed class RallyingCallRune : HextechRelicBase
{
	private bool _playingMatches;

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (_playingMatches || !IsOwnedCard(cardPlay.Card)
			|| Owner.PlayerCombatState == null || Owner.Creature.IsDead
			|| Owner.Creature.CombatState == null || CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		// 只取抽牌堆(手牌里的同名牌不连带打出)。模型 ID 不受语言、升级后缀或附魔显示名影响;
		// 快照和连锁 guard 防止打出途中洗回抽牌堆的牌重复入队。
		CardModel[] matches = SnapshotMatches(cardPlay.Card, PileType.Draw.GetPile(Owner).Cards);
		if (matches.Length == 0)
		{
			return;
		}

		_playingMatches = true;
		try
		{
			Flash();
			foreach (CardModel card in matches)
			{
				if (CombatManager.Instance.IsOverOrEnding || Owner.Creature.IsDead
					|| Owner.Creature.CombatState == null)
				{
					break;
				}
				// 前一张牌可能已经抽走、消耗或打出后面的牌；仅继续处理仍在抽牌堆里的牌。
				if (card.Owner != Owner || card.Pile?.Type != PileType.Draw)
				{
					continue;
				}

				Creature? target = cardPlay.Target;
				if (card.TargetType == TargetType.AnyEnemy)
				{
					target = target != null && Owner.Creature.CombatState.HittableEnemies.Contains(target)
						? target
						: HextechRuneTargeting.FirstHittableEnemy(Owner.Creature.CombatState);
					if (target == null)
					{
						break;
					}
				}
				else if (card.TargetType != TargetType.AnyAlly)
				{
					target = null;
				}

				// 原版自动打牌检查无法打出、Hook 限制与目标，不以需要支付费用的 CanPlay 筛掉候选。
				await HextechAutoPlayHelper.AutoPlayOrMoveToResultPile(choiceContext, card, target);
			}
		}
		finally
		{
			_playingMatches = false;
		}
	}

	internal static CardModel[] SnapshotMatches(CardModel source, IEnumerable<CardModel> candidates)
	{
		return candidates.Where(card => !ReferenceEquals(card, source)
			&& card.Owner == source.Owner && card.Id == source.Id).Distinct().ToArray();
	}
}
