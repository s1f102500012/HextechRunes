using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using static HextechRunes.HextechRunePoolBuilder;
using static HextechRunes.HextechSelectionHelpers;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	// 单人流程；联机的每幕选择统一走 SelectRuneMultiplayer。
	private static async Task<RuneSelectionResult> SelectRune(
		HextechMayhemModifier modifier,
		Player player,
		int actIndex,
		int choiceOrdinal,
		IReadOnlyList<RelicModel> options,
		RelicModel? monsterHexRelic,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null)
	{
		HextechRuneSelectionScreen screen = await CreateLocalRuneSelectionScreenAsync(
			modifier,
			player,
			actIndex,
			choiceOrdinal,
			options,
			monsterHexRelic,
			enemyHexOptions,
			offeredOptionIds: null,
			CancellationToken.None);
		RelicModel? selectedRelic = (await screen.RelicsSelected()).FirstOrDefault();
		if (selectedRelic != null)
		{
			// 自选模式的最终候选不经过重随,这里补记;普通三选一的候选已在展示/重随时记过,重复记录无副作用。
			modifier.RecordSeenPlayerRunes(player, screen.CurrentRelics);
		}

		return new RuneSelectionResult(selectedRelic, HextechWeightedRuneOptions.Copy(screen.CurrentRelics), screen.RerollHistory.Count, screen.CurrentMonsterHexes);
	}

	/// <summary>
	/// 本机玩家的符文选择界面(单机与联机共用):记“已见”、建金色重掷会话、推入界面。
	/// 单机推进原版 Niche 随机流,展示和重随时立即写入存档的已见记录;
	/// 联机(传入 offeredOptionIds)用各端一致的稳定哈希,本次展示过的候选(含被重随替换的)只累积到 offeredOptionIds,
	/// 确认后随选择结果同步,由两端经 CommitSeenRuneSelection 统一写入存档。
	/// </summary>
	private static async Task<HextechRuneSelectionScreen> CreateLocalRuneSelectionScreenAsync(
		HextechMayhemModifier modifier,
		Player player,
		int actIndex,
		int choiceOrdinal,
		IReadOnlyList<RelicModel> options,
		RelicModel? monsterHexRelic,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions,
		HashSet<ModelId>? offeredOptionIds,
		CancellationToken cancellationToken)
	{
		MarkRelicsSeen(options);
		if (offeredOptionIds == null)
		{
			modifier.RecordSeenPlayerRunes(player, options);
		}

		HashSet<ModelId> seenOptionIds = CreateSeenOptionIds(options, modifier.GetSeenPlayerRuneIds(player));
		// 本幕品级在幕开始时已定并存档（联机为房主同步值）；保底格刷新按它抽，不受其他格刷新的影响。
		HextechRarityTier? stageRarity = modifier.GetRarityForAct(actIndex);
		HextechGoldenRerollSession goldenReroll = CreateGoldenRerollSession(
			modifier,
			player,
			actIndex,
			choiceOrdinal,
			options);
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>> rerollFunc = offeredOptionIds is { } multiplayerOfferedOptionIds
			? (relics, slotIndex, rerollOrdinal) => RerollSingleOptionAndTrackMultiplayer(
				modifier,
				player,
				relics,
				slotIndex,
				actIndex,
				rerollOrdinal,
				seenOptionIds,
				multiplayerOfferedOptionIds,
				stageRarity,
				GetGoldenRerollOverride(goldenReroll))
			: (relics, slotIndex, rerollOrdinal) => RerollSingleOptionAndTrack(
				modifier,
				player,
				relics,
				slotIndex,
				seenOptionIds,
				stageRarity,
				GetGoldenRerollOverride(goldenReroll),
				rerollOrdinal);
		return await CreateRuneSelectionScreenAsync(
			options,
			monsterHexRelic,
			rerollFunc,
			enemyHexOptions,
			modifier.PlayerRuneRerollLimit,
			goldenRerollSession: goldenReroll,
			cancellationToken: cancellationToken,
			selfPickPool: BuildSelfPickPool(modifier, player, options));
	}

	private static async Task<RuneSelectionResult> SelectRuneMultiplayer(
		HextechMayhemModifier modifier,
		PendingRuneSelection selection,
		PlayerChoiceSynchronizer synchronizer,
		int actIndex,
		int choiceOrdinal,
		RelicModel? monsterHexRelic,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null,
		Func<HextechRuneSelectionScreen, Task>? afterLocalSelection = null,
		Action<HextechRuneSelectionScreen>? screenCreated = null,
		Func<Task?>? getConcurrentTask = null,
		CancellationToken cancellationToken = default)
	{
		string context = $"rune-choice act={actIndex} ordinal={choiceOrdinal}";
		cancellationToken.ThrowIfCancellationRequested();
		if (selection.IsLocal)
		{
			// 初始候选在 pending 阶段已由各端按同一种子记为已见;这里只累积本次展示过的完整候选,确认后统一提交。
			HashSet<ModelId> offeredOptionIds = CreateSeenOptionIds(selection.Options);
			HextechRuneSelectionScreen screen = await CreateLocalRuneSelectionScreenAsync(
				modifier,
				selection.Player,
				actIndex,
				choiceOrdinal,
				selection.Options,
				monsterHexRelic,
				enemyHexOptions,
				offeredOptionIds,
				cancellationToken);
			screenCreated?.Invoke(screen);
			RelicModel? selectedRelic;
			try
			{
				Task<IEnumerable<RelicModel>> localSelection = screen.RelicsSelected(removeOverlay: false);
				selectedRelic = (await WaitForSelectionWithConcurrentFailure(
					localSelection,
					getConcurrentTask?.Invoke(),
					context,
					cancellationToken)).FirstOrDefault();
			}
			catch (OperationCanceledException)
			{
				if (HextechPlayerContextHelper.IsMultiplayerConnected())
				{
					uint canceledChoiceId = SyncLocalHextechChoice(
						synchronizer,
						selection.Player,
						selection.ChoiceId,
						CreateRuneChoiceResult(actIndex, choiceOrdinal, screen, offeredOptionIds, selectedRelic: null),
						context);
					HextechLog.Info("Mayhem", $"RuneChoice sync canceled: act={actIndex} ordinal={choiceOrdinal} player={selection.Player.NetId} choiceId={canceledChoiceId}");
				}

				throw;
			}

			uint sentChoiceId = SyncLocalHextechChoice(
				synchronizer,
				selection.Player,
				selection.ChoiceId,
				CreateRuneChoiceResult(actIndex, choiceOrdinal, screen, offeredOptionIds, selectedRelic),
				context);
			HextechLog.Info("Mayhem", $"RuneChoice sync local: act={actIndex} ordinal={choiceOrdinal} player={selection.Player.NetId} choiceId={sentChoiceId}");
			HextechSelectionProgress.MarkCompleted(selection.Player.NetId);
			_ = RequireCompletedSelection(
				selectedRelic,
				$"local {context} player={selection.Player.NetId} choiceId={sentChoiceId}");
			// "已见"存在 Modifier 上、不参与原版联机校验,两端一致靠本模组保证:本机与远端 ResolveRemoteRuneChoice 都在确认后按同步的完整候选历史写入。
			CommitSeenRuneSelection(modifier, selection.Player, offeredOptionIds, screen.CurrentRelics);

			if (afterLocalSelection != null)
			{
				await afterLocalSelection(screen).WaitAsync(cancellationToken);
			}

			return new RuneSelectionResult(selectedRelic, HextechWeightedRuneOptions.Copy(screen.CurrentRelics), screen.RerollHistory.Count, screen.CurrentMonsterHexes);
		}

		HextechLog.Info("Mayhem", $"RuneChoice wait remote: act={actIndex} ordinal={choiceOrdinal} player={selection.Player.NetId} choiceId={selection.ChoiceId}");
		// 解码结果在 isExpected 回调里捕获:等待只会在它返回 true 时结束,之后不再二次解码。
		RuneSelectionPayload? decoded = null;
		(_, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			(RunState)selection.Player.RunState,
			selection.Player,
			selection.ChoiceId,
			result => HextechChoiceCodec.TryDecodeRuneSelection(result, actIndex, choiceOrdinal, out decoded),
			context,
			cancellationToken: cancellationToken);
		HextechLog.Info("Mayhem", $"RuneChoice remote received: act={actIndex} ordinal={choiceOrdinal} player={selection.Player.NetId} choiceId={receivedChoiceId}");
		HextechSelectionProgress.MarkCompleted(selection.Player.NetId);
		return ResolveRemoteRuneChoice(modifier, selection.Player, actIndex, choiceOrdinal, decoded!);
	}

	/// <summary>等待覆盖层栈就绪后创建并推入选择界面;符文、仅敌方、"无候选"与锻造三选一共用。</summary>
	internal static async Task<HextechRuneSelectionScreen> CreateRuneSelectionScreenAsync(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc = null,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null,
		int playerRuneRerollLimit = 1,
		string? titleOverride = null,
		HextechGoldenRerollSession? goldenRerollSession = null,
		CancellationToken cancellationToken = default,
		IReadOnlyList<RelicModel>? selfPickPool = null,
		bool continueOnly = false,
		HextechSelectionMetadataMode metadataMode = HextechSelectionMetadataMode.PlayerRune)
	{
		await WaitForSingletonAsync(static () => NOverlayStack.Instance, cancellationToken: cancellationToken);
		HextechRuneSelectionScreen selectionScreen = HextechRuneSelectionScreen.Create(
			relics,
			monsterHexRelic,
			rerollFunc,
			enemyHexOptions,
			playerRuneRerollLimit,
			titleOverride,
			metadataMode,
			goldenRerollSession,
			selfPickPool,
			continueOnly);
		await CloseMapBeforeOwnScreenAsync(selectionScreen);

		if (NOverlayStack.Instance == null)
		{
			throw new InvalidOperationException("NOverlayStack is not available for hextech selection.");
		}

		NOverlayStack.Instance.Push(selectionScreen);
		enemyHexOptions?.ScreenCreated?.Invoke(selectionScreen);
		return selectionScreen;
	}

	/// <summary>
	/// 本稀有度已无任何可选海克斯(未见的和见过没选的都抽完、其余全部拥有/互斥/禁用):只给说明和"继续"按钮。
	/// 纯本机界面,不抽取、不同步、不发放;联机时各端对"无候选"的判断一致,不需要额外协议。
	/// </summary>
	private static async Task ShowNoRuneOptionsScreenAsync(CancellationToken cancellationToken = default)
	{
		HextechRuneSelectionScreen? screen = null;
		try
		{
			screen = await CreateRuneSelectionScreenAsync(
				[],
				null,
				titleOverride: new LocString(HextechRuneLabels.LocTable, "HEXTECH_NO_RUNE_OPTIONS_TITLE").GetRawText(),
				cancellationToken: cancellationToken,
				continueOnly: true);
			// 联机批次被取消(断线、换局)时不能一直等玩家点继续。
			Task completed = await Task.WhenAny(
				screen.RelicsSelected(removeOverlay: false),
				Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
			cancellationToken.ThrowIfCancellationRequested();
			await completed;
		}
		finally
		{
			if (screen != null)
			{
				await screen.DismissAfterSelectionComplete();
			}
		}
	}

	/// <summary>
	/// 玩家海克斯重随次数为无限时,选择界面改为自选:列出本次候选稀有度的全部合法海克斯。
	/// 合法池与重随同一套规则(配置启用、版本可用、本幕允许、角色可用、已拥有与互斥排除),不排除"已见",不消耗随机数。
	/// 只在本机构造给界面用;远端只收到最终候选、本次展示历史与选中序号,按 ID 还原,不需要这份池。
	/// </summary>
	private static IReadOnlyList<RelicModel>? BuildSelfPickPool(HextechMayhemModifier modifier, Player player, IReadOnlyList<RelicModel> options)
	{
		if (modifier.PlayerRuneRerollLimit != HextechRuneConfiguration.InfiniteRerollLimit || options.Count == 0)
		{
			return null;
		}

		try
		{
			HextechRarityTier rarity = GetRarityForOptions(options);
			List<RelicModel> pool = BuildSelectableRunePool(player, rarity, (RunState)player.RunState)
				.Select(relic => CreateSelectableRuneOption(player, relic))
				.ToList();
			return pool.Count > 0 ? pool : null;
		}
		catch (Exception ex)
		{
			// 自选池只影响本机界面:构造失败就记错误并退回普通的三选一界面,不阻断本幕选择。
			HextechLog.Error("Mayhem", $"Self-pick pool unavailable, falling back to regular choices: player={player.NetId} error={ex}");
			return null;
		}
	}

	private static PlayerChoiceResult CreateRuneChoiceResult(int actIndex, int choiceOrdinal, HextechRuneSelectionScreen screen, IEnumerable<ModelId> offeredOptionIds, RelicModel? selectedRelic)
	{
		int selectedIndex = IndexOfRelicInstance(screen.CurrentRelics, selectedRelic);
		HextechLog.Info("Mayhem", $"CreateRuneChoiceResult: act={actIndex} ordinal={choiceOrdinal} selectedIndex={selectedIndex} rerolls={string.Join(",", screen.RerollHistory)}");
		return HextechChoiceCodec.CreateRuneSelection(actIndex, choiceOrdinal, selectedIndex, screen.RerollHistory, screen.CurrentRelics, offeredOptionIds);
	}

	/// <summary>
	/// 联机确认后把本次展示过的完整候选(含被重随替换的)与最终候选写入存档的已见记录。
	/// 自选模式的最终单候选也计入;本次历史与重随排除集合独立,排除池耗尽时的清空不会抹掉历史。
	/// </summary>
	private static void CommitSeenRuneSelection(
		HextechMayhemModifier modifier,
		Player player,
		IEnumerable<ModelId> offeredOptionIds,
		IReadOnlyList<RelicModel> finalOptions)
	{
		IEnumerable<ModelId> ids = offeredOptionIds.Concat(finalOptions.Select(static relic => relic.CanonicalId()));
		modifier.RecordSeenPlayerRunes(player, ids.Distinct().Select(static id => ModelDb.GetById<RelicModel>(id)));
	}

	private static RuneSelectionResult ResolveRemoteRuneChoice(
		HextechMayhemModifier modifier,
		Player player,
		int actIndex,
		int choiceOrdinal,
		RuneSelectionPayload decoded)
	{
		int selectedIndex = decoded.SelectedIndex;
		IReadOnlyList<ModelId> syncedOptionIds = decoded.FinalOptionIds;
		IReadOnlyList<ModelId> offeredOptionIds = decoded.SeenOptionIds;
		if (syncedOptionIds.Count == 0)
		{
			string message =
				$"Rune selection payload omitted authoritative final options: act={actIndex} " +
				$"ordinal={choiceOrdinal} player={player.NetId}";
			throw CreateProtocolFailure($"rune-choice act={actIndex} ordinal={choiceOrdinal}", message);
		}

		if (!AreRegisteredPlayerRuneIds(syncedOptionIds))
		{
			string message =
				$"Rune selection payload contains non-hextech rune options: act={actIndex} ordinal={choiceOrdinal} " +
				$"player={player.NetId} ids={string.Join(",", syncedOptionIds.Select(static id => id.ToString()))}";
			throw CreateProtocolFailure($"rune-choice act={actIndex} ordinal={choiceOrdinal}", message);
		}

		if (!AreRegisteredPlayerRuneIds(offeredOptionIds))
		{
			string message =
				$"Rune selection payload contains non-hextech seen history: act={actIndex} ordinal={choiceOrdinal} " +
				$"player={player.NetId} ids={string.Join(",", offeredOptionIds.Select(static id => id.ToString()))}";
			throw CreateProtocolFailure($"rune-choice act={actIndex} ordinal={choiceOrdinal}", message);
		}

		if (!TryCreateSyncedRuneOptions(player, syncedOptionIds, actIndex, choiceOrdinal, out List<RelicModel> syncedOptions))
		{
			string message =
				$"Failed to load authoritative rune options: act={actIndex} ordinal={choiceOrdinal} " +
				$"player={player.NetId} ids={string.Join(",", syncedOptionIds.Select(static id => id.Entry))}";
			throw CreateProtocolFailure($"rune-choice act={actIndex} ordinal={choiceOrdinal}", message);
		}

		if (!HextechGeneratedRuneDataCodec.Restore(decoded.GeneratedRuneData, syncedOptions))
		{
			throw CreateProtocolFailure("generated rune data", "Invalid or missing generated rune recipe.");
		}

		if (decoded.CharacterWeightPercent is not int characterWeightPercent)
		{
			throw CreateProtocolFailure("character rune weight", "Rune selection omitted a valid final character weight.");
		}

		syncedOptions = new HextechWeightedRuneOptions(syncedOptions, characterWeightPercent);

		if (selectedIndex < -1 || selectedIndex >= syncedOptions.Count)
		{
			string message =
				$"Invalid rune selection index: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} " +
				$"index={selectedIndex} optionCount={syncedOptions.Count}";
			throw CreateProtocolFailure($"rune-choice act={actIndex} ordinal={choiceOrdinal}", message);
		}

		MarkRelicsSeen(syncedOptions);
		RelicModel syncedSelectedRelic = RequireCompletedSelection(
			selectedIndex >= 0 ? syncedOptions[selectedIndex] : null,
			$"remote rune-choice act={actIndex} ordinal={choiceOrdinal} player={player.NetId}");
		CommitSeenRuneSelection(modifier, player, offeredOptionIds, syncedOptions);
		HextechLog.Info("Mayhem", $"ResolveRemoteRuneChoice: player={player.NetId} selectedIndex={selectedIndex} rerolls={string.Join(",", decoded.RerollHistory)} syncedOptions={string.Join(",", syncedOptions.Select(static o => o.CanonicalId().Entry))}");
		return new RuneSelectionResult(syncedSelectedRelic, syncedOptions, decoded.RerollHistory.Count);
	}

	private static async Task<T> WaitForSelectionWithConcurrentFailure<T>(
		Task<T> selectionTask,
		Task? concurrentTask,
		string context,
		CancellationToken cancellationToken)
	{
		if (concurrentTask == null)
		{
			return await selectionTask.WaitAsync(cancellationToken);
		}

		using CancellationTokenSource monitorCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task concurrentFailure = WaitForFailureAsync(concurrentTask, monitorCancellation.Token);
		Task<T> cancelableSelection = selectionTask.WaitAsync(cancellationToken);
		try
		{
			Task winner = await Task.WhenAny(cancelableSelection, concurrentFailure);
			if (winner == concurrentFailure)
			{
				await concurrentFailure;
			}

			return await cancelableSelection;
		}
		finally
		{
			monitorCancellation.Cancel();
			ObserveCompletion(concurrentFailure, $"{context} concurrent task monitor");
		}
	}

	private static async Task WaitForFailureAsync(Task task, CancellationToken cancellationToken)
	{
		await task.WaitAsync(cancellationToken);
		await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
	}

	/// <summary>
	/// 远端同步来的候选必须全部是已登记的海克斯玩家符文(内置或外部 API 登记):候选会原样发放给各端,
	/// 畸形或恶意载荷不能借此让各端发放任意原版遗物。稀有度不做一致性校验——金色重掷会把单个槽位升一档。
	/// </summary>
	internal static bool AreRegisteredPlayerRuneIds(IReadOnlyList<ModelId> optionIds)
	{
		return optionIds.Count > 0
			&& optionIds.All(static id => HextechCatalog.TryGetPlayerRuneRarityById(id, out _));
	}

	private static bool TryCreateSyncedRuneOptions(
		Player player,
		IReadOnlyList<ModelId> optionIds,
		int actIndex,
		int choiceOrdinal,
		out List<RelicModel> options)
	{
		options = new(optionIds.Count);
		try
		{
			foreach (ModelId id in optionIds)
			{
				RelicModel relic = ModelDb.GetById<RelicModel>(id);
				options.Add(CreateSelectableRuneOption(player, relic));
			}

			return options.Count > 0;
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"ResolveRemoteRuneChoice: failed to load synced option model: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} ids={string.Join(",", optionIds)} error={ex}");
			options.Clear();
			return false;
		}
	}
}
