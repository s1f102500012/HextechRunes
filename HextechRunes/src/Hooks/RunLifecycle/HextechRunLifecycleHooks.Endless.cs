using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static bool IsUnannouncedEndlessLoopRewind(RunState runState, HextechMayhemModifier modifier, int stageIndex)
	{
		return IsUnannouncedEndlessLoopRewind(runState.CurrentActIndex, stageIndex, modifier.IsStageResolved);
	}

	/// <summary>
	/// 幕序号回到 0,且本幕与下一幕对应的阶段都已发放过 = 进入了未通知海克斯的新无尽循环。
	/// 正常流程里进入某幕时下一阶段尚未发放;幕开局存档读档同理,因此不会误判。
	/// 自带通知的 EndlessMode 在进入新循环第 0 幕前已重置,此时本阶段未发放,不会重复重置。
	/// </summary>
	internal static bool IsUnannouncedEndlessLoopRewind(int currentActIndex, int stageIndex, Func<int, bool> isStageResolved)
	{
		return currentActIndex == 0
			&& stageIndex >= 0
			&& isStageResolved(stageIndex)
			&& isStageResolved(stageIndex + 1);
	}

	internal static void HandleEndlessLoopReset(HextechMayhemModifier modifier, string reason)
	{
		SubscribeRoomEnteredIfNeeded(force: true);
		HextechRuneSelectionCoordinator.ResetActSelectionState();
		// 由 Modifier.ResetForEndlessLoop 同步调用（房间进入回调或无尽模组的同步通知），调用方无法等待；
		// 所有端都在同一逻辑点重置并启动这段等待，真正的海克斯选择由选择协议同步，异常由 RunSafely 记录。
		TaskHelper.RunSafely(HandleEndlessLoopActSelection(modifier, reason));
	}

	private static async Task HandleEndlessLoopActSelection(HextechMayhemModifier modifier, string reason)
	{
		RunState runState = modifier.ActiveRunState;
		int roomReadyFrames = 0;
		for (int frame = 0; frame < EndlessLoopActTransitionTimeoutFrames && IsCurrentRun(runState); frame++)
		{
			int actIndex = runState.CurrentActIndex;
			if (actIndex != 0)
			{
				if (frame % EndlessLoopWaitLogIntervalFrames == 0)
				{
					HextechLog.Info("Mayhem", $"Endless loop selection waiting for act transition: reason={reason} frame={frame} act={actIndex} room={runState.CurrentRoom?.GetType().Name ?? "null"} mapOpen={NMapScreen.Instance?.IsOpen == true}");
				}

				await WaitOneFrame();
				continue;
			}

			if (modifier.IsActResolved(actIndex))
			{
				HextechLog.Info("Mayhem", $"Endless loop selection skipped: act0 already resolved reason={reason} frame={frame}");
				return;
			}

			if (ShouldDeferActSelectionUntilAfterCurrentEvent(runState))
			{
				HextechLog.Info("Mayhem", $"Endless loop selection deferred until the opening event is left reason={reason} frame={frame} {DescribeCurrentEventState(runState)}");
				EnsurePostOpeningEventSelectionWatch(runState, $"EndlessLoop:{reason}");
				return;
			}

			if (runState.CurrentRoom != null || NMapScreen.Instance?.IsOpen == true)
			{
				HextechLog.Info("Mayhem", $"Endless loop selection starting: reason={reason} frame={frame} room={runState.CurrentRoom?.GetType().Name ?? "null"} mapOpen={NMapScreen.Instance?.IsOpen == true}");
				await HextechRuneSelectionCoordinator.HandleActSelection(runState, modifier);
				return;
			}

			if (roomReadyFrames % EndlessLoopWaitLogIntervalFrames == 0)
			{
				HextechLog.Info("Mayhem", $"Endless loop selection waiting for room: reason={reason} frame={frame} readyFrame={roomReadyFrames} act={actIndex} room=null mapOpen={NMapScreen.Instance?.IsOpen == true}");
			}

			roomReadyFrames++;
			if (roomReadyFrames >= EndlessLoopRoomReadyTimeoutFrames)
			{
				HextechLog.Warn("Mayhem", $"Endless loop selection room wait timed out: reason={reason} currentRun={IsCurrentRun(runState)} act={runState.CurrentActIndex} room={runState.CurrentRoom?.GetType().Name ?? "null"} mapOpen={NMapScreen.Instance?.IsOpen == true}");
				return;
			}

			await WaitOneFrame();
		}

		HextechLog.Warn("Mayhem", $"Endless loop selection act transition timed out: reason={reason} currentRun={IsCurrentRun(runState)} act={runState.CurrentActIndex} room={runState.CurrentRoom?.GetType().Name ?? "null"} mapOpen={NMapScreen.Instance?.IsOpen == true}");
	}
}
