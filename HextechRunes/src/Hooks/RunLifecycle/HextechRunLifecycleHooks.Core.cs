using Godot;
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
	// 等待其他玩家完成远古事件超过这个帧数时告警一次，但继续等待：超时由各端独立判定，单端放弃会让
	// 先放弃的一端等下一房间、后完成的一端等它完成选择，互相卡住，所以只能换局时退出。
	private const int RemoteEventsSlowWaitWarnFrames = 18000;

	private static readonly HashSet<RunState> RunsInsideStartRunOrig = [];

	private readonly record struct EventRoomProceedState(RunState RunState, int ActIndex, string EventId);

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

	private static bool ShouldDeferActSelectionUntilAfterCurrentEvent(RunState runState)
	{
		return runState.CurrentActIndex >= 0
			&& runState.CurrentRoom is EventRoom { CanonicalEvent: AncientEventModel };
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
