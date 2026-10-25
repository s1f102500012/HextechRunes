namespace HextechRunes;

public sealed class MindToMatterRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new MaxHpVar(2m)
	];

	// 与原版幸运鱼同一判定：自己的卡牌落入牌组时触发。
	public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
	{
		if (card.Pile?.Type != PileType.Deck || card.Owner != Owner)
		{
			return;
		}

		Flash();
		await CreatureCmd.GainMaxHp(Owner.Creature, DynamicVars.MaxHp.BaseValue);
	}
}
