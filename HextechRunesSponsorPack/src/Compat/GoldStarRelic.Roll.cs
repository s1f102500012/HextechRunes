using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;

namespace HextechRunesSponsorPack;

// 0.108.0 起 PotionRewardOdds.Roll 为 (player, RoomType) 两参;按普通战斗房间掷药水掉落。
public sealed partial class GoldStarRelic
{
	private static bool RollPotionReward(Player owner)
	{
		return owner.PlayerOdds.PotionReward.Roll(owner, RoomType.Monster);
	}
}
