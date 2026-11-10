namespace HextechRunes;

public sealed class TwiceThriceRune : HextechRelicBase
{
	private const int AttacksPerReplay = 3;

	// 旧版本存档兼容占位：原为单机本场攻击计数；现在单机与联机都读原版战斗完成历史，名称与类型须保留。
	// 原版不存战斗中途状态（读档从本场开头重进），战斗内计数本来就不需要随存档恢复。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedAttacksPlayedThisCombat
	{
		get => 0;
		set { }
	}

	public override bool ShowCounter => IsInLiveCombat;

	public override int DisplayAmount
	{
		get
		{
			if (IsCanonical)
			{
				return 0;
			}

			return GetAttacksPlayedThisCombat() % AttacksPerReplay;
		}
	}

	public override Task BeforeCombatStart()
	{
		InvokeDisplayAmountChanged();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		InvokeDisplayAmountChanged();
		return Task.CompletedTask;
	}

	// 计数口径同原版苦无（Kunai）：持有者攻击牌的每一次打出都计 1，含重放（PlayIndex > 0）与自动打出。
	// 原版 CardModel.GeneratePlayCount 在第一次打出前一次性算出总次数（Hook.ModifyCardPlayCount 按监听者顺序累加，
	// 0.111.0 没有 Late 版本），之后每次打出都推进计数。本张牌这一系列打出（c+1 … c+playCount）会跨过 3 的倍数时追加一次，
	// 追加的那次同样计数。限制：这里只能看到排在本符文之前的监听者累加后的次数——持有者身上的 Power 全部在遗物之前，
	// 同一玩家的遗物按获得顺序；比本符文晚获得的双刀流等 +1 在这里看不到，只在打出后推进计数。
	// 本钩子可能被同一次出牌重复求值（预测轮询），只读计数不改状态。
	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		if (!IsOwnedAttack(card))
		{
			return playCount;
		}

		return ShouldAddReplay(GetAttacksPlayedThisCombat(), playCount)
			? playCount + 1
			: playCount;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!IsOwnedAttack(cardPlay.Card))
		{
			return Task.CompletedTask;
		}

		InvokeDisplayAmountChanged();
		Flash();
		return Task.CompletedTask;
	}

	internal static bool ShouldAddReplay(int attacksPlayedBefore, int playCount)
	{
		return attacksPlayedBefore % AttacksPerReplay + playCount >= AttacksPerReplay;
	}

	// 单机与联机统一读原版战斗完成历史（CardPlayFinished），不维护本地计数。原版每次打出先记完成历史、
	// 再依次派发 AfterCardPlayed；一呼百应等排在本符文之前的监听者在自己的 AfterCardPlayed 里嵌套自动打出攻击牌时，
	// 外层那张已在历史里、而本符文的 AfterCardPlayed 还没执行。本地计数会少算外层这张，导致单机与联机追加次数不同。
	// 历史只含本场（原版在战斗结束与重置时清空），战斗外读到 0。
	private int GetAttacksPlayedThisCombat()
	{
		return CountOwnedAttackCardsPlayedFromHistory();
	}
}
