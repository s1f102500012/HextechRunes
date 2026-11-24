namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override async Task AfterActEntered()
	{
		ClearActiveExtraStage();
		int actIndex = GetCurrentActSelectionIndex();
		if (!IsStageResolved(actIndex) && TryRecoverResolvedActsFromPlayerRelics(nameof(AfterActEntered), actIndex))
		{
			HextechEnemyUi.Refresh(this);
		}

		if (RunState.CurrentActIndex <= 0 || IsStageResolved(actIndex))
		{
			return;
		}

		if (ShouldDeferImmediateActSelection(RunState.CurrentRoom))
		{
			HextechLog.Info("Mayhem", $"AfterActEntered: deferring act selection until room/event flow is stable actIndex={actIndex} currentRoom={RunState.CurrentRoom?.GetType().Name ?? "null"}");
			return;
		}

		HextechLog.Info("Mayhem", $"AfterActEntered: resolving act selection before first room actIndex={actIndex}");
		await HextechRuneSelectionCoordinator.HandleStageSelection(RunState, this, actIndex);
	}

	public override async Task BeforeRoomEntered(AbstractRoom room)
	{
		string? extraStageId = HextechRunesInterop.GetCurrentExtraActId(RunState);
		int actIndex = string.IsNullOrWhiteSpace(extraStageId)
			? GetCurrentActSelectionIndex()
			: ActivateExtraStage(extraStageId);
		if (!IsStageResolved(actIndex) && TryRecoverResolvedActsFromPlayerRelics(nameof(BeforeRoomEntered), actIndex))
		{
			HextechEnemyUi.Refresh(this);
		}

		switch (DecideBeforeRoomSelection(
			actIndex,
			IsStageResolved(actIndex),
			room is EventRoom or MapRoom,
			isFirstActStage: RunState.CurrentActIndex == 0 && string.IsNullOrWhiteSpace(extraStageId),
			insideVanillaRunEntry: HextechRunLifecycleHooks.IsInsideVanillaRunEntry(RunState)))
		{
			case BeforeRoomSelectionStep.None:
				return;
			case BeforeRoomSelectionStep.DeferFirstActToLoadResume:
				HextechLog.Info("Mayhem", $"BeforeRoomEntered: act0 selection not opened inside vanilla StartRun/LoadRun room={room.GetType().Name} stage={actIndex}; load resume / RoomEntered will reopen it after the run screen is shown");
				return;
			case BeforeRoomSelectionStep.FirstActGate:
				HextechLog.Info("Mayhem", $"BeforeRoomEntered: act0 still unresolved, resolving at synchronized gate before room={room.GetType().Name} stage={actIndex}");
				await HextechRunLifecycleHooks.ResolveFirstActSelectionBeforeRoomAsync(RunState, this, actIndex, room);
				return;
		}

		HextechLog.Info("Mayhem", $"BeforeRoomEntered: resolving pending act selection before room={room.GetType().Name} actIndex={actIndex}");
		await HextechRuneSelectionCoordinator.HandleStageSelection(RunState, this, actIndex);
	}

	internal enum BeforeRoomSelectionStep
	{
		None,
		// 第二幕起与额外阶段：原有兜底（直接开始选择，已有选择在进行时跳过）。
		LaterStage,
		// 第一幕：各端同步的兜底关口，等进行中的选择收尾后补完本幕。
		FirstActGate,
		// 第一幕但仍在原版开局/读档流程里：读档入口的遮罩未淡入、LoadRun 被挡住，不在这里等玩家，交给读档续跑与 RoomEntered。
		DeferFirstActToLoadResume
	}

	/// <summary>进房前补选的判定（纯函数）。第二幕起的分支与旧行为相同；第一幕不再整幕跳过，只排除原版开局/读档流程内部。</summary>
	internal static BeforeRoomSelectionStep DecideBeforeRoomSelection(
		int stageIndex,
		bool stageResolved,
		bool roomIsEventOrMap,
		bool isFirstActStage,
		bool insideVanillaRunEntry)
	{
		if (stageIndex < 0 || stageResolved || roomIsEventOrMap)
		{
			return BeforeRoomSelectionStep.None;
		}

		if (!isFirstActStage)
		{
			return BeforeRoomSelectionStep.LaterStage;
		}

		return insideVanillaRunEntry
			? BeforeRoomSelectionStep.DeferFirstActToLoadResume
			: BeforeRoomSelectionStep.FirstActGate;
	}

	private static bool ShouldDeferImmediateActSelection(AbstractRoom? currentRoom)
	{
		return currentRoom == null
			|| currentRoom is EventRoom { CanonicalEvent: AncientEventModel };
	}
}
