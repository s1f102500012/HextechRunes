using MegaCrit.Sts2.Core.Runs;

namespace HextechRunesSponsorPack;

/// <summary>
/// 拓展包用到的 0.111.0↔0.113.0 原版差异。共享代码按 0.111 的语义调用这里。
/// </summary>
internal static class SponsorV113ApiCompat
{
	/// <summary>
	/// 本体不启用的原版游戏模式返回 true，口径与本体 <c>HextechV113ApiCompat.GetUnsupportedGameModeReason</c> 一致
	/// （本体那份是 internal，拓展包只走公开 API，所以这里单独判断）：0.113.0 的极速（Blitz）模式离开奖励界面时
	/// <c>RunManager.Proceed</c> 直接推进到下一节点、不打开地图，拓展包不能在这个交接点插入房间。
	/// </summary>
	internal static bool IsGameModeUnsupported(IRunState runState)
	{
#if STS2_113_OR_NEWER
		return runState.GameMode == GameMode.Blitz;
#else
		return false;
#endif
	}
}
