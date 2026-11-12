using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static async Task LoadRunAfterOriginal(Task original, RunState runState)
	{
		await original;

		// mod 延续体异常不能把原版 LoadRun 任务链打成 faulted。
		try
		{
			if (IsCurrentRun(runState))
			{
				RestoreStageState(runState, "LoadRun");
			}

			await RefreshEnemyUiWhenReady(() => runState, "LoadRun");
			// 不能 await：原版读档入口（主菜单继续/各联机读档界面）在 LoadRun 返回后才 FadeIn，
			// 恢复中的锻造器/海克斯选择要等玩家操作，等它会让画面一直停在淡出状态。
			// 两端都在各自的 LoadRun 延续里启动这段恢复，选择结果由选择协议同步；异常由 RunSafely 记录。
			_ = TaskHelper.RunSafely(ResumePendingSelectionTransactionsAfterLoad(runState));
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"LoadRun continuation failed: {ex}");
		}
	}

	// 按当前额外阶段 ID 激活/清除额外阶段，本阶段未结算时从玩家遗物恢复已结算阶段；返回是否恢复了状态。
	// 只在各端相同的同步流程位置调用，不碰界面节点：
	// - TopBar.Initialize 后缀：NRun._Ready 内同步执行，读档时早于 GenerateMap 和房间进入，预先结束的战斗房间生成奖励前已完成；
	// - LoadRunAfterOriginal：原版读档 Task 完成后再做一次，结果与进房钩子写入的相同（幂等兜底）。
	// 找不到本局 Modifier 时什么都不做（新开局的 Modifier 在 StartRun 之后才建，由开局流程和进房钩子负责）。
	// 额外阶段 ID 由其他模组提供，TopBar.Initialize 时可能还取不到（IntegratedStrategyEvents 在生成地图时才还原终局会话），
	// 取不到时按当前幕处理，额外阶段由之后的进房钩子激活。
	internal static bool RestoreStageState(RunState runState, string reason)
	{
		return HextechMayhemModifier.FindIn(runState) is HextechMayhemModifier modifier
			&& RestoreStageState(runState, modifier, HextechRunesInterop.GetCurrentExtraActId(runState), reason);
	}

	internal static bool RestoreStageState(RunState runState, HextechMayhemModifier modifier, string? extraStageId, string reason)
	{
		int stageIndex = ApplyStageIndex(modifier, extraStageId);
		bool recovered = !modifier.IsStageResolved(stageIndex)
			&& modifier.TryRecoverResolvedActsFromPlayerRelics(reason, stageIndex);
		HextechLog.Info("Mayhem", $"Stage state restored: reason={reason} recovered={recovered} actIndex={runState.CurrentActIndex} stage={stageIndex} {modifier.DescribeActState()}");
		return recovered;
	}

	private static async Task ResumePendingSelectionTransactionsAfterLoad(RunState runState)
	{
		if (!await ResumePendingInitialForgeGrantsAfterLoad(runState))
		{
			return;
		}

		await ResumePendingActSelectionAfterLoad(runState);
	}

	private static async Task<bool> ResumePendingInitialForgeGrantsAfterLoad(RunState runState)
	{
		List<InitialForgeGrantRune> pending = runState.Players
			.SelectMany(static player => player.Relics.OfType<InitialForgeGrantRune>())
			.Where(static rune => rune.SavedInitialForgeGrantPending)
			.ToList();
		if (pending.Count == 0)
		{
			return true;
		}

		for (int frame = 0; frame <= ResumeAfterLoadFrameBudget; frame++)
		{
			if (!IsCurrentRun(runState))
			{
				return false;
			}

			if (NOverlayStack.Instance != null
				&& NRun.Instance?.GlobalUi?.TopBar != null
				&& NOverlayStack.Instance.Peek() == null)
			{
				bool reopenMap = NMapScreen.Instance?.IsOpen == true && NGame.Instance != null;
				if (reopenMap)
				{
					NMapScreen.Instance!.Close(animateOut: false);
					await WaitOneFrame();
				}

				try
				{
					foreach (InitialForgeGrantRune rune in pending)
					{
						if (!IsCurrentRun(runState))
						{
							return false;
						}

						HextechLog.Info(
							"ForgeChoice", $"Resuming pending initial forge grants after load: "
							+ $"player={rune.Owner?.NetId.ToString() ?? "none"} rune={rune.Id.Entry}");
						if (!await rune.ResumePendingInitialForgeGrant())
						{
							HextechLog.Info(
								"ForgeChoice", $"Pending initial forge grants remain unresolved after load: "
								+ $"player={rune.Owner?.NetId.ToString() ?? "none"} rune={rune.Id.Entry}");
							return false;
						}
					}

					return true;
				}
				finally
				{
					if (reopenMap
						&& IsCurrentRun(runState)
						&& NMapScreen.Instance != null
						&& !NMapScreen.Instance.IsOpen)
					{
						NMapScreen.Instance.Open();
					}
				}
			}

			await WaitOneFrame();
		}

		HextechLog.Warn(
			"ForgeChoice", $"Pending initial forge grant recovery timed out: "
			+ $"currentRun={IsCurrentRun(runState)} count={pending.Count}");
		return false;
	}

	private static async Task ResumePendingActSelectionAfterLoad(RunState runState)
	{
		for (int frame = 0; frame <= ResumeAfterLoadFrameBudget; frame++)
		{
			if (!IsCurrentRun(runState))
			{
				return;
			}

			HextechMayhemModifier? modifier = HextechMayhemModifier.FindIn(runState);
			if (modifier != null)
			{
				// 轮询按界面就绪和玩家操作推进，各端执行时刻不同，所以只读：额外阶段已由
				// RestoreStageState 和进房钩子激活，这里只查序号。
				int stageIndex = PeekCurrentStageIndex(runState, modifier);
				if (stageIndex < 0 || modifier.IsStageResolved(stageIndex))
				{
					return;
				}

				if (ShouldDeferActSelectionUntilAfterCurrentEvent(runState))
				{
					HextechLog.Info("Mayhem", $"ResumePendingActSelectionAfterLoad: deferred for current event act={runState.CurrentActIndex} stage={stageIndex}");
					return;
				}

				if (NOverlayStack.Instance != null
					&& NRun.Instance?.GlobalUi?.TopBar != null
					&& ShouldScheduleActSelectionOnRoomEntered(runState, modifier, stageIndex))
				{
					HextechLog.Info("Mayhem", $"ResumePendingActSelectionAfterLoad: reopening unresolved selection act={runState.CurrentActIndex} stage={stageIndex} frame={frame} room={runState.CurrentRoom?.GetType().Name ?? "null"}");
					await HextechRuneSelectionCoordinator.HandleStageSelection(runState, modifier, stageIndex);
					return;
				}
			}

			await WaitOneFrame();
		}

		HextechLog.Warn("Mayhem", $"ResumePendingActSelectionAfterLoad timed out: currentRun={IsCurrentRun(runState)} act={runState.CurrentActIndex} room={runState.CurrentRoom?.GetType().Name ?? "null"}");
	}

	// 顶栏初始化是同步回调，只能启动不能等待。刷新时机取决于各端界面何时就绪，所以整条路径只读本局状态：
	// 额外阶段激活和已结算阶段恢复由进房钩子与 RestoreStageState 负责。
	private static void ScheduleEnemyUiRefresh(Func<RunState?> resolveRun, string reason)
	{
		TaskHelper.RunSafely(RefreshEnemyUiWhenReady(resolveRun, reason));
	}

	private static async Task RefreshEnemyUiWhenReady(Func<RunState?> resolveRun, string reason)
	{
		for (int frame = 0; frame <= EnemyUiRefreshFrameBudget; frame++)
		{
			if (resolveRun() is RunState runState && TryRefreshEnemyUiForRun(runState, reason, frame))
			{
				return;
			}

			await WaitOneFrame();
		}

		HextechEnemyUi.HideMayhemModifierBadge();
		HextechLog.Info("Mayhem", $"EnemyUi delayed refresh skipped: reason={reason} run/topbar/modifier not ready after {EnemyUiRefreshFrameBudget} frames");
	}

	private static bool TryRefreshEnemyUiForRun(RunState runState, string reason, int frame)
	{
		if (!IsCurrentRun(runState))
		{
			return true;
		}

		if (NRun.Instance?.GlobalUi?.TopBar == null || !HextechEnemyUi.IsTopBarReady())
		{
			return false;
		}

		HextechMayhemModifier? modifier = HextechMayhemModifier.FindIn(runState);
		if (modifier == null)
		{
			HextechEnemyUi.HideMayhemModifierBadge();
			return false;
		}

		int stageIndex = PeekCurrentStageIndex(runState, modifier);
		HextechEnemyUi.Refresh(modifier);
		HextechLog.Info("Mayhem", $"EnemyUi delayed refresh: reason={reason} frame={frame} actIndex={runState.CurrentActIndex} stage={stageIndex} {modifier.DescribeActState()}");
		return true;
	}

	[HarmonyPatch(typeof(NGame), "LoadRun", typeof(RunState), typeof(SerializableRoom))]
	[HextechPatch("run.load", "跑局生命周期")]
	private static class LoadRunPatch
	{
		[HarmonyPostfix]
		private static void Postfix(RunState runState, ref Task __result)
		{
			HextechSavedPropertyAuditCompat.RunAuditOnRunStartOnce();
			ResetRunScopedState(runState);
			// 读档重新进入当前房间时由 OnRoomEntered 激活阶段，订阅不能等界面就绪。
			SubscribeRoomEnteredIfNeeded();
			__result = LoadRunAfterOriginal(__result, runState);
		}
	}

	[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize), typeof(IRunState))]
	[HextechPatch("run.top-bar", "顶栏海克斯入口")]
	private static class TopBarInitializePatch
	{
		[HarmonyPostfix]
		private static void Postfix(IRunState runState)
		{
			// 顶栏初始化在 NRun._Ready 里同步执行，早于读档/开局的房间进入；订阅和阶段状态还原都在这里同步做，
			// 不依赖顶栏节点是否就绪，也不放进按帧轮询的界面刷新里。
			SubscribeRoomEnteredIfNeeded();
			if (runState is RunState concreteRunState)
			{
				try
				{
					RestoreStageState(concreteRunState, "NTopBar.Initialize");
				}
				catch (Exception ex)
				{
					HextechLog.Error("Mayhem", $"NTopBar.Initialize stage state restore failed: {ex}");
				}

				ScheduleEnemyUiRefresh(() => concreteRunState, "NTopBar.Initialize");
				return;
			}

			ScheduleEnemyUiRefresh(static () => RunManager.Instance.DebugOnlyGetState(), "NTopBar.Initialize");
		}
	}
}
