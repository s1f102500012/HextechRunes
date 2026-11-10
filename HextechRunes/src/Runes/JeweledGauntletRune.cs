namespace HextechRunes;

public sealed class JeweledGauntletRune : HextechRelicBase
{
	private const int ReplayChancePercent = 33;

	private int _replayRollsThisCombat;

	// 按 card 实例分槽存放待定判定,而非单一共享槽位:引擎/UI 可能在同一张牌的
	// ModifyCardPlayCount 与真实打出之间,针对手牌里其它牌再次调用 ModifyCardPlayCount
	// (出牌次数预测类轮询),单一槽位会被覆盖导致认错牌。
	private readonly Dictionary<CardModel, bool> _pendingReplayRolls = new();

	public override Task BeforeCombatStart()
	{
		ResetReplayRollState();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetReplayRollState();
		return Task.CompletedTask;
	}

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		if (card.Owner != Owner)
		{
			_pendingReplayRolls.Remove(card);
			return playCount;
		}

		// 这里只 peek 不消费:本钩子可能被引擎/UI 针对同一次出牌重复求值(预测轮询只在
		// 本地端发生),在这里推进联机共享序号会让各端推进次数不一致→稳定随机结果分叉而断线。
		int ordinal = _replayRollsThisCombat;
		bool shouldReplay = HextechStableRandom.PercentChance(
			(RunState)Owner.RunState,
			ReplayChancePercent,
			"jeweled-gauntlet-replay",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState?.RoundNumber.ToString() ?? "-1",
			ordinal.ToString(),
			TargetKey(target),
			HextechStableRandom.CardKey(card));
		_pendingReplayRolls[card] = shouldReplay;
		return shouldReplay ? playCount + 1 : playCount;
	}

	// 消费点选 BeforeCardPlayed(PlayIndex==0)而非 AfterModifyingCardPlayCount:后者只对
	// "改变了次数"的模型回调,判定失败(67%)时不会触发,序号将冻结、同一(卡,目标,回合)的
	// 稳定随机结果永远不变。真实出牌每次恰好经过一次 PlayIndex==0,且与判定成败无关,
	// 消费节奏与旧版(每次真实出牌推进一位)一致;轮询产生的残留 pending 不会走到这里,无副作用。
	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner != Owner || cardPlay.PlayIndex != 0)
		{
			return Task.CompletedTask;
		}

		if (_pendingReplayRolls.Remove(cardPlay.Card, out bool shouldReplay))
		{
			ConsumeCombatProcOrdinal(ref _replayRollsThisCombat);
			if (shouldReplay)
			{
				Flash();
				HextechCombatVfx.JeweledGauntletSparkle(cardPlay.Card);
			}
		}

		return Task.CompletedTask;
	}

	private static string TargetKey(Creature? target)
	{
		return target?.CombatId?.ToString() ?? target?.Side.ToString() ?? "none";
	}

	private void ResetReplayRollState()
	{
		_replayRollsThisCombat = 0;
		_pendingReplayRolls.Clear();
	}
}
