using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static async Task EventRoomProceedAfterOriginal(Task original, EventRoomProceedState? state)
	{
		await original;

		// mod 延续体异常不能把原版 NEventRoom.Proceed 任务链打成 faulted(单端中断即联机分叉)。
		if (state is not EventRoomProceedState pending)
		{
			return;
		}

		try
		{
			await EventRoomProceedContinuation(pending);
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"EventRoomProceed continuation failed: {ex}");
		}
	}

	private static async Task EventRoomProceedContinuation(EventRoomProceedState state)
	{
		RunState runState = state.RunState;
		int actIndex = state.ActIndex;
		string eventId = state.EventId;
		if (!IsCurrentRun(runState))
		{
			HextechLog.Info("Mayhem", $"EventRoomProceed skip: run changed after proceed act={actIndex} event={eventId}");
			return;
		}

		HextechMayhemModifier modifier = GetOrRecoverMayhemModifier(runState, $"EventRoomProceed recovered missing modifier after proceed act={actIndex} event={eventId}");
		if (!modifier.IsActResolved(actIndex) && modifier.TryRecoverResolvedActsFromPlayerRelics(nameof(EventRoomProceedAfterOriginal)))
		{
			HextechEnemyUi.Refresh(modifier);
		}

		if (modifier.IsActResolved(actIndex))
		{
			HextechLog.Info("Mayhem", $"EventRoomProceed skip: act{actIndex} already resolved event={eventId}");
			return;
		}

		HextechLog.Info("Mayhem", $"EventRoomProceed: waiting for all ancient events before act{actIndex} selection event={eventId} mapOpen={NMapScreen.Instance?.IsOpen == true}");
		NMapScreen.Instance?.SetTravelEnabled(enabled: false);
		try
		{
			if (!await WaitForAllCurrentEventsFinished(runState, eventId))
			{
				// 换局时不在这里开选择；新局的选择由它自己的调度负责。
				return;
			}

			if (!IsCurrentRun(runState) || modifier.IsActResolved(actIndex))
			{
				HextechLog.Info("Mayhem", $"EventRoomProceed skip: run changed or act{actIndex} resolved after wait event={eventId}");
				return;
			}

			HextechLog.Info("Mayhem", $"EventRoomProceed: selecting act{actIndex} hex after all ancient events finished event={eventId} mapOpen={NMapScreen.Instance?.IsOpen == true}");
			await HextechRuneSelectionCoordinator.HandleActSelection(runState, modifier);
		}
		finally
		{
			if (IsCurrentRun(runState))
			{
				NMapScreen.Instance?.SetTravelEnabled(enabled: true);
			}
		}
	}

	private static bool TryGetPendingEventProceedSelection([NotNullWhen(true)] out EventRoomProceedState? state)
	{
		state = null;
		if (RunManager.Instance.DebugOnlyGetState() is not RunState runState
			|| runState.CurrentActIndex < 0
			|| runState.CurrentRoom is not EventRoom { CanonicalEvent: AncientEventModel ancientEvent }
			|| HextechMayhemModifier.IsGameModeUnsupportedForRun(runState))
		{
			return false;
		}

		if (HextechMayhemModifier.FindIn(runState)?.IsActResolved(runState.CurrentActIndex) == true)
		{
			return false;
		}

		state = new EventRoomProceedState(runState, runState.CurrentActIndex, ancientEvent.Id.Entry);
		return true;
	}

	/// <summary>
	/// 等所有玩家的当前事件都结束；只有换局才返回 false。等待过久只告警，不单端放弃：
	/// 超时由各端独立判定，一端放弃后另一端会在选择界面等它，而地图行进已被禁用，双方互相卡住。
	/// </summary>
	private static async Task<bool> WaitForAllCurrentEventsFinished(RunState runState, string eventId)
	{
		for (int frame = 0; IsCurrentRun(runState); frame++)
		{
			IReadOnlyList<EventModel> events = RunManager.Instance.EventSynchronizer.Events;
			int finishedCount = events.Count(static eventModel => eventModel.IsFinished);
			if (AreAllPlayerEventsFinished(runState, events, finishedCount))
			{
				HextechLog.Info("Mayhem", $"EventRoomProceed: required events finished event={eventId} count={events.Count} finished={finishedCount} reason=all-player-events waitedFrames={frame}");
				return true;
			}

			if (frame == RemoteEventsSlowWaitWarnFrames)
			{
				HextechLog.Warn("Mayhem", $"EventRoomProceed: still waiting for remote events after {RemoteEventsSlowWaitWarnFrames} frames event={eventId} finished={finishedCount}/{events.Count} players={runState.Players.Count}; keep waiting.");
			}
			else if (frame % RemoteEventsWaitLogIntervalFrames == 0)
			{
				HextechLog.Info("Mayhem", $"EventRoomProceed: waiting for remote events event={eventId} finished={finishedCount}/{events.Count} players={runState.Players.Count}");
			}

			await WaitOneFrame();
		}

		return false;
	}

	private static bool AreAllPlayerEventsFinished(RunState runState, IReadOnlyList<EventModel> events, int finishedCount)
	{
		return events.Count >= runState.Players.Count && finishedCount == events.Count;
	}

	[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.Proceed), new Type[0])]
	[HextechPatch("run.event-room-proceed", "事件房离开")]
	private static class EventRoomProceedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(out EventRoomProceedState? __state)
		{
			if (TryGetPendingEventProceedSelection(out __state))
			{
				EventRoomProceedState state = __state.Value;
				HextechLog.Info("Mayhem", $"EventRoomProceed begin: act={state.ActIndex} event={state.EventId} {DescribeCurrentEventState(state.RunState)}");
			}
		}

		[HarmonyPostfix]
		private static void Postfix(EventRoomProceedState? __state, ref Task __result)
		{
			__result = EventRoomProceedAfterOriginal(__result, __state);
		}
	}
}
