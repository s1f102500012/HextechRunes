using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Saves;
using static HextechRunes.HextechRunePoolBuilder;
using static HextechRunes.HextechSelectionHelpers;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	// 等待遮挡界面关闭时,每 120 帧(约 2 秒)记一次日志,避免逐帧刷屏。
	private const int OverlayWaitLogIntervalFrames = 120;

	// 同一时刻只处理一幕的选择:非空表示正在处理,值是所属的局。换局后残留的标记在下次进入时清掉,
	// 不能挡住新局的选择;处理结束时只释放属于本局的标记。
	private static RunState? _actSelectionRun;

	public static void ResetActSelectionState()
	{
		_actSelectionRun = null;
		HextechSelectionProgress.Reset([]);
	}

	private static bool ResetActSelectionIfStale(RunState runState)
	{
		if (_actSelectionRun == null || ReferenceEquals(_actSelectionRun, runState))
		{
			return false;
		}

		_actSelectionRun = null;
		return true;
	}

	public static Task HandleActSelection(RunState runState, HextechMayhemModifier modifier)
	{
		return HandleStageSelection(runState, modifier, modifier.GetCurrentActSelectionIndex());
	}

	public static async Task HandleStageSelection(RunState runState, HextechMayhemModifier modifier, int actIndex)
	{
		if (!modifier.IsStageResolved(actIndex) && modifier.TryRecoverResolvedActsFromPlayerRelics(nameof(HandleStageSelection), actIndex))
		{
			HextechEnemyUi.Refresh(modifier);
		}

		if (ResetActSelectionIfStale(runState))
		{
			HextechLog.Warn("Mayhem", $"HandleHextechActSelection: clearing stale handling state for previous run");
		}

		HextechLog.Info("Mayhem", $"HandleHextechActSelection enter: room={runState.CurrentRoom?.GetType().Name ?? "null"} actIndex={actIndex} resolved={modifier.IsStageResolved(actIndex)} handling={_actSelectionRun != null}");
		if (_actSelectionRun != null || !IsCurrentRun(runState) || actIndex < 0 || modifier.IsStageResolved(actIndex))
		{
			HextechLog.Info("Mayhem", $"HandleHextechActSelection skip");
			return;
		}

		_actSelectionRun = runState;
		bool reopenMapAfterSelection = false;
		// 表现层要求保持地图打开时，本体界面会在显示前自行关闭地图（见 CloseMapBeforeOwnScreen），结束时同样要重新打开。
		bool mapOpenBeforeSelection = NMapScreen.Instance?.IsOpen == true;
		try
		{
			if (!await WaitForSelectionBlockingOverlaysToClear(runState, actIndex, "before-map-close"))
			{
				return;
			}

			reopenMapAfterSelection = CloseMapForSelection();
			if (reopenMapAfterSelection && NGame.Instance is NGame game)
			{
				await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			if (!IsCurrentRun(runState))
			{
				HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: run is no longer current");
				return;
			}
			if (!await WaitForSelectionBlockingOverlaysToClear(runState, actIndex, "before-selection"))
			{
				return;
			}

			foreach (Player player in runState.Players)
			{
				RemoveRunesFromGrabBags(player);
			}

			(HextechRarityTier rarity, MonsterHexKind? monsterHex, int playerHexCount) = await ResolveActRoll(runState, modifier, actIndex);
			// 必须先完成房主配置同步，再决定是否生成内容；禁用分支不能先抽取
			// 多个敌方海克斯再丢弃，否则仍会推进原版 Niche 随机流。
			if (modifier.FreezeModActiveForRunAndCheckDisabled())
			{
				// 弑神试炼局同样关闭模组内容，只在第一幕补发弑神；中途关掉界面时本幕保持未决，下次进入再弹。
				await HextechGodslayerTrial.GrantForFirstActAsync(runState, modifier, actIndex);
				modifier.SetMonsterHexesForAct(actIndex, []);
				modifier.SetStageResolved(actIndex, true);
				HextechEnemyUi.Refresh(modifier);
				await PersistActSelection(runState, actIndex);
				HextechLog.Info("Mayhem", $"Skipped content generation for disabled run: act={actIndex}");
				return;
			}

			HextechLog.Info("Mayhem", $"HandleHextechActSelection rarity: act={actIndex} rarity={rarity}");
			HextechLog.Info("Mayhem", $"HandleHextechActSelection monsterHex: act={actIndex} hex={monsterHex}");
			StageMonsterHexes stageHexes = new(modifier.GetActiveMonsterHexesBeforeAct(actIndex));
			stageHexes.SetNew(ResolveNewMonsterHexesForAct(modifier, rarity, runState, actIndex, monsterHex));
			if (!await RunStagePlayerSelectionsAsync(runState, modifier, actIndex, rarity, playerHexCount, stageHexes))
			{
				return;
			}

			// 退出清理期间选择界面被原版清掉，任务链会在 State 仍指向本局时续跑到这里；此时不能标记本幕已决。
			if (!IsCurrentRunInProgress(runState))
			{
				HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: run changed or is being cleaned up before resolving act");
				return;
			}

			await CompleteStageAsync(runState, modifier, actIndex, stageHexes.Final);
			HextechLog.Info("Mayhem", $"HandleHextechActSelection resolved: act={actIndex}");
		}
		catch (OperationCanceledException)
		{
			HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: selection overlay closed before choice act={actIndex}");
		}
		catch (Exception ex)
		{
			// 记录异常，避免它继续打断开局/进幕的调用链。这里不回滚已经完成的状态写入；
			// 只有尚未 SetStageResolved 的阶段才会在后续进入或读档时重新尝试选择。
			// 捕获异常本身不保证两端恢复一致，联机状态仍由原版校验。
			HextechLog.Error("Mayhem", $"HandleHextechActSelection failed act={actIndex} networkMp={HextechRelicBase.IsNetworkMultiplayerRun()}: {ex}");
		}
		finally
		{
			// 地图 UI 是表现层,它抛错不能阻断闸门释放;否则本局后续所有进入都会被 IsHandling 挡住。
			try
			{
				if ((reopenMapAfterSelection || (mapOpenBeforeSelection && HextechSelectionPresentation.KeepsMapOpen))
					&& IsCurrentRunInProgress(runState)
					&& NMapScreen.Instance != null
					&& !NMapScreen.Instance.IsOpen)
				{
					HextechLog.Info("Mayhem", $"HandleHextechActSelection: reopening map after selection overlay");
					NMapScreen.Instance.Open();
				}
			}
			catch (Exception ex)
			{
				HextechLog.Error("Mayhem", $"HandleHextechActSelection: reopening map failed act={actIndex}: {ex}");
			}
			finally
			{
				if (ReferenceEquals(_actSelectionRun, runState))
				{
					_actSelectionRun = null;
				}
			}

			HextechLog.Info("Mayhem", $"HandleHextechActSelection exit: act={actIndex}");
		}
	}

	/// <summary>本幕敌方海克斯的显示/结算状态:前几幕累积 + 本幕新增,以及选择界面上展示的本幕首个新增海克斯。</summary>
	private sealed class StageMonsterHexes(IReadOnlyList<MonsterHexKind> previous)
	{
		public IReadOnlyList<MonsterHexKind> Previous { get; } = previous;
		public IReadOnlyList<MonsterHexKind> New { get; private set; } = [];
		public IReadOnlyList<MonsterHexKind> Final { get; private set; } = previous;
		public RelicModel? VisibleRelic { get; private set; }

		public void SetNew(IReadOnlyList<MonsterHexKind> newHexes)
		{
			New = newHexes;
			Final = HextechMonsterHexRoller.CombineActiveHexes(Previous, newHexes);
			VisibleRelic = CreateMonsterHexRelic(newHexes.Cast<MonsterHexKind?>().FirstOrDefault());
		}

		// 联机批次直接返回本幕最终累积集;允许调整敌方时再据此回推本幕新增与展示图标。
		public void SetFinalFromMultiplayer(IReadOnlyList<MonsterHexKind> finalHexes, bool enemyHexesMayHaveChanged)
		{
			Final = finalHexes;
			if (enemyHexesMayHaveChanged)
			{
				New = finalHexes
					.Where(hex => !Previous.Contains(hex))
					.ToArray();
				VisibleRelic = CreateMonsterHexRelic(New.Cast<MonsterHexKind?>().FirstOrDefault());
			}
		}
	}

	/// <summary>
	/// 本幕的玩家选择(每名玩家 playerHexCount 次;没有玩家选择时可能只调整敌方海克斯)。
	/// 返回 false 表示对局已切换,调用方应直接放弃本幕。
	/// </summary>
	private static async Task<bool> RunStagePlayerSelectionsAsync(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		HextechRarityTier rarity,
		int playerHexCount,
		StageMonsterHexes stageHexes)
	{
		NetGameType gameType = RunManager.Instance.NetService.Type;
		// 候选池一旦抽空,本幕后续几次选择也必然为空:每名玩家本幕只弹一次"继续"界面。
		HashSet<ulong> playersNotifiedNoOptions = [];
		for (int choiceOrdinal = 0; choiceOrdinal < playerHexCount; choiceOrdinal++)
		{
			bool allowEnemyHexAdjustment = choiceOrdinal == 0;
			if (HextechPlayerContextHelper.IsSinglePlayerFlow(gameType))
			{
				foreach (Player player in runState.Players)
				{
					if (!await SelectSinglePlayerRuneAsync(runState, modifier, actIndex, rarity, choiceOrdinal, allowEnemyHexAdjustment, player, stageHexes, playersNotifiedNoOptions))
					{
						return false;
					}
				}
			}
			else
			{
				IReadOnlyList<MonsterHexKind> finalMonsterHexes = await SelectRunesForAllPlayersMultiplayer(
					runState,
					modifier,
					actIndex,
					rarity,
					stageHexes.Previous,
					stageHexes.New,
					stageHexes.VisibleRelic,
					choiceOrdinal,
					allowEnemyHexAdjustment,
					playersNotifiedNoOptions);
				stageHexes.SetFinalFromMultiplayer(finalMonsterHexes, allowEnemyHexAdjustment);
			}
		}

		if (playerHexCount <= 0)
		{
			// 没有玩家选择时仍要给敌方新增海克斯一个确认/调整界面。
			if (stageHexes.New.Count > 0)
			{
				stageHexes.SetNew(await SelectEnemyHexesOnly(runState, modifier, actIndex, rarity, stageHexes.Previous, stageHexes.New));
			}
			HextechLog.Info("Mayhem", $"HandleHextechActSelection skipped player choices: act={actIndex} configuredPlayerHexCount={playerHexCount}");
		}

		return true;
	}

	/// <summary>单机一名玩家的一次符文选择并发放。返回 false 表示对局已切换。</summary>
	private static async Task<bool> SelectSinglePlayerRuneAsync(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		HextechRarityTier rarity,
		int choiceOrdinal,
		bool allowEnemyHexAdjustment,
		Player player,
		StageMonsterHexes stageHexes,
		HashSet<ulong> playersNotifiedNoOptions)
	{
		// 玩家候选只排除本局已见过的符文;敌方持有的海克斯照样可以出现在玩家候选里。
		HashSet<ModelId> excludedIds = modifier.GetSeenPlayerRuneIds(player);
		List<RelicModel> options = BuildSelectableRunesForRarity(
			player,
			rarity,
			runState,
			excludedIds,
			useEndlessTagWindow: modifier.IsEndlessLoopActive);
		options = ApplyStageGuaranteedPick(runState, player, actIndex, choiceOrdinal, excludedIds, options);
		if (options.Count == 0)
		{
			HextechLog.Warn("Mayhem", $"HandleHextechActSelection no options: player={player.NetId} act={actIndex} ordinal={choiceOrdinal} rarity={rarity}");
			// 本稀有度已无可选:仍要给玩家一个界面交代,并保留本幕敌方海克斯的调整机会。
			if (!playersNotifiedNoOptions.Add(player.NetId))
			{
				return true;
			}

			if (allowEnemyHexAdjustment && stageHexes.New.Count > 0)
			{
				stageHexes.SetNew(await SelectEnemyHexesOnly(runState, modifier, actIndex, rarity, stageHexes.Previous, stageHexes.New,
					new LocString(HextechRuneLabels.LocTable, "HEXTECH_NO_RUNE_OPTIONS_TITLE").GetRawText()));
			}
			else
			{
				await ShowNoRuneOptionsScreenAsync();
			}

			if (!IsCurrentRunInProgress(runState))
			{
				HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: no-options screen returned for stale run");
				return false;
			}

			return true;
		}

		HextechLog.Info("Mayhem", $"HandleHextechActSelection options: player={player.NetId} ordinal={choiceOrdinal} count={options.Count} ids={string.Join(",", options.Select(o => o.CanonicalId().Entry))}");
		// choiceOrdinal>0(!allowEnemyHexAdjustment):敌方 hex 已在第一次选择时定妥,后续玩家符文选择只读展示
		// 【本幕新增】的敌方 hex(New 在首次选择后已更新为调整后的结果),不给控件。
		// 不能用 Final:它含前几幕累积集,会把历史敌方海克斯一起显示(玩家实报)。
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = stageHexes.Final.Count > 0
			? CreateEnemyHexAdjustmentOptions(
				modifier,
				rarity,
				runState,
				actIndex,
				stageHexes.New,
				controlsEnabled: allowEnemyHexAdjustment && stageHexes.New.Count > 0,
				syncContext: null,
				CancellationToken.None)
			: null;
		RuneSelectionResult selection = await SelectRune(
			modifier,
			player,
			actIndex,
			choiceOrdinal,
			options,
			stageHexes.VisibleRelic,
			enemyHexOptions);
		if (!IsCurrentRunInProgress(runState))
		{
			HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: selection returned for stale run");
			return false;
		}

		if (allowEnemyHexAdjustment)
		{
			stageHexes.SetNew(selection.ResolvedMonsterHexes);
		}

		RelicModel selected = RequireCompletedSelection(
			selection.SelectedRelic,
			$"singleplayer act={actIndex} ordinal={choiceOrdinal} player={player.NetId}");
		HextechTelemetry.RecordRuneChoice(runState, actIndex, rarity, player, selection.FinalOptions, selected, selection.RerollCount, choiceOrdinal);
		modifier.CommitCharacterRuneWeight(player, selection.FinalOptions);
		await RelicCmd.Obtain(selected, player);
		// 拾取效果（如棱彩海克斯的连续锻造三选一）在 Obtain 内等待玩家操作；保存并退出会取消这些界面并让这里续跑。
		// 单机回滚语义：放弃本幕，不标记已决、不存档；内存里的半途发放随本局一起丢弃，读档回到选择前的存档重选。
		if (!IsCurrentRunInProgress(runState))
		{
			HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: run changed or is being cleaned up after obtaining relic={selected.CanonicalId().Entry} player={player.NetId} ordinal={choiceOrdinal}");
			return false;
		}

		HextechLog.Info("Mayhem", $"HandleHextechActSelection obtained: player={player.NetId} ordinal={choiceOrdinal} relic={selected.CanonicalId().Entry}");
		return true;
	}

	// 每幕首次三选一加入保底候选（PlayerRuneFlags.GuaranteedFirstPick）。
	// 单机与联机共用，联机各端用同样的已见记录各自算出同一个结果。
	private static List<RelicModel> ApplyStageGuaranteedPick(
		RunState runState,
		Player player,
		int actIndex,
		int choiceOrdinal,
		IReadOnlySet<ModelId> seenIds,
		List<RelicModel> options)
	{
		return choiceOrdinal == 0
			? ApplyGuaranteedFirstPick(player, runState, actIndex, seenIds, options)
			: options;
	}

	// 关掉地图才能显示选择覆盖层;返回 true 表示关过地图,选择结束后要重新打开(调用方等一帧再继续)。
	private static bool CloseMapForSelection()
	{
		if (NMapScreen.Instance?.IsOpen != true || NGame.Instance == null
			// 外部表现层在打开的地图上呈现选择。
			|| HextechSelectionPresentation.KeepsMapOpen)
		{
			return false;
		}

		HextechLog.Info("Mayhem", $"HandleHextechActSelection: closing map before showing selection overlay");
		NMapScreen.Instance.Close(animateOut: false);
		return true;
	}

	// 表现层保持地图打开，但这块界面由本体自己显示（表现层拒绝接管，或是“仅继续”、自选、锻造器等界面）：
	// 显示前关闭地图，否则界面会落进只读地图预览。重新打开由 HandleHextechActSelection 的 finally 负责。
	private static async Task CloseMapBeforeOwnScreenAsync(HextechRuneSelectionScreen screen)
	{
		if (screen.IsExternallyPresented
			|| !HextechSelectionPresentation.KeepsMapOpen
			|| NMapScreen.Instance?.IsOpen != true
			|| NGame.Instance is not NGame game)
		{
			return;
		}

		HextechLog.Info("Mayhem", "SelectionScreen: closing map before showing a screen the presenter did not take over");
		NMapScreen.Instance.Close(animateOut: false);
		await game.ToSignal(game.GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	/// <summary>本幕结算完成:写入敌方海克斯、标记已结算、应用地图修正、刷新敌方 UI 并作用到当前敌人,最后存档。</summary>
	private static async Task CompleteStageAsync(RunState runState, HextechMayhemModifier modifier, int actIndex, IReadOnlyList<MonsterHexKind> finalMonsterHexes)
	{
		// 敌方质变在本幕海克斯落定时才展开：选择界面里展示和调整的仍是质变本身，与玩家质变"获得时移除自身"一致。
		// 选择中途的检查点存的是未展开的集合，读档重进本阶段会在这里得到同样的展开结果。
		finalMonsterHexes = HextechEnemyTransmute.ExpandForRun(runState, modifier, $"act-{actIndex}", finalMonsterHexes);
		modifier.SetMonsterHexesForAct(actIndex, finalMonsterHexes);
		modifier.SetStageResolved(actIndex, true);
		modifier.ApplyMapModifiersToCurrentAct(nameof(HandleStageSelection), actIndex);
		HextechEnemyUi.Refresh(modifier);
		await modifier.ApplyToCurrentEnemiesIfNeeded();
		await PersistActSelection(runState, actIndex);
	}

	private static async Task<bool> WaitForSelectionBlockingOverlaysToClear(RunState runState, int actIndex, string reason)
	{
		for (int frame = 0; IsCurrentRun(runState); frame++)
		{
			object? topOverlay = NOverlayStack.Instance?.Peek();
			if (!IsSelectionBlockingOverlay(topOverlay, out string overlayName))
			{
				return true;
			}

			if (frame % OverlayWaitLogIntervalFrames == 0)
			{
				HextechLog.Info("Mayhem", $"HandleHextechActSelection waiting: act={actIndex} reason={reason} topOverlay={overlayName} frame={frame}");
			}

			await WaitForProcessFrameOrDelayAsync();
		}

		HextechLog.Info("Mayhem", $"HandleHextechActSelection abort: run changed while waiting for overlays act={actIndex} reason={reason}");
		return false;
	}

	private static bool IsSelectionBlockingOverlay(object? overlay, out string overlayName)
	{
		if (overlay == null || overlay is HextechRuneSelectionScreen)
		{
			overlayName = overlay?.GetType().FullName ?? "null";
			return false;
		}

		Type overlayType = overlay.GetType();
		overlayName = overlayType.FullName ?? overlayType.Name;
		if (overlay is IOverlayScreen { ScreenType: NetScreenType.Rewards } or NCardRewardSelectionScreen)
		{
			return true;
		}

		// 第三方模组的奖励类界面不一定声明 NetScreenType.Rewards,也没有可依赖的公共基类,
		// 只能按类型名兜底识别;原版奖励界面已由上面的类型判断覆盖。
		return overlayName.Contains("Reward", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task PersistActSelection(RunState runState, int actIndex)
	{
		try
		{
			if (!IsCurrentRunInProgress(runState) || RunManager.Instance.NetService.Type == NetGameType.Replay)
			{
				return;
			}

			await SaveManager.Instance.SaveRun(SelectPreFinishedRoomForSave(runState.CurrentRoom), saveProgress: false);
			HextechLog.Info("Mayhem", $"PersistActSelection: requested save after resolving act={actIndex} room={runState.CurrentRoom?.GetType().Name ?? "null"}");
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"PersistActSelection failed: act={actIndex} error={ex}");
		}
	}

	/// <summary>
	/// 存档时当前房若是已完成的事件房（先古/涅奥结束、点“继续”后才开始本幕选择），必须作为 preFinishedRoom 写入；
	/// 传 null 时原版 LoadIntoLatestMapCoord 会按地图坐标重建房间，把先古当新事件重开，而存档里本幕已决，玩家就再也看不到海克斯选择。
	/// 与原版 EventRoom 在事件全部完成时 SaveRun(this) 同口径；其他房间照旧传 null。
	/// </summary>
	internal static AbstractRoom? SelectPreFinishedRoomForSave(AbstractRoom? currentRoom)
	{
		return currentRoom is EventRoom { IsPreFinished: true } ? currentRoom : null;
	}

	/// <summary>
	/// 本局仍是当前局且不在原版退出清理中。原版 RunManager.CleanUp（保存并退出/放弃，0.107.1–0.111.0 一致）
	/// 先置 ShouldSave=false、IsCleaningUp=true，再清覆盖层，最后才清 State；清覆盖层会取消等待中的选择界面，
	/// 让本局任务链在 State 仍指向本局时同步续跑。只比较 State 引用会把正在拆除的对局当成当前局继续推进本幕。
	/// </summary>
	private static bool IsCurrentRunInProgress(RunState runState)
	{
		RunManager runManager = RunManager.Instance;
		return IsRunInProgress(runManager.DebugOnlyGetState(), runState, runManager.IsCleaningUp);
	}

	internal static bool IsRunInProgress(object? currentRunState, object runState, bool isCleaningUp)
	{
		return !isCleaningUp && ReferenceEquals(currentRunState, runState);
	}
}
