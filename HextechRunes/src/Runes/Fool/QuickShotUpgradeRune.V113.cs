#if STS2_113_OR_NEWER
namespace HextechRunes;

// 获得时补 1 张快速射击、悬浮提示由基类提供。
public sealed partial class QuickShotUpgradeRune : CardUpgradeRuneBase<QuickShot>
{
	protected override bool IsAvailableForCharacter(Player player) => IsFoolPlayer(player);

	// 同熔融之拳、永恒铠甲：AfterCardPlayed 且牌归持有者（队友打出的不算）。原版每次打出（含重放）各派发一次，
	// 所以重放的快速射击也各抽一次；没有可用箭时打出（原版照常结算出牌）同样算打出。
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!IsOwnQuickShotPlay(cardPlay))
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

	internal bool IsOwnQuickShotPlay(CardPlay cardPlay)
	{
		return cardPlay.Card is QuickShot
			&& cardPlay.Card.Owner == Owner
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner);
	}
}
#endif
