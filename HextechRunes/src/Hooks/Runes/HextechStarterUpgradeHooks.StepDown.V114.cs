#if STS2_114_OR_NEWER
namespace HextechRunes;

// 0.114.0 起原版 CardCmd.Downgrade 每次只降一级（魔法骑士等要清零的改走 CardCmd.RemoveAllUpgrades），
// 旧版本里把降级改成“只降一级”的补丁不再需要，降级幅度完全跟随原版。
internal static partial class HextechStarterUpgradeHooks
{
	internal static void DowngradeOneLevel(CardModel card)
	{
		CardCmd.Downgrade(card);
	}
}
#endif
