using MegaCrit.Sts2.Core.Saves;
#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.Save;
#endif

namespace HextechRunes;

/// <summary>
/// 对局流程侧（结束结算、游戏模式）的 0.111.0↔0.113.0 API 差异。共享代码按 0.111 的语义调用这里。
/// </summary>
internal static partial class HextechV113ApiCompat
{
#if STS2_113_OR_NEWER
	// 0.113.0 的 RunManager.OnEnded 返回 PostRunProgressUpdateResult，结算用的存档在 .run；
	// 同一局第二次调用被原版 HasRunEnded 守卫挡下，返回 default，.run 为 null。
	internal static SerializableRun? GetEndedRun(PostRunProgressUpdateResult result)
	{
		return result.run;
	}

	internal static bool HasRunEnded(RunManager runManager)
	{
		return runManager.HasRunEnded;
	}
#else
	// 0.111.0 及以前 OnEnded 每次都返回本次 ToSave 的结果；没有重复调用守卫（重复上报靠遥测按 RunId 去重）。
	internal static SerializableRun? GetEndedRun(SerializableRun result)
	{
		return result;
	}

	internal static bool HasRunEnded(RunManager runManager)
	{
		return false;
	}
#endif

	/// <summary>
	/// 本模组不支持的原版游戏模式返回原因（写进日志），支持的返回 null。
	/// 0.113.0 的极速（Blitz）模式每幕都从先古事件开始，离开事件与奖励界面时 RunManager.Proceed 直接为下一节点投票，
	/// 不经过地图，SetTravelEnabled 挡不住推进，海克斯选择会和进下一房间并发；所以本局不启用。
	/// 恐惧（Dread）沿用标准地图与先古流程，照常启用。
	/// </summary>
	internal static string? GetUnsupportedGameModeReason(GameMode gameMode)
	{
#if STS2_113_OR_NEWER
		if (gameMode == GameMode.Blitz)
		{
			return "Blitz auto-advances to the next map node without the map screen, so act selections cannot block travel";
		}
#endif
		return null;
	}
}
