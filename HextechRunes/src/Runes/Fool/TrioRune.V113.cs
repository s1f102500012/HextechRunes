#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class TrioRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	// 重放口径同塔罗牌桌布：持有者的牌在原版 GeneratePlayCount 里加次数，自动打出同样经过这里。
	// 原版每次出牌只在开始时求一次打出次数（此时这张牌还没进出牌历史），重放出来的几次共用这个次数，不再求值，
	// 所以凑齐第三种旋律的那张牌自己不重放、重放也不会连锁。旋律种类数口径同弦外之音（含开嗓 / 引吭高歌直接给予的）。
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
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner)
			&& HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			&& MelodyCmd.MelodiesThisTurn(Owner) >= HextechGrantedMelodies.MelodyTags.Length;
	}
}
#endif
