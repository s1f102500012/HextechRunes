using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

internal static class HextechSelectionCapstoneHooks
{
	// 原版开局地图房间在任何全屏界面（卡组等）关闭后都会重新打开地图，盖住还没选完的海克斯选择界面，
	// 玩家得再关一次地图才能回去。选择待定且地图本来没开时跳过这次重开，关闭卡组直接回到选择界面；
	// 从地图里打开卡组时地图一直开着，原方法本来就什么都不做，看地图的流程不受影响。
	[HarmonyPatch(typeof(NMapRoom), "ReopenMap")]
	[HextechPatch("ui.selection-capstone-return", "海克斯选择界面")]
	private static class ReopenMapPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix()
		{
			if (!HextechRuneSelectionScreen.ShouldReturnToSelectionAfterCapstone())
			{
				return true;
			}

			HextechLog.Info("Mayhem", "SelectionScreen: capstone closed, returning to pending selection instead of reopening map");
			return false;
		}
	}
}
