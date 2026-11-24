using MegaCrit.Sts2.Core.Nodes;

namespace HextechRunes;

internal static partial class HextechRunLifecycleHooks
{
	private static async Task FinalizeStartingRelicsAfterOriginal(Task original, RunManager self)
	{
		await original;

		// mod 延续体异常不能把原版任务链打成 faulted(单端中断即联机分叉)。
		try
		{
			RunState? runState = self.DebugOnlyGetState();
			if (runState == null)
			{
				return;
			}

			foreach (Player player in runState.Players)
			{
				HextechRuneSelectionCoordinator.RemoveRunesFromGrabBags(player);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"FinalizeStartingRelics continuation failed: {ex}");
		}
	}

	private static async Task StartRunAfterOriginal(Task original, RunState runState)
	{
		try
		{
			await original;
		}
		finally
		{
			RunsInsideStartRunOrig.Remove(runState);
		}

		// mod 延续体异常不能把原版 StartRun 任务链打成 faulted(单端中断即联机分叉)。
		try
		{
			HextechMayhemModifier modifier = EnsureMayhemModifier(runState);
			HextechLog.Info("Mayhem", $"StartRun end: currentRoom={runState.CurrentRoom?.GetType().Name ?? "null"} actIndex={runState.CurrentActIndex} {DescribeCurrentEventState(runState)}");
			try
			{
				HextechEnemyUi.Refresh(modifier);
			}
			catch (Exception ex)
			{
				HextechLog.Error("Mayhem", $"StartRun UI refresh failed: {ex}");
			}

			if (!modifier.IsActResolved(runState.CurrentActIndex)
				&& IsCurrentRun(runState))
			{
				if (ShouldDeferActSelectionUntilAfterCurrentEvent(runState))
				{
					HextechLog.Info("Mayhem", $"StartRun: deferring act{runState.CurrentActIndex} selection until the opening event is left {DescribeCurrentEventState(runState)}");
					EnsurePostOpeningEventSelectionWatch(runState, "StartRun");
				}
				else
				{
					HextechLog.Info("Mayhem", $"StartRun: selecting act{runState.CurrentActIndex} hex immediately after StartRun");
					await HextechRuneSelectionCoordinator.HandleActSelection(runState, modifier);
				}
			}
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"StartRun continuation failed: {ex}");
		}
	}

	[HarmonyPatch(typeof(RunManager), nameof(RunManager.FinalizeStartingRelics), new Type[0])]
	[HextechPatch("run.finalize-starting-relics", "跑局生命周期")]
	private static class FinalizeStartingRelicsPatch
	{
		[HarmonyPostfix]
		private static void Postfix(RunManager __instance, ref Task __result)
		{
			__result = FinalizeStartingRelicsAfterOriginal(__result, __instance);
		}
	}

	[HarmonyPatch(typeof(NGame), "StartRun", typeof(RunState))]
	[HextechPatch("run.start", "跑局生命周期")]
	private static class StartRunPatch
	{
		[HarmonyPrefix]
		private static void Prefix(RunState runState)
		{
			ResetRunScopedState(runState);
			HextechRuneSelectionCoordinator.ResetActSelectionState();
			HextechEnemyUi.Clear();
			SubscribeRoomEnteredIfNeeded();
			HextechLog.Info("Mayhem", $"StartRun begin: seed={runState.Rng.StringSeed} actIndex={runState.CurrentActIndex} startedWithNeow={runState.ExtraFields.StartedWithNeow}");
			RunsInsideStartRunOrig.Add(runState);
		}

		[HarmonyPostfix]
		private static void Postfix(RunState runState, ref Task __result)
		{
			HextechSavedPropertyAuditCompat.RunAuditOnRunStartOnce();
			__result = StartRunAfterOriginal(__result, runState);
		}
	}
}
