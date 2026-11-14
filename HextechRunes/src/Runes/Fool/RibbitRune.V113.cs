#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class RibbitRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<WahChoKi>()
	];

	// 同攻防一体：按牌组顺序把基础打击与基础防御两两配对，每对移除后换成 {Cards} 张哇 啾 叽，落单的不动。
	public override async Task AfterObtained()
	{
		if (!IsFoolPlayer(Owner))
		{
			return;
		}

		List<CardModel> strikes = Owner.Deck.Cards
			.Where(static card => card.IsBasicStrikeOrDefend && card.Type == CardType.Attack)
			.ToList();
		List<CardModel> defends = Owner.Deck.Cards
			.Where(static card => card.IsBasicStrikeOrDefend && card.Type == CardType.Skill)
			.ToList();
		int pairs = Math.Min(strikes.Count, defends.Count);
		if (pairs <= 0)
		{
			return;
		}

		Flash();
		for (int i = 0; i < pairs; i++)
		{
			await CardPileCmd.RemoveFromDeck(strikes[i], showPreview: false);
			await CardPileCmd.RemoveFromDeck(defends[i], showPreview: false);
		}

		await AddCardCopiesToDeckOrHand<WahChoKi>(pairs * DynamicVars.Cards.IntValue);
	}
}
#endif
