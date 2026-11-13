#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class BittersweetRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<Sadness>()];

	// "生成悲伤"与原版万事无忧（AllIsWellPower）、肥料（Fertilizer）同一口径：Hook.AfterCardGeneratedForCombat，
	// 生成者是持有者、牌是悲伤；另要求悲伤进入持有者自己的牌堆。原版一次生成多张时逐张派发该钩子，
	// 所以每张各抽一次；CardCmd.Transform 在战斗牌堆里变出的悲伤（如哀悼一击）同样派发，按原版一并计入。
	// 不会循环：抽牌只派发 AfterCardDrawn，原版生成悲伤的入口都在出牌或抽牌前（BeforeHandDraw），没有"抽到时生成悲伤"的效果。
	public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
	{
		if (!IsOwnSadnessGeneration(card, creator))
		{
			return;
		}

		Flash();
		await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), DynamicVars.Cards.BaseValue, Owner, fromHandDraw: false);
	}

	internal bool IsOwnSadnessGeneration(CardModel card, Player? creator)
	{
		return creator == Owner
			&& card is Sadness
			&& card.Owner == Owner
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner);
	}
}
#endif
