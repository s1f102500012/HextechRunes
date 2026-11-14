#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.CardSelection;

namespace HextechRunes;

public sealed partial class PreludeRune
{
	// 原版 CombatManager.SetupPlayerTurn 的顺序：BeforeHandDraw → ModifyHandDraw（第一回合再取固有牌数的较大值、手牌上限的较小值）
	// → CardPileCmd.Draw(fromHandDraw: true)，三步用同一个 HookPlayerChoiceContext。
	// 照纯粹主义者的做法在那次起手抽牌的 ShouldDraw 里拦下并记下请求张数（HextechDrawRequest），
	// 原版紧接着同步调用本模型的 AfterPreventingDraw，在那里改为从抽牌堆选牌加入手牌。
	// 选牌照原版预见（ForegoneConclusionPower）：同一时机、同一个上下文，ShuffleIfNecessary → CardSelectCmd.FromCombatPile → CardPileCmd.Add 到手牌。
	// 不按抽牌结算（不触发抽到时、不记抽牌历史）；原版对每名玩家的起手准备各用一个上下文，选择期间放行其他玩家，
	// 选牌经原版选择同步传到各端。

	// BeforeHandDraw 记下的本次起手上下文；只在持有者自己战斗第一回合的起手里有效，用完即清。不保存：原版不在战斗中途存档。
	private PlayerChoiceContext? _pendingContext;

	// ShouldDraw 是查询钩子，每次查询都重写；原版拦下后同步紧接着调 AfterPreventingDraw，读到的就是这次真实请求。
	// 抽牌之外的模拟查询拿不到请求张数（为 0），不会被拦。
	private int _pendingDraws;

	public override Task BeforeCombatStart()
	{
		_pendingContext = null;
		_pendingDraws = 0;
		return Task.CompletedTask;
	}

	public override Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		_pendingContext = player == Owner
			&& IsOwnersFirstTurn
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner)
				? choiceContext
				: null;
		return Task.CompletedTask;
	}

	public override bool ShouldDraw(Player player, bool fromHandDraw)
	{
		_pendingDraws = player == Owner && fromHandDraw && _pendingContext != null && IsOwnersFirstTurn
			? HextechDrawRequest.GetRequestedCount(player)
			: 0;
		return _pendingDraws <= 0;
	}

	public override async Task AfterPreventingDraw()
	{
		int requested = _pendingDraws;
		PlayerChoiceContext? choiceContext = _pendingContext;
		_pendingDraws = 0;
		_pendingContext = null;
		if (requested <= 0
			|| choiceContext == null
			|| Owner.Creature.IsDead
			|| CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		Flash();
		await CardPileCmd.ShuffleIfNecessary(choiceContext, Owner);
		CardPile hand = PileType.Hand.GetPile(Owner);
		CardPile drawPile = PileType.Draw.GetPile(Owner);
		(int count, bool needsSelection) = GetCardsToAdd(requested, hand.Cards.Count, CardPile.MaxCardsInHand, drawPile.Cards.Count);
		if (count <= 0)
		{
			return;
		}

		IEnumerable<CardModel> chosen = needsSelection
			? await CardSelectCmd.FromCombatPile(choiceContext, drawPile, Owner, new CardSelectorPrefs(SelectionScreenPrompt, count))
			: drawPile.Cards.ToList();
		List<CardModel> cards = chosen
			.Where(card => card.Pile == drawPile)
			.Take(count)
			.ToList();
		if (cards.Count > 0)
		{
			await CardPileCmd.Add(cards, PileType.Hand);
		}
	}
}
#endif
