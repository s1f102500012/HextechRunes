#if STS2_113_OR_NEWER
namespace HextechRunes;

// 获得时补 1 张歌之道由基类提供。
public sealed partial class SongPathUpgradeRune : CardUpgradeRuneBase<SongPath>
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		.. base.ExtraHoverTips,
		HoverTipFactory.FromPower<SongPathPower>(),
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	protected override bool IsAvailableForCharacter(Player player) => IsFoolPlayer(player);

	// 原版 SongPathPower 只在打出的牌带啾、且是本回合第一张开始结算的带啾牌（MelodyCmd.IsFirstPlayThisTurn）时给格挡。
	// 这里用同一判定补上哇、叽：每回合每种旋律各一次，一张牌带来几种就结算几次；啾仍由原版自己结算，不重复。
	// 数值读能力层数、命令照原版（无力格挡，没有来源牌）。持有者没有歌之道能力时什么都不做。
	// 引吭高歌给予的旋律由 SingOutRune 在回合开始时按能力列表结算（它认本海克斯）；开嗓是"拥有"，不结算。
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		int melodies = CountUpgradedMelodiesGained(cardPlay);
		for (int i = 0; i < melodies; i++)
		{
			if (Owner.Creature.IsDead || Owner.Creature.GetPower<SongPathPower>() is not { } power)
			{
				return;
			}

			Flash();
			await CreatureCmd.GainBlock(Owner.Creature, power.Amount, ValueProp.Unpowered, null);
		}
	}

	internal int CountUpgradedMelodiesGained(CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner != Owner
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner)
			|| Owner.Creature.GetPower<SongPathPower>() == null)
		{
			return 0;
		}

		return HextechGrantedMelodies.MelodyTags.Count(tag => tag != CardTag.Cho
			&& cardPlay.Card.Tags.Contains(tag)
			&& MelodyCmd.IsFirstPlayThisTurn(cardPlay, tag));
	}

	// 引吭高歌结算给予旋律时用：持有本海克斯（且是愚者）时任意旋律都触发歌之道。
	internal static bool UpgradesAnyMelody(Player owner)
	{
		return owner.GetRelic<SongPathUpgradeRune>() != null && HextechPlayerContextHelper.IsFoolPlayer(owner);
	}
}
#endif
