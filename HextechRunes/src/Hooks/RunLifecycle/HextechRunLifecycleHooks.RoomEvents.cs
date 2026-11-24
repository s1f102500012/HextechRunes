using MegaCrit.Sts2.Core.Helpers;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static readonly RunManagerEventSubscription RoomEnteredSubscription = new(
		static (manager, handler) => manager.RoomEntered += handler,
		static (manager, handler) => manager.RoomEntered -= handler,
		OnRoomEntered);

	private static void SubscribeRoomEnteredIfNeeded(bool force = false)
	{
		RoomEnteredSubscription.Ensure(RunManager.Instance, force);
	}

	/// <summary>
	/// 对当前 RunManager 实例保持恰好一次订阅：实例换了就从旧实例退订再订阅新实例；
	/// force（无尽循环重置）时对同一实例先退订再重新订阅。
	/// </summary>
	private sealed class RunManagerEventSubscription(
		Action<RunManager, Action> subscribe,
		Action<RunManager, Action> unsubscribe,
		Action handler)
	{
		private RunManager? _manager;

		internal void Ensure(RunManager manager, bool force)
		{
			if (ReferenceEquals(_manager, manager))
			{
				if (!force)
				{
					return;
				}

				unsubscribe(manager, handler);
			}
			else if (_manager != null)
			{
				unsubscribe(_manager, handler);
			}

			subscribe(manager, handler);
			_manager = manager;
		}
	}

	private static void OnRoomEntered()
	{
		// 挂在 RunManager.RoomEntered 上，异常会中断委托链导致后续订阅者单端不执行（联机分叉），必须整体兜底。
		try
		{
			OnRoomEnteredCore();
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"OnRoomEntered failed: {ex}");
		}
	}

	private static void OnRoomEnteredCore()
	{
		if (RunManager.Instance.DebugOnlyGetState() is not RunState runState)
		{
			HextechLog.Info("Mayhem", $"OnRoomEntered: no run state");
			return;
		}

		HextechMayhemModifier? modifier = HextechMayhemModifier.FindIn(runState);
		if (modifier == null && !RunsInsideStartRunOrig.Contains(runState))
		{
			modifier = GetOrRecoverMayhemModifier(runState, $"OnRoomEntered recovered missing modifier room={runState.CurrentRoom?.GetType().Name ?? "null"} actIndex={runState.CurrentActIndex}");
		}

		string? extraStageId = null;
		int stageIndex = modifier == null
			? -1
			: ResolveCurrentStageIndex(runState, modifier, out extraStageId);
		if (modifier != null
			&& extraStageId == null
			&& IsUnannouncedEndlessLoopRewind(runState, modifier, stageIndex))
		{
			// 其他无尽模组(如 Limitless)换章时只把幕序号拨回 0,不调用 ResetForEndlessLoop;
			// 不补这一步,新一章的每幕都对上旧阶段而被当作已发放,整章不再发海克斯。
			modifier.ResetForEndlessLoop("act index rewound to 0");
			RefreshEnemyUiSafely(modifier);
			return;
		}
		if (modifier != null && !modifier.IsStageResolved(stageIndex) && modifier.TryRecoverResolvedActsFromPlayerRelics(nameof(OnRoomEntered), stageIndex))
		{
			RefreshEnemyUiSafely(modifier);
		}

		HextechLog.Info("Mayhem", $"OnRoomEntered: room={runState.CurrentRoom?.GetType().Name ?? "null"} actIndex={runState.CurrentActIndex} stageIndex={stageIndex} stageResolved={modifier?.IsStageResolved(stageIndex)} startedWithNeow={runState.ExtraFields.StartedWithNeow} {DescribeCurrentEventState(runState)}");
		if (modifier != null
			&& extraStageId == null
			&& ShouldDeferActSelectionUntilAfterCurrentEvent(runState)
			&& !modifier.IsActResolved(runState.CurrentActIndex))
		{
			HextechLog.Info("Mayhem", $"OnRoomEntered: pending act selection is deferred until the opening event is left. act={runState.CurrentActIndex} {DescribeCurrentEventState(runState)}");
			EnsurePostOpeningEventSelectionWatch(runState, "OnRoomEntered");
		}
		if (modifier != null && ShouldScheduleActSelectionOnRoomEntered(runState, modifier, stageIndex))
		{
			HextechLog.Info("Mayhem", $"OnRoomEntered: scheduling selection for room={runState.CurrentRoom?.GetType().Name ?? "null"}");
			// RunManager.RoomEntered 是同步 C# 事件，处理器无法等待；每个客户端都在自己的 RoomEntered 里对同一阶段
			// 启动选择，两端的同步由选择协议（等待远端/确认）完成。异常由 RunSafely 记录，不中断事件委托链。
			TaskHelper.RunSafely(HextechRuneSelectionCoordinator.HandleStageSelection(runState, modifier, stageIndex));
		}

		// Refresh 内部已先隐藏 Mayhem 顶栏徽标；只有没有 Modifier 时才需要单独隐藏。
		if (modifier != null)
		{
			RefreshEnemyUiSafely(modifier);
			return;
		}

		try
		{
			HextechEnemyUi.HideMayhemModifierBadge();
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"OnRoomEntered badge refresh failed: {ex}");
		}
	}

	private static void RefreshEnemyUiSafely(HextechMayhemModifier modifier)
	{
		// UI 刷新是纯表现层，失败不能影响后续状态调度。
		try
		{
			HextechEnemyUi.Refresh(modifier);
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"OnRoomEntered enemy UI refresh failed: {ex}");
		}
	}

	private static bool ShouldScheduleActSelectionOnRoomEntered(RunState runState, HextechMayhemModifier modifier, int stageIndex)
	{
		if (stageIndex < 0 || modifier.IsStageResolved(stageIndex) || ShouldDeferActSelectionUntilAfterCurrentEvent(runState))
		{
			return false;
		}

		return runState.CurrentRoom is not null and not EventRoom;
	}
}
