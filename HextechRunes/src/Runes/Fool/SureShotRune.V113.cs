#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class SureShotRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>()
	];

	// 口径同原版合股线（Twine）：AfterArrowDislodged 且箭属于持有者。击杀造成的松脱（ArrowCmd.ImpaleTarget 里
	// 目标已死就松脱、敌人死亡时的全部松脱）同样派发该钩子，一并计入；被摧毁的箭原版不派发该钩子，不触发。
	// 抽牌不派发松脱，不会自我循环。
	public override async Task AfterArrowDislodged(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (arrow.owner != Owner || Owner.Creature.IsDead || !IsFoolPlayer(Owner))
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
