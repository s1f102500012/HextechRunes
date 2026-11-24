using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace HextechRunes;

/// <summary>
/// 开幕事件（第一幕的涅奥/替换涅奥的先古）之后的本幕选择。两条触发路径，同一幕只会开始一次选择：
/// <list type="number">
/// <item>主触发（各端本机时点）：本幕未决时观察开幕房，等到本机玩家已离开事件（本机地图打开过且可以行进，
/// 与原版点"继续"后的状态相同；之后再关上地图也算）、所有玩家的事件都报告完成，在选第一个节点之前开始选择；
/// 不依赖玩家经由哪个界面方法离开事件。等待期间不禁用地图行进，只有选择真正开始后才禁用到选择结束，
/// 避免"禁用行进又永远等不到条件"。</item>
/// <item>兜底关口（各端同步）：条件一直不满足、玩家已经投票出发时，进入本幕第一个非事件、非地图房之前，
/// 在 <see cref="HextechMayhemModifier.BeforeRoomEntered"/> 里等本幕选完（见 <see cref="ResolveFirstActSelectionBeforeRoomAsync"/>）。</item>
/// </list>
/// 选择本身的各端同步（房主幕掷骰、等待远端、确认）由 <see cref="HextechRuneSelectionCoordinator"/> 完成。
/// </summary>
internal static partial class HextechRunLifecycleHooks
{
	// 正在观察开幕房的局与幕序号；同一局同一幕只启动一个观察任务。观察结束（选完、换局、离开开幕房）时清掉。
	private static (RunState Run, int ActIndex)? _openingEventWatch;

	internal enum PostOpeningEventStep
	{
		Wait,
		Select,
		StopRunEnded,
		StopResolved,
		StopLeftOpeningRoom
	}

	/// <summary>
	/// 主触发的判定（纯函数，便于测试）。顺序：换局/退出清理 → 本幕已决 → 已离开开幕位置（地图坐标或幕序号变化，
	/// 交给兜底关口）→ 本机玩家还没离开事件（没见过"地图打开且可以行进"）或正在行进时等待 → 所有玩家的事件都完成才开始。
	/// "已离开事件"是锁存量：先做完事件的玩家在等队友时把地图关上，队友做完后本端照样开始，不必再打开地图。
	/// 事件数少于玩家数（其他模组没有按原版为每名玩家建事件）视为未完成：继续等待，不会因此禁用行进。
	/// </summary>
	internal static PostOpeningEventStep DecidePostOpeningEventStep(
		bool runInProgress,
		bool stageResolved,
		bool leftOpeningLocation,
		bool localPlayerLeftEvent,
		bool traveling,
		int eventCount,
		int finishedEventCount,
		int playerCount)
	{
		if (!runInProgress)
		{
			return PostOpeningEventStep.StopRunEnded;
		}

		if (stageResolved)
		{
			return PostOpeningEventStep.StopResolved;
		}

		if (leftOpeningLocation)
		{
			return PostOpeningEventStep.StopLeftOpeningRoom;
		}

		if (!localPlayerLeftEvent || traveling)
		{
			return PostOpeningEventStep.Wait;
		}

		return AreAllPlayerEventsFinished(eventCount, finishedEventCount, playerCount)
			? PostOpeningEventStep.Select
			: PostOpeningEventStep.Wait;
	}

	internal static bool AreAllPlayerEventsFinished(int eventCount, int finishedEventCount, int playerCount)
	{
		return eventCount >= playerCount && finishedEventCount == eventCount;
	}

	/// <summary>
	/// 本幕选择推迟到开幕事件之后时调用（开局、进入开幕房、读档续跑、无尽循环）。重复调用只保留一个观察任务。
	/// 联机各端各自启动，开始选择的时刻各端不同，由选择协议对齐。
	/// </summary>
	private static void EnsurePostOpeningEventSelectionWatch(RunState runState, string reason)
	{
		int actIndex = runState.CurrentActIndex;
		if (_openingEventWatch is { } watch && ReferenceEquals(watch.Run, runState) && watch.ActIndex == actIndex)
		{
			HextechLog.Info("Mayhem", $"PostOpeningEvent: watch already active act={actIndex} reason={reason}");
			return;
		}

		_openingEventWatch = (runState, actIndex);
		HextechLog.Info("Mayhem", $"PostOpeningEvent: watching act{actIndex} opening event until map travel is available reason={reason} {DescribeCurrentEventState(runState)}");
		// 调用点都是同步回调或不能被玩家操作挡住的延续（开局/读档/进房），只能启动不能等待；异常由 RunSafely 记录。
		TaskHelper.RunSafely(WatchPostOpeningEventSelection(runState, actIndex, reason));
	}

	private static async Task WatchPostOpeningEventSelection(RunState runState, int actIndex, string reason)
	{
		int visitedCountAtStart = runState.VisitedMapCoords.Count;
		bool localPlayerLeftEvent = false;
		try
		{
			for (int frame = 0; ; frame++)
			{
				HextechMayhemModifier? modifier = HextechMayhemModifier.FindIn(runState);
				NMapScreen? map = NMapScreen.Instance;
				IReadOnlyList<EventModel> events = RunManager.Instance.EventSynchronizer.Events;
				int finishedCount = events.Count(static eventModel => eventModel.IsFinished);
				bool runInProgress = HextechRuneSelectionCoordinator.IsCurrentRunInProgress(runState);
				localPlayerLeftEvent |= map?.IsOpen == true && map.IsTravelEnabled;
				PostOpeningEventStep step = DecidePostOpeningEventStep(
					runInProgress,
					stageResolved: runInProgress && modifier?.IsActResolved(actIndex) == true,
					leftOpeningLocation: runInProgress
						&& (runState.CurrentActIndex != actIndex || runState.VisitedMapCoords.Count != visitedCountAtStart),
					localPlayerLeftEvent,
					traveling: map?.IsTraveling == true,
					events.Count,
					finishedCount,
					runState.Players.Count);
				switch (step)
				{
					case PostOpeningEventStep.StopRunEnded:
						HextechLog.Info("Mayhem", $"PostOpeningEvent: stop, run changed or cleaning up act={actIndex} frame={frame}");
						return;
					case PostOpeningEventStep.StopResolved:
						HextechLog.Info("Mayhem", $"PostOpeningEvent: stop, act{actIndex} already resolved frame={frame}");
						return;
					case PostOpeningEventStep.StopLeftOpeningRoom:
						HextechLog.Info("Mayhem", $"PostOpeningEvent: stop, travel already left the opening room before selection act={actIndex} frame={frame} room={runState.CurrentRoom?.GetType().Name ?? "null"}; BeforeRoomEntered gate will resolve it");
						return;
					case PostOpeningEventStep.Select:
						HextechLog.Info("Mayhem", $"PostOpeningEvent: selecting act{actIndex} hex after opening event; local player left the event (mapOpen={map?.IsOpen == true}), all events finished count={events.Count} finished={finishedCount} waitedFrames={frame} reason={reason}");
						await SelectAfterOpeningEvent(runState, modifier, actIndex);
						return;
				}

				if (frame == RemoteEventsSlowWaitWarnFrames)
				{
					HextechLog.Warn("Mayhem", $"PostOpeningEvent: still waiting after {RemoteEventsSlowWaitWarnFrames} frames act={actIndex} localLeftEvent={localPlayerLeftEvent} mapOpen={map?.IsOpen == true} travelEnabled={map?.IsTravelEnabled == true} finished={finishedCount}/{events.Count} players={runState.Players.Count}; travel stays enabled, BeforeRoomEntered gate will select before the first room.");
				}
				else if (frame % RemoteEventsWaitLogIntervalFrames == 0)
				{
					HextechLog.Info("Mayhem", $"PostOpeningEvent: waiting act={actIndex} localLeftEvent={localPlayerLeftEvent} mapOpen={map?.IsOpen == true} travelEnabled={map?.IsTravelEnabled == true} traveling={map?.IsTraveling == true} finished={finishedCount}/{events.Count} players={runState.Players.Count} {DescribeCurrentEventState(runState)}");
				}

				await WaitOneFrame();
			}
		}
		finally
		{
			if (_openingEventWatch is { } watch && ReferenceEquals(watch.Run, runState) && watch.ActIndex == actIndex)
			{
				_openingEventWatch = null;
			}
		}
	}

	private static async Task SelectAfterOpeningEvent(RunState runState, HextechMayhemModifier? existing, int actIndex)
	{
		HextechMayhemModifier modifier = existing
			?? GetOrRecoverMayhemModifier(runState, $"PostOpeningEvent recovered missing modifier act={actIndex}");
		if (!modifier.IsActResolved(actIndex) && modifier.TryRecoverResolvedActsFromPlayerRelics(nameof(SelectAfterOpeningEvent)))
		{
			HextechEnemyUi.Refresh(modifier);
		}

		if (modifier.IsActResolved(actIndex))
		{
			HextechLog.Info("Mayhem", $"PostOpeningEvent skip: act{actIndex} recovered as resolved");
			return;
		}

		// 只在选择进行期间禁用行进：本端玩家在选择界面里本来就投不了票，禁用是为了不让选择途中开始行进（原版清屏会关掉选择界面）。
		// 退出条件是选择结束（选完、换局或界面被关），不依赖其他玩家的事件状态。
		NMapScreen.Instance?.SetTravelEnabled(enabled: false);
		try
		{
			await HextechRuneSelectionCoordinator.HandleStageSelectionAfterInFlightAsync(
				runState,
				modifier,
				modifier.GetSelectionIndexForAct(actIndex),
				"PostOpeningEvent");
		}
		finally
		{
			if (IsCurrentRun(runState))
			{
				NMapScreen.Instance?.SetTravelEnabled(enabled: true);
			}
		}
	}

	/// <summary>
	/// 兜底关口：第一幕在进入第一个非事件、非地图房之前（原版各端 EnterRoom 里等待 Hook.BeforeRoomEntered）补完本幕选择。
	/// 先等本端进行中的选择收尾，再看是否仍未决。原版地图行进先淡出转场遮罩（RoomFadeOut 结束时拦截鼠标）再进房，
	/// 遮罩画在覆盖层之上；选择期间临时淡入、结束后淡出还原，原版进房后照常淡入。
	/// </summary>
	internal static async Task ResolveFirstActSelectionBeforeRoomAsync(RunState runState, HextechMayhemModifier modifier, int stageIndex, AbstractRoom room)
	{
		bool revealed = false;
		try
		{
			if (HextechRuneSelectionCoordinator.GetInFlightStageSelection(runState) == null
				&& (modifier.IsStageResolved(stageIndex) || !HextechRuneSelectionCoordinator.IsCurrentRunInProgress(runState)))
			{
				return;
			}

			revealed = await RevealRoomTransitionForSelectionAsync(room);
			await HextechRuneSelectionCoordinator.HandleStageSelectionAfterInFlightAsync(runState, modifier, stageIndex, "BeforeRoomEntered");
		}
		finally
		{
			if (revealed && HextechRuneSelectionCoordinator.IsCurrentRunInProgress(runState))
			{
				await CoverRoomTransitionAfterSelectionAsync(room);
			}
		}
	}

	// 表现层：失败只记日志，不影响选择与进房。
	private static async Task<bool> RevealRoomTransitionForSelectionAsync(AbstractRoom room)
	{
		try
		{
			if (NGame.Instance?.Transition is not { InTransition: true } transition)
			{
				return false;
			}

			HextechLog.Info("Mayhem", $"BeforeRoomEntered: revealing room transition for selection room={room.GetType().Name}");
			await transition.RoomFadeIn();
			return true;
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"BeforeRoomEntered: revealing room transition failed: {ex}");
			return false;
		}
	}

	private static async Task CoverRoomTransitionAfterSelectionAsync(AbstractRoom room)
	{
		try
		{
			if (NGame.Instance?.Transition is { } transition)
			{
				await transition.RoomFadeOut();
				HextechLog.Info("Mayhem", $"BeforeRoomEntered: restored room transition after selection room={room.GetType().Name}");
			}
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"BeforeRoomEntered: restoring room transition failed: {ex}");
		}
	}
}
