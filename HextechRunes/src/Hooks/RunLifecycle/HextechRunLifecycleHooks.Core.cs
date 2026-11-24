using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	// 按帧轮询的预算与日志间隔（帧数随帧率变化，只作为"足够久"的上限，不承担时序语义）。
	private const int EnemyUiRefreshFrameBudget = 45;
	private const int ResumeAfterLoadFrameBudget = 300;
	private const int EndlessLoopActTransitionTimeoutFrames = 3600;
	private const int EndlessLoopRoomReadyTimeoutFrames = 600;
	private const int EndlessLoopWaitLogIntervalFrames = 120;
	private const int RemoteEventsWaitLogIntervalFrames = 300;
	// 先古结束后等待所有玩家的事件完成超过这个帧数时告警一次，但不单端放弃：等待期间地图行进保持可用，
	// 条件一直不满足时由各端同步的进房关口（BeforeRoomEntered）补选，退出条件只有换局、本幕已决或已离开开幕房。
	private const int RemoteEventsSlowWaitWarnFrames = 18000;

	private static readonly HashSet<RunState> RunsInsideStartRunOrig = [];
	// 原版 NGame.LoadRun 尚未完成的局：读档时进入当前房间的 BeforeRoomEntered 在这里面被等待，
	// 此时读档入口的转场遮罩还没淡入、LoadRun 也被挡住，不能在这里等玩家选择（交给读档后续跑与 RoomEntered）。
	private static readonly HashSet<RunState> RunsInsideLoadRunOrig = [];

	/// <summary>
	/// 跑局开始与读档共用：清空日志预算、战斗与敌方海克斯的跑局临时状态，并按新跑局重置夺金同步。
	/// 开始新局的前缀在本方法之后才登记 <see cref="RunsInsideStartRunOrig"/>；这里清掉的只有原版 StartRun 同步抛异常时没能移除的旧跑局。
	/// </summary>
	private static void ResetRunScopedState(RunState runState)
	{
		RunsInsideStartRunOrig.Clear();
		HextechRunLogBudget.Reset();
		ResetTransientRunState();
		HextechGoldrendSync.ResetForRun(runState);
	}

	/// <summary>跑局开始、读档与结束都要清的战斗/敌方海克斯临时状态。</summary>
	private static void ResetTransientRunState()
	{
		HextechCombatHooks.ResetTransientCombatState();
		HextechEnemyHexEffects.ResetAllRunScopedState();
	}

	internal static HextechMayhemModifier EnsureMayhemModifier(RunState runState)
	{
		if (HextechMayhemModifier.FindIn(runState) is HextechMayhemModifier existing)
		{
			HextechLog.Info("Mayhem", $"EnsureMayhemModifier: existing state preserved {existing.DescribeActState()}");
			return existing;
		}

		HextechMayhemModifier modifier = (HextechMayhemModifier)ModelDb.Modifier<HextechMayhemModifier>().ToMutable();
		modifier.ResetForNewRun();
		modifier.OnRunLoaded(runState);
		runState.AddModifierDebug(modifier);
		HextechLog.Info("Mayhem", $"EnsureMayhemModifier: added");
		return modifier;
	}

	private static HextechMayhemModifier GetOrRecoverMayhemModifier(RunState runState, string reason)
	{
		if (HextechMayhemModifier.FindIn(runState) is HextechMayhemModifier existing)
		{
			return existing;
		}

		HextechLog.Warn("Mayhem", $"{reason}; reattaching");
		return EnsureMayhemModifier(runState);
	}

	private static bool IsCurrentRun(RunState runState)
	{
		return ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), runState);
	}

	/// <summary>
	/// 当前房是本幕的开幕事件：先古事件，或本幕起点上的事件房（其他模组把涅奥换成非先古事件时也算）。
	/// 本幕选择推迟到离开它、地图可以行进时（见 <see cref="EnsurePostOpeningEventSelectionWatch"/>）。
	/// </summary>
	private static bool ShouldDeferActSelectionUntilAfterCurrentEvent(RunState runState)
	{
		return runState.CurrentActIndex >= 0
			&& runState.CurrentRoom is EventRoom eventRoom
			&& (eventRoom.CanonicalEvent is AncientEventModel || IsAtActStartingPoint(runState));
	}

	private static bool IsAtActStartingPoint(RunState runState)
	{
		try
		{
			return runState.CurrentMapCoord is MapCoord current
				&& runState.Map?.StartingMapPoint is MapPoint start
				&& current.Equals(start.coord);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"IsAtActStartingPoint failed: {ex.GetType().Name}: {ex.Message}");
			return false;
		}
	}

	/// <summary>原版开局/读档流程（NGame.StartRun / NGame.LoadRun）仍在进行；各端都在自己的同一流程位置里。</summary>
	internal static bool IsInsideVanillaRunEntry(RunState runState)
	{
		return RunsInsideStartRunOrig.Contains(runState) || RunsInsideLoadRunOrig.Contains(runState);
	}

	private static int ResolveCurrentStageIndex(
		RunState runState,
		HextechMayhemModifier modifier,
		out string? extraStageId)
	{
		extraStageId = HextechRunesInterop.GetCurrentExtraActId(runState);
		return ApplyStageIndex(modifier, extraStageId);
	}

	// 有额外阶段 ID 时激活（必要时创建）该阶段，否则清除激活的额外阶段；返回当前阶段序号。
	private static int ApplyStageIndex(HextechMayhemModifier modifier, string? extraStageId)
	{
		if (!string.IsNullOrWhiteSpace(extraStageId))
		{
			return modifier.ActivateExtraStage(extraStageId);
		}

		modifier.ClearActiveExtraStage();
		return modifier.GetCurrentActSelectionIndex();
	}

	// ResolveCurrentStageIndex 的只读版本：不激活/清除额外阶段，额外阶段尚无条目时退回当前幕的选择序号。
	private static int PeekCurrentStageIndex(RunState runState, HextechMayhemModifier modifier)
	{
		return modifier.PeekStageIndex(HextechRunesInterop.GetCurrentExtraActId(runState));
	}

	private static string DescribeCurrentEventState(RunState runState)
	{
		if (runState.CurrentRoom is not EventRoom eventRoom)
		{
			return "eventState=none";
		}

		try
		{
			EventModel localEvent = eventRoom.LocalMutableEvent;
			return $"eventState={localEvent.Id.Entry} finished={localEvent.IsFinished} options={localEvent.CurrentOptions.Count}";
		}
		catch (Exception ex)
		{
			return $"eventState={eventRoom.CanonicalEvent.Id.Entry} localUnavailable={ex.GetType().Name}";
		}
	}

	private static async Task WaitOneFrame()
	{
		if (NGame.Instance != null)
		{
			await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
			return;
		}

		await Task.Yield();
	}
}
