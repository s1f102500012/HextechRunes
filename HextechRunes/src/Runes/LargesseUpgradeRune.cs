namespace HextechRunes;

/// <summary>
/// 升级：慷慨捐助（仅储君、仅联机）：持有者打出慷慨捐助为其他玩家加入手牌的无色牌，在本回合可以免费打出。
/// 原版在每一端都执行同一次 OnPlay，用共享随机数生成这张归接收方所有的牌并调 AfterCardGeneratedForCombat；
/// 这里在同一个钩子里对它调用原版 SetToFreeThisTurn，因此免费状态在各端同时成立，不需要额外同步。
/// 只认这次打出期间、持有者为目标玩家生成的第一张牌，避免把生成链里其他效果补出的牌也算进来。
/// </summary>
public sealed class LargesseUpgradeRune : CardUpgradeRuneBase<Largesse>
{
	// 正在结算的那次慷慨捐助与它的目标玩家；纯战斗内时序状态，不存档。
	private CardModel? _pendingLargesse;
	private Player? _pendingRecipient;

	protected override bool IsAvailableForCharacter(Player player) => IsNetworkMultiplayer() && IsRegentPlayer(player);

	public override Task BeforeCombatStart()
	{
		ClearPending();
		return Task.CompletedTask;
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (cardPlay.Card is Largesse && cardPlay.Card.Owner == Owner && cardPlay.Target?.Player is Player recipient)
		{
			_pendingLargesse = cardPlay.Card;
			_pendingRecipient = recipient;
		}

		return Task.CompletedTask;
	}

	public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
	{
		if (_pendingRecipient == null
			|| creator != Owner
			|| card.Owner != _pendingRecipient
			|| card.Pile?.Type != PileType.Hand)
		{
			return Task.CompletedTask;
		}

		ClearPending();
		card.SetToFreeThisTurn();
		Flash();
		return Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card == _pendingLargesse)
		{
			ClearPending();
		}

		return Task.CompletedTask;
	}

	private void ClearPending()
	{
		_pendingLargesse = null;
		_pendingRecipient = null;
	}
}
