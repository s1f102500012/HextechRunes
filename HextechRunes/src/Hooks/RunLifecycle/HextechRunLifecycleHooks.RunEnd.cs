using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static void EnsureCombatHistoryBeforeLossSave(RunManager runManager, bool isVictory)
	{
		if (isVictory)
		{
			return;
		}

		try
		{
			// 只为启用了海克斯的跑局补历史:这是自家遭遇改写造成的缺口,普通跑局与第三方历史不碰。
			if (runManager.DebugOnlyGetState() is not RunState runState
				|| !HextechMayhemModifier.IsEnabledForRun(runState)
				|| runState.CurrentRoom is not CombatRoom combatRoom
				|| runState.CurrentMapPointHistoryEntry is not MapPointHistoryEntry mapPointHistory)
			{
				return;
			}

			MapPointRoomHistoryEntry? roomHistory = mapPointHistory.Rooms.Count > 0 ? mapPointHistory.Rooms[^1] : null;
			if (roomHistory == null || !roomHistory.RoomType.IsCombatRoom())
			{
				roomHistory = new MapPointRoomHistoryEntry
				{
					RoomType = combatRoom.RoomType,
					ModelId = combatRoom.ModelId
				};
				mapPointHistory.Rooms.Add(roomHistory);
				HextechLog.Warn("Mayhem", $"Added missing combat room history before loss save: encounter={combatRoom.ModelId.Entry}");
			}

			if (roomHistory.ModelId == null)
			{
				roomHistory.ModelId = combatRoom.ModelId;
				HextechLog.Warn("Mayhem", $"Filled missing combat encounter id before loss save: encounter={combatRoom.ModelId.Entry}");
			}

			if (roomHistory.MonsterIds == null)
			{
				roomHistory.MonsterIds = [];
			}

			if (roomHistory.MonsterIds.Count == 0)
			{
				foreach ((MonsterModel monster, _) in combatRoom.Encounter.MonstersWithSlots)
				{
					roomHistory.MonsterIds.Add(monster.Id);
				}
				HextechLog.Warn("Mayhem", $"Filled missing combat monster ids before loss save: encounter={combatRoom.ModelId.Entry} count={roomHistory.MonsterIds.Count}");
			}
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"Failed to sanitize combat history before loss save: {ex}");
		}
	}

	/// <summary>
	/// 跑局结束后的本机清理与遥测。清理全部幂等，同一局重复结束时照常执行；
	/// <paramref name="endedRun"/> 为 null 表示原版判定本局已经结束过（0.113.0 起的重复调用守卫），不再上报遥测。
	/// </summary>
	internal static void HandleRunEnded(RunState? runState, SerializableRun? endedRun, bool isVictory)
	{
		ResetTransientRunState();
		// 生物节点映射只供本机视觉查找，下一场战斗的 NCombatRoom._Ready 会重建；跑局结束时先放掉上一场的生物引用。
		HextechCreatureNodeRegistry.Clear();
		HextechSelectionProgress.Reset([]);
		HextechGoldrendSync.ClearRun(runState);
		if (endedRun == null)
		{
			HextechLog.Info("Mayhem", "Run end reported again without a save; cleanup repeated, telemetry skipped");
			return;
		}

		HextechTelemetry.OnRunEnded(runState, endedRun, isVictory);
	}

	[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), typeof(bool))]
	[HextechPatch("run.end", "跑局结束统计")]
	private static class RunEndedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(RunManager __instance, bool isVictory, out RunState? __state)
		{
			// 0.113.0 起原版第二次调用直接返回、不再 ToSave，不补历史；旧版本没有这道守卫，恒为 false。
			if (!HextechV113ApiCompat.HasRunEnded(__instance))
			{
				EnsureCombatHistoryBeforeLossSave(__instance, isVictory);
			}

			__state = __instance.DebugOnlyGetState();
		}

		[HarmonyPostfix]
		private static void Postfix(
			RunState? __state,
			bool isVictory,
#if STS2_113_OR_NEWER
			MegaCrit.Sts2.Core.Entities.Save.PostRunProgressUpdateResult __result)
#else
			SerializableRun __result)
#endif
		{
			HandleRunEnded(__state, HextechV113ApiCompat.GetEndedRun(__result), isVictory);
		}
	}
}
