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
				int stageIndex = ResolveCurrentStageIndex(runState, modifier, out _);
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

	// 顶栏初始化是同步回调，只能启动不能等待。刷新会经 ResolveCurrentStageIndex 激活额外阶段、并从遗物恢复
	// 已结算幕次（写本局状态），触发时机取决于各端界面何时就绪。
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

		SubscribeRoomEnteredIfNeeded();
		int stageIndex = ResolveCurrentStageIndex(runState, modifier, out _);
		bool recovered = !modifier.IsStageResolved(stageIndex)
			&& modifier.TryRecoverResolvedActsFromPlayerRelics(reason, stageIndex);
		HextechEnemyUi.Refresh(modifier);
		HextechLog.Info("Mayhem", $"EnemyUi delayed refresh: reason={reason} frame={frame} recovered={recovered} actIndex={runState.CurrentActIndex} {modifier.DescribeActState()}");
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
			if (runState is RunState concreteRunState)
			{
				ScheduleEnemyUiRefresh(() => concreteRunState, "NTopBar.Initialize");
				return;
			}

			ScheduleEnemyUiRefresh(static () => RunManager.Instance.DebugOnlyGetState(), "NTopBar.Initialize");
		}
	}
}
