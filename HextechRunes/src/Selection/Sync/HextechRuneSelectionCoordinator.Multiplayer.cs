using MegaCrit.Sts2.Core.Saves;
using static HextechRunes.HextechRunePoolBuilder;
using static HextechRunes.HextechSelectionHelpers;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	private static async Task<IReadOnlyList<MonsterHexKind>> SelectRunesForAllPlayersMultiplayer(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		HextechRarityTier rarity,
		IReadOnlyList<MonsterHexKind> previousMonsterHexes,
		IReadOnlyList<MonsterHexKind> initialNewMonsterHexes,
		RelicModel? monsterHexRelic,
		int choiceOrdinal,
		bool allowEnemyHexAdjustment,
		HashSet<ulong> playersNotifiedNoOptions)
	{
		RunManager runManager = RunManager.Instance;
		IReadOnlyList<MonsterHexKind> initialActiveMonsterHexes = HextechMonsterHexRoller.CombineActiveHexes(previousMonsterHexes, initialNewMonsterHexes);
		PlayerChoiceSynchronizer synchronizer = RequirePlayerChoiceSynchronizer(runManager);

		List<PendingRuneSelection> pendingSelections = [];
		List<ResolvedRuneSelection> resolvedSelections = [];
		bool localHasNoOptions = CollectRuneSelections(
			runState,
			modifier,
			runManager,
			synchronizer,
			actIndex,
			rarity,
			choiceOrdinal,
			playersNotifiedNoOptions,
			pendingSelections,
			resolvedSelections);

		// 本轮开始：没有候选、已直接结算的玩家算作已完成。
		HextechSelectionProgress.Reset(resolvedSelections.Select(static resolved => resolved.Player.NetId));

		// 敌方调整由固定的权威玩家在自己的选择界面里完成;他本次没有候选(不会弹选择界面)时,
		// 其余客户端会一直等他的调整结果,所以这种情况下本幕不开放敌方调整。各端对 pending 的判断一致。
		Player? enemyHexAuthority = GetActRollAuthorityPlayer(runManager, runState);
		EnemyHexAdjustmentSyncContext? enemyHexSync =
			allowEnemyHexAdjustment
			&& initialNewMonsterHexes.Count > 0
			&& enemyHexAuthority != null
			&& pendingSelections.Any(selection => selection.Player == enemyHexAuthority)
				? CreateEnemyHexAdjustmentSyncContext(
					synchronizer,
					enemyHexAuthority,
					actIndex,
					initialNewMonsterHexes)
				: null;

		RuneSelectionResult[] selectedRelics = [];
		List<HextechRuneSelectionScreen> blockingScreens = [];
		using CancellationTokenSource batchCancellation = new();
		async Task<RuneSelectionResult> RunSelection(PendingRuneSelection selection)
		{
			try
			{
				return await SelectRuneMultiplayer(
					modifier,
					selection,
					synchronizer,
					actIndex,
					choiceOrdinal,
					monsterHexRelic,
					CreateEnemyHexAdjustmentOptionsForSelection(
						modifier,
						runManager,
						runState,
						actIndex,
						rarity,
						initialActiveMonsterHexes,
						initialNewMonsterHexes,
						enemyHexSync,
						selection,
						batchCancellation.Token),
					screen => CompleteLocalEnemyHexAdjustmentSync(runManager, enemyHexSync, screen),
					blockingScreens.Add,
					() => enemyHexSync?.RemoteReceiveTask,
					batchCancellation.Token);
			}
			catch
			{
				batchCancellation.Cancel();
				throw;
			}
		}

		try
		{
			Task<RuneSelectionResult>[] selectionTasks = pendingSelections
				.Select(RunSelection)
				.ToArray();
			// "继续"界面与其他玩家的选择并行显示;任一选择失败时 RunSelection 会取消批次,界面随之关闭。
			Task noOptionsScreen = localHasNoOptions
				? ShowNoRuneOptionsScreenAsync(batchCancellation.Token)
				: Task.CompletedTask;
			await Task.WhenAll(selectionTasks.Cast<Task>().Append(noOptionsScreen));
			selectedRelics = selectionTasks.Select(static task => task.Result).ToArray();
			for (int i = 0; i < pendingSelections.Count; i++)
			{
				PendingRuneSelection selection = pendingSelections[i];
				RuneSelectionResult selectedResult = selectedRelics[i];
				RelicModel selectedRelic = RequireCompletedSelection(
					selectedResult.SelectedRelic,
					$"multiplayer choice record act={actIndex} ordinal={choiceOrdinal} player={selection.Player.NetId}");
				ModelId selectedId = selectedRelic.CanonicalId();
				modifier.RecordRuneSelectionJournalSelection(
					actIndex,
					choiceOrdinal,
					selection.Player.NetId,
					selectedId, (selectedRelic as IHextechGeneratedRune)?.ExportSelectionData() ?? "");
				resolvedSelections.Add(new ResolvedRuneSelection(selection.Player, selectedRelic, Applied: false));
				modifier.CommitCharacterRuneWeight(selection.Player, selectedResult.FinalOptions);
				modifier.RecordRuneChoice(actIndex, rarity, selection.Player, selectedResult.FinalOptions, selectedRelic, selectedResult.RerollCount, choiceOrdinal);
			}

			IReadOnlyList<MonsterHexKind> resolvedMonsterHexes = enemyHexSync != null
				? HextechMonsterHexRoller.CombineActiveHexes(previousMonsterHexes, enemyHexSync.CurrentMonsterHexes)
				: initialActiveMonsterHexes;
			modifier.SetMonsterHexesForAct(actIndex, resolvedMonsterHexes);
			await PersistRuneSelectionCheckpoint(runState, actIndex, choiceOrdinal);

			await ObtainResolvedRuneSelectionsAsync(runState, modifier, actIndex, choiceOrdinal, resolvedSelections);

			await SynchronizeActSelectionApplied(
				runState,
				synchronizer,
				actIndex,
				choiceOrdinal,
				batchCancellation.Token);

			return resolvedMonsterHexes;
		}
		catch (OperationCanceledException)
		{
			batchCancellation.Cancel();
			throw;
		}
		catch (HextechChoiceProtocolException)
		{
			// CreateProtocolFailure 已断开联机并记录原因。
			batchCancellation.Cancel();
			throw;
		}
		catch (Exception ex)
		{
			batchCancellation.Cancel();
			string message =
				$"Multiplayer rune selection transaction failed: act={actIndex} " +
				$"ordinal={choiceOrdinal}";
			HextechLog.Error("Mayhem", $"{message}: {ex}");
			AbortMultiplayerChoiceTransaction(
				$"rune-choice act={actIndex} ordinal={choiceOrdinal}",
				message);
			throw;
		}
		finally
		{
			batchCancellation.Cancel();
			await ObserveEnemyHexAdjustmentReceiveTask(enemyHexSync);
			await DismissBlockingSelectionScreens(blockingScreens);
		}
	}

	private readonly record struct ResolvedRuneSelection(Player Player, RelicModel SelectedRelic, bool Applied);

	/// <summary>
	/// 为本次选择收集每名玩家的状态:读档/遥测可恢复的已决选择进 resolvedSelections,其余生成候选并预留
	/// choiceId 进 pendingSelections;没有候选的玩家跳过。返回本机玩家是否需要看一次“没有可选”的界面。
	/// </summary>
	private static bool CollectRuneSelections(
		RunState runState,
		HextechMayhemModifier modifier,
		RunManager runManager,
		PlayerChoiceSynchronizer synchronizer,
		int actIndex,
		HextechRarityTier rarity,
		int choiceOrdinal,
		HashSet<ulong> playersNotifiedNoOptions,
		List<PendingRuneSelection> pendingSelections,
		List<ResolvedRuneSelection> resolvedSelections)
	{
		bool localHasNoOptions = false;
		foreach (Player player in runState.Players)
		{
			bool hasJournalEntry = modifier.TryGetRuneSelectionJournalEntry(
				actIndex,
				choiceOrdinal,
				player.NetId,
				out HextechRuneSelectionJournalEntry journalEntry);
			if (!hasJournalEntry)
			{
				hasJournalEntry = modifier.TryRecoverRuneSelectionJournalEntryFromChoiceHistory(
					actIndex,
					choiceOrdinal,
					player,
					out journalEntry);
				if (hasJournalEntry)
				{
					HextechLog.Info(
						"Mayhem", $"RuneChoice journal rebuilt from saved choice history: " +
						$"act={actIndex} ordinal={choiceOrdinal} player={player.NetId}");
				}
			}

			if (hasJournalEntry)
			{
				RelicModel recoveredRelic = ModelDb.GetById<RelicModel>(journalEntry.SelectedId).ToMutable();
				if (!journalEntry.Applied && recoveredRelic is IHextechGeneratedRune generated
					&& !generated.TryImportSelectionData(journalEntry.SelectionData))
				{
					throw CreateProtocolFailure("generated rune recovery", "Checkpoint omitted a valid generated recipe.");
				}

				// Applied 是不可重放的提交边界；符文之后可能自我消耗、替换或被其他机制移除，
				// 因此当前背包缺席不能反证当时未成功发放。
				resolvedSelections.Add(new ResolvedRuneSelection(player, recoveredRelic, journalEntry.Applied));
				HextechLog.Info(
					"Mayhem", $"RuneChoice journal recovered: act={actIndex} " +
					$"ordinal={choiceOrdinal} player={player.NetId} " +
					$"relic={journalEntry.SelectedId.Category}:{journalEntry.SelectedId.Entry} " +
					$"applied={journalEntry.Applied}");
				continue;
			}

			// 玩家候选只排除本局已见过的符文;敌方持有的海克斯照样可以出现在玩家候选里。
			HashSet<ModelId> excludedIds = modifier.GetSeenPlayerRuneIds(player);
			List<RelicModel> options = BuildStableSelectableRunesForRarity(
				player,
				rarity,
				runState,
				actIndex,
				excludedIds,
				useEndlessTagWindow: modifier.IsEndlessLoopActive);
			options = ApplyStageGuaranteedPick(runState, player, actIndex, choiceOrdinal, excludedIds, options);
			if (options.Count == 0)
			{
				HextechLog.Warn("Mayhem", $"No rune options for player={player.NetId} act={actIndex} ordinal={choiceOrdinal} rarity={rarity}; skipping this selection.");
				// 各端对"无候选"的判断一致,都跳过这名玩家的同步选择;本机玩家额外看到一次"继续"界面(纯本机,不同步)。
				if (IsLocalPlayer(runManager, player) && playersNotifiedNoOptions.Add(player.NetId))
				{
					localHasNoOptions = true;
				}

				continue;
			}

			MarkRelicsSeen(options);
			modifier.RecordSeenPlayerRunes(player, options);

			uint choiceId = synchronizer.ReserveChoiceId(player);
			pendingSelections.Add(new PendingRuneSelection(player, options, choiceId, IsLocalPlayer(runManager, player)));
			HextechLog.Info("Mayhem", $"RuneChoice pending: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} choiceId={choiceId} local={IsLocalPlayer(runManager, player)} options={string.Join(",", options.Select(o => o.CanonicalId().Entry))}");
		}

		return localHasNoOptions;
	}

	/// <summary>按选择日志逐个发放;Applied 是不可重放的提交边界,发放失败时中止联机事务。</summary>
	private static async Task ObtainResolvedRuneSelectionsAsync(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		int choiceOrdinal,
		IReadOnlyList<ResolvedRuneSelection> resolvedSelections)
	{
		foreach ((Player player, RelicModel selectedRelic, bool applied) in resolvedSelections)
		{
			if (!IsCurrentRun(runState) || !HextechPlayerContextHelper.IsMultiplayerConnected())
			{
				throw new OperationCanceledException(
					$"Rune obtain transaction became inactive: act={actIndex} "
					+ $"ordinal={choiceOrdinal} player={player.NetId}");
			}

			ModelId selectedId = selectedRelic.CanonicalId();
			bool currentlyOwned = selectedRelic is IHextechGeneratedRune generatedSelection
				? player.Relics.OfType<IHextechGeneratedRune>().Any(owned => owned.ExportSelectionData() == generatedSelection.ExportSelectionData())
				: PlayerHasRelicId(player, selectedId);
			// Applied 后不再重放(自我消耗的符文离开背包也不补发);未 Applied 但已在背包里说明发放已越过
			// 提交边界,只补记 Applied,不能再发一次。
			if (applied || currentlyOwned)
			{
				if (!applied)
				{
					modifier.MarkRuneSelectionJournalApplied(
						actIndex,
						choiceOrdinal,
						player.NetId,
						selectedId);
				}
				continue;
			}

			try
			{
				await RelicCmd.Obtain(selectedRelic, player);
				modifier.MarkRuneSelectionJournalApplied(
					actIndex,
					choiceOrdinal,
					player.NetId,
					selectedId);
			}
			catch (Exception ex)
			{
				// 发放中途抛错时遗物可能已进背包;以背包为准记下提交边界,记录和断线由批次外层统一处理。
				if (player.Relics.Any(relic => ReferenceEquals(relic, selectedRelic)))
				{
					modifier.MarkRuneSelectionJournalApplied(
						actIndex,
						choiceOrdinal,
						player.NetId,
						selectedId);
				}

				string message = $"Rune obtain failed: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} relic={selectedId.Category}:{selectedId.Entry}";
				HextechLog.Warn("Mayhem", message);
				// 外层把取消异常当作正常退出、不断线;发放已开始后的取消仍要断线,否则两端提交边界可能不同。
				if (ex is OperationCanceledException)
				{
					AbortMultiplayerChoiceTransaction(
						$"rune-choice act={actIndex} ordinal={choiceOrdinal}",
						message);
				}

				throw;
			}
		}

	}

	private static bool PlayerHasRelicId(Player player, ModelId expectedId)
	{
		return player.Relics.Any(relic =>
		{
			ModelId actualId = relic.CanonicalId();
			return actualId == expectedId;
		});
	}

	private static async Task PersistRuneSelectionCheckpoint(
		RunState runState,
		int actIndex,
		int choiceOrdinal)
	{
		try
		{
			if (!IsCurrentRun(runState) || !HextechPlayerContextHelper.IsMultiplayerConnected())
			{
				throw new OperationCanceledException(
					$"RuneChoice checkpoint canceled because the multiplayer transaction is inactive: "
					+ $"act={actIndex} ordinal={choiceOrdinal}");
			}

			// 先古结束后开始的选择要带上已完成的事件房，否则读这份检查点会把先古当新事件重开。
			await SaveManager.Instance.SaveRun(SelectPreFinishedRoomForSave(runState.CurrentRoom), saveProgress: false);
			HextechLog.Info(
				"Mayhem", $"RuneChoice checkpoint saved: " +
				$"act={actIndex} ordinal={choiceOrdinal}");
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException(
				$"RuneChoice checkpoint save failed before relic obtain: act={actIndex} ordinal={choiceOrdinal}",
				ex);
		}
	}

	private static async Task DismissBlockingSelectionScreens(IEnumerable<HextechRuneSelectionScreen> screens)
	{
		foreach (HextechRuneSelectionScreen screen in screens.Distinct())
		{
			try
			{
				await screen.DismissAfterSelectionComplete();
			}
			catch (Exception ex)
			{
				HextechLog.Warn("Mayhem", $"Failed to dismiss blocking rune selection screen: {ex}");
			}
		}
	}

}
