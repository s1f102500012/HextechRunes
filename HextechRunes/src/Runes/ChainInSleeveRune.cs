namespace HextechRunes;

public sealed class ChainInSleeveRune : HextechRelicBase
{
	private const int CanonicalShivsNeeded = 3;

	private int _shivsPlayedThisCombat;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedShivsPlayedThisCombat
	{
		get => CountOwnedShivCardsPlayedFromHistory() % ShivsNeeded;
		set
		{
			_shivsPlayedThisCombat = Math.Max(0, value) % ShivsNeeded;
			InvokeDisplayAmountChanged();
		}
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

			int shivsNeeded = ShivsNeeded;
			int remainder = CountOwnedShivCardsPlayedFromHistory() % shivsNeeded;
			return remainder == 0 ? shivsNeeded : shivsNeeded - remainder;
		}
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("ShivsNeeded", CanonicalShivsNeeded),
		new CardsVar(1)
	];

	private int ShivsNeeded => DynamicVars["ShivsNeeded"].IntValue;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Shiv>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		ResetCounter();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetCounter();
		return Task.CompletedTask;
	}

	// 单机与联机都按原版战斗完成历史推进：原版每次打出先记完成历史、再依次派发 AfterCardPlayed，
	// 排在本符文之前的监听者嵌套自动打出小刀时，外层那张已在历史里、本符文的 AfterCardPlayed 还没执行。
	// 若单机用本地计数，奖励会晚到外层那次才发，与联机（嵌套那次就发）时点不同。
	// _shivsPlayedThisCombat 只记已结算到的历史张数；Late 钩子兜底补结算，重复调用不会重复发放。
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (IsCountedShivPlay(cardPlay))
		{
			await ResolveShivProgressFromHistory();
		}
	}

	public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (IsCountedShivPlay(cardPlay))
		{
			await ResolveShivProgressFromHistory();
		}
	}

	private async Task ResolveShivProgressFromHistory()
	{
		if (Owner.Creature.IsDead)
		{
			return;
		}

		int shivsPlayed = CountOwnedShivCardsPlayedFromHistory();
		int previousShivsPlayed = _shivsPlayedThisCombat;
		if (shivsPlayed <= previousShivsPlayed)
		{
			return;
		}

		_shivsPlayedThisCombat = shivsPlayed;
		await ResolveShivRewards(previousShivsPlayed, shivsPlayed);
	}

	private async Task ResolveShivRewards(int previousShivsPlayed, int currentShivsPlayed)
	{
		InvokeDisplayAmountChanged();
		int rewards = CountThresholdCrossings(previousShivsPlayed, currentShivsPlayed, ShivsNeeded);
		if (rewards <= 0)
		{
			return;
		}

		Flash();
		await AddShivRewardCards(rewards * DynamicVars.Cards.IntValue);
	}

	// 计数口径同原版苦无（Kunai）：持有者小刀的每一次打出都计 1，含重放（PlayIndex > 0）与自动打出。
	private bool IsCountedShivPlay(CardPlay cardPlay)
	{
		return cardPlay.Card.Owner == Owner
			&& HextechKnifeHelper.IsShivLike(cardPlay.Card, Owner);
	}

	// 历史只含本场（原版在战斗结束与重置时清空），战斗外读到 0。
	private int CountOwnedShivCardsPlayedFromHistory()
	{
		return HextechCombatHistoryHelper.CountOwnedCardsPlayed(
			Owner,
			card => HextechKnifeHelper.IsShivLike(card, Owner));
	}

	private async Task AddShivRewardCards(int count)
	{
		if (Owner.GetRelic<BigKnifeRune>() != null)
		{
			await AddCardCopiesToCombatHand<SovereignBlade>(count, HextechKnifeHelper.ConfigureBigKnifeBlade);
			return;
		}

		await AddCardCopiesToCombatHand<Shiv>(count);
	}

	private void ResetCounter()
	{
		_shivsPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();
	}
}
