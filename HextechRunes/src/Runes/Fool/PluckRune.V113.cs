#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class PluckRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForArrow(null)];

	// 时机同原版火力掩护（CoveringFirePower）：AfterArrowShot，且射击者是持有者。原版 ArrowCmd.TryShootArrow
	// 没有可用箭时也派发该钩子（arrow 为 null，也不记战斗历史），这时并没有射出箭矢，不触发；
	// 火力掩护还要求来源是卡牌，这里不要求：遗物、能力等非卡牌来源射出的箭同样算“你发射箭矢”。
	// 抽牌发生在这一箭的攻击结算之前（射出 → 攻击 → 扎穿），与原版出牌途中抽牌（如拧下/Wrest）同一类时机。
	public override async Task AfterArrowShot(Player owner, ArrowSlot? arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (arrow == null || owner != Owner || Owner.Creature.IsDead || !IsFoolPlayer(Owner))
		{
			return;
		}

		decimal cards = DynamicVars.Cards.BaseValue;
		if (cards <= 0m)
		{
			return;
		}

		Flash();
		await CardPileCmd.Draw(context, cards, Owner, fromHandDraw: false);
	}
}
#endif
