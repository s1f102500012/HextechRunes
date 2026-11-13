#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class TarotClothRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Tarot)];

	// 同水晶球：原版 TeaOfDiscourtesy 也在 BeforeCombatStart 往抽牌堆加牌。每场只在这里加一次，序号固定为 0。
	public override Task BeforeCombatStart()
	{
		if (Owner.Creature.IsDead || !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		Flash();
		return HextechTarotCardGeneration.AddRandomTarotCardsToDrawPile(
			Owner,
			DynamicVars.Cards.IntValue,
			"tarot-cloth",
			HextechStableRandom.PlayerKey(Owner),
			"turn",
			Owner.PlayerCombatState?.TurnNumber.ToString(),
			"ordinal",
			"0");
	}

	// 重放口径同本模组其他重放海克斯（扉八分钱、思维覆写）：持有者的牌在原版 GeneratePlayCount 里加次数。
	// 原版出牌的去向（消耗/弃牌）在加次数之前就已定下，重放不会让消耗失效；自动打出同样经过这里，与其他重放海克斯一致。
	// 本钩子可能被同一次出牌重复求值，只读不改状态。
	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		return ShouldReplay(card) ? playCount + DynamicVars["Replays"].IntValue : playCount;
	}

	public override Task AfterModifyingCardPlayCount(CardModel card)
	{
		if (ShouldReplay(card))
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	private bool ShouldReplay(CardModel card)
	{
		return card.Owner == Owner
			&& IsFoolPlayer(Owner)
			&& HextechTarotCardGeneration.IsTarotCard(card);
	}
}
#endif
