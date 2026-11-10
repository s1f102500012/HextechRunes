using static HextechRunes.HextechSelectionHelpers;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	private static EnemyHexAdjustmentSyncContext CreateEnemyHexAdjustmentSyncContext(
		PlayerChoiceSynchronizer synchronizer,
		Player authorityPlayer,
		int actIndex,
		IReadOnlyList<MonsterHexKind> initialMonsterHexes)
	{
		uint choiceId = synchronizer.ReserveChoiceId(authorityPlayer);
		HextechLog.Info("Mayhem", $"EnemyHexAdjustmentSync: reserved act={actIndex} authority={authorityPlayer.NetId} choiceId={choiceId}");
		return new EnemyHexAdjustmentSyncContext(synchronizer, authorityPlayer, choiceId, actIndex, initialMonsterHexes);
	}

	private static HextechEnemyHexAdjustmentOptions? CreateEnemyHexAdjustmentOptionsForSelection(
		HextechMayhemModifier modifier,
		RunManager runManager,
		RunState runState,
		int actIndex,
		HextechRarityTier rarity,
		IReadOnlyList<MonsterHexKind> activeMonsterHexes,
		IReadOnlyList<MonsterHexKind> initialNewMonsterHexes,
		EnemyHexAdjustmentSyncContext? syncContext,
		PendingRuneSelection selection,
		CancellationToken cancellationToken)
	{
		if (!selection.IsLocal || (syncContext == null && activeMonsterHexes.Count == 0))
		{
			return null;
		}

		bool isAuthorityLocal = syncContext != null && IsLocalPlayer(runManager, syncContext.AuthorityPlayer);
		// choiceOrdinal>0 时无新 hex 可调整(syncContext 为 null):只读展示【本幕新增】的敌方 hex。
		// 不能回退到 activeMonsterHexes(前几幕累积集),否则会把历史敌方海克斯一起显示;
		// 本幕无新增时面板按空集隐藏。纯显示、不触发任何同步。
		return CreateEnemyHexAdjustmentOptions(
			modifier,
			rarity,
			runState,
			actIndex,
			syncContext?.CurrentMonsterHexes ?? initialNewMonsterHexes,
			controlsEnabled: isAuthorityLocal,
			syncContext,
			cancellationToken);
	}

	/// <summary>
	/// 敌方海克斯调整面板的选项。controlsEnabled 的一端可重掷并(联机时)把每次改动同步出去;
	/// 联机中其余端只读,改为接收权威端的调整。单机、联机、仅敌方三处共用。
	/// </summary>
	private static HextechEnemyHexAdjustmentOptions CreateEnemyHexAdjustmentOptions(
		HextechMayhemModifier modifier,
		HextechRarityTier rarity,
		RunState runState,
		int actIndex,
		IReadOnlyList<MonsterHexKind> initialHexes,
		bool controlsEnabled,
		EnemyHexAdjustmentSyncContext? syncContext,
		CancellationToken cancellationToken)
	{
		HashSet<MonsterHexKind> seenEnemyHexes = modifier.GetKnownMonsterHexes().ToHashSet();
		seenEnemyHexes.UnionWith(initialHexes);
		return new HextechEnemyHexAdjustmentOptions
		{
			InitialHexes = initialHexes,
			RerollLimit = modifier.MonsterHexRerollLimit,
			ControlsEnabled = controlsEnabled,
			RerollFunc = controlsEnabled
				? (currentHexes, slotIndex, rerollOrdinal) => RerollEnemyHexForAct(
					modifier,
					rarity,
					runState,
					actIndex,
					GetMonsterHexSlot(currentHexes, slotIndex),
					rerollOrdinal,
					CreateEnemyHexRerollExcludedIds(currentHexes, slotIndex),
					seenEnemyHexes)
				: null,
			Changed = controlsEnabled && syncContext != null
				? (monsterHexes, rerollCounts) => SendEnemyHexAdjustment(syncContext, monsterHexes, rerollCounts, isFinal: false)
				: null,
			ScreenCreated = !controlsEnabled && syncContext != null
				? screen => syncContext.RemoteReceiveTask = ReceiveEnemyHexAdjustments(
					syncContext,
					runState,
					screen,
					payload => IsValidEnemyHexAdjustment(
						payload,
						syncContext.InitialMonsterHexes,
						HextechMonsterHexRoller.GetConfiguredRarityPool(rarity, modifier.DisabledMonsterHexIdsForPool),
						modifier.MonsterHexRerollLimit),
					cancellationToken)
				: null
		};
	}

	/// <summary>
	/// 远端敌方调整载荷的内容校验(解码只保证格式):槽位数与重掷计数长度等于本次调整的槽位数;
	/// 每个非空槽位要么是开局给出的海克斯,要么是本稀有度按本局配置可重掷到的海克斯;
	/// 有限重掷上限时每槽重掷次数不超过上限。
	/// </summary>
	internal static bool IsValidEnemyHexAdjustment(
		EnemyHexAdjustmentPayload payload,
		IReadOnlyList<MonsterHexKind> initialHexes,
		IReadOnlyCollection<MonsterHexKind> rerollCandidates,
		int rerollLimit)
	{
		int slotCount = initialHexes.Count;
		if (payload.MonsterHexes.Count != slotCount || payload.RerollCounts.Count != slotCount)
		{
			return false;
		}

		for (int i = 0; i < slotCount; i++)
		{
			if (rerollLimit != HextechRuneConfiguration.InfiniteRerollLimit && payload.RerollCounts[i] > rerollLimit)
			{
				return false;
			}

			if (payload.MonsterHexes[i] is MonsterHexKind hex
				&& !initialHexes.Contains(hex)
				&& !rerollCandidates.Contains(hex))
			{
				return false;
			}
		}

		return true;
	}

	private static async Task CompleteLocalEnemyHexAdjustmentSync(RunManager runManager, EnemyHexAdjustmentSyncContext? syncContext, HextechRuneSelectionScreen screen)
	{
		if (syncContext == null)
		{
			return;
		}

		if (IsLocalPlayer(runManager, syncContext.AuthorityPlayer))
		{
			SendEnemyHexAdjustment(syncContext, screen.CurrentMonsterHexSlots, screen.EnemyHexRerollCounts, isFinal: true);
			return;
		}

		if (syncContext.RemoteReceiveTask != null)
		{
			await syncContext.RemoteReceiveTask;
		}
	}

	private static void SendEnemyHexAdjustment(
		EnemyHexAdjustmentSyncContext syncContext,
		IReadOnlyList<MonsterHexKind?> monsterHexes,
		IReadOnlyList<int> rerollCounts,
		bool isFinal)
	{
		if (syncContext.FinalSent)
		{
			return;
		}

		try
		{
			List<MonsterHexKind?> nextMonsterHexes = monsterHexes.ToList();
			List<int> nextRerollCounts = rerollCounts.ToList();
			EnemyHexAdjustmentPayload payload = new(
				syncContext.ActIndex,
				syncContext.Sequence,
				nextMonsterHexes.ToArray(),
				nextRerollCounts.ToArray(),
				isFinal);
			int operationToken = GetEnemyHexAdjustmentOperationToken(syncContext);
			uint sentChoiceId = SyncLocalHextechChoice(
				syncContext.Synchronizer,
				syncContext.AuthorityPlayer,
				syncContext.NextChoiceId,
				HextechChoiceCodec.CreateEnemyHexAdjustment(operationToken, payload),
				$"enemy-hex-adjustment act={syncContext.ActIndex}");

			syncContext.CurrentMonsterHexSlots.Clear();
			syncContext.CurrentMonsterHexSlots.AddRange(nextMonsterHexes);
			syncContext.RerollCounts.Clear();
			syncContext.RerollCounts.AddRange(nextRerollCounts);
			HextechLog.Info("Mayhem", $"EnemyHexAdjustmentSync send: act={syncContext.ActIndex} choiceId={sentChoiceId} seq={syncContext.Sequence} hexes={string.Join(",", syncContext.CurrentMonsterHexSlots.Select(static hex => hex?.ToString() ?? "None"))} rerolls={string.Join(",", syncContext.RerollCounts)} final={isFinal}");
			if (isFinal)
			{
				syncContext.FinalSent = true;
				return;
			}

			syncContext.Sequence++;
			syncContext.NextChoiceId = syncContext.Synchronizer.ReserveChoiceId(syncContext.AuthorityPlayer);
		}
		catch (HextechChoiceProtocolException)
		{
			throw;
		}
		catch (Exception ex)
		{
			string message =
				$"Enemy hex adjustment failed after reserving choice: act={syncContext.ActIndex} " +
				$"player={syncContext.AuthorityPlayer.NetId} choiceId={syncContext.NextChoiceId} " +
				$"sequence={syncContext.Sequence}";
			throw CreateProtocolFailure($"enemy-hex-adjustment act={syncContext.ActIndex}", message, ex);
		}
	}

	private static async Task ObserveEnemyHexAdjustmentReceiveTask(EnemyHexAdjustmentSyncContext? syncContext)
	{
		if (syncContext?.RemoteReceiveTask is not Task receiveTask)
		{
			return;
		}

		try
		{
			await receiveTask;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			HextechLog.Error(
				"Mayhem", $"Enemy hex adjustment receiver failed during transaction cleanup: " +
				$"act={syncContext.ActIndex} error={ex}");
		}
	}

	private static async Task ReceiveEnemyHexAdjustments(
		EnemyHexAdjustmentSyncContext syncContext,
		RunState runState,
		HextechRuneSelectionScreen screen,
		Func<EnemyHexAdjustmentPayload, bool> isValidPayload,
		CancellationToken cancellationToken)
	{
		while (screen.IsInsideTree() && !cancellationToken.IsCancellationRequested)
		{
			int operationToken = GetEnemyHexAdjustmentOperationToken(syncContext);
			// 解码结果在 isExpected 回调里捕获:等待只会在它返回 true 时结束,之后不再二次解码。
			EnemyHexAdjustmentPayload payload = default;
			(_, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
				syncContext.Synchronizer,
				runState,
				syncContext.AuthorityPlayer,
				syncContext.NextChoiceId,
				choice => HextechChoiceCodec.TryDecodeEnemyHexAdjustment(
					choice,
					operationToken,
					syncContext.ActIndex,
					syncContext.Sequence,
					out payload),
				$"enemy-hex-adjustment act={syncContext.ActIndex}",
				() => screen.IsInsideTree(),
				cancellationToken);
			if (!isValidPayload(payload))
			{
				throw CreateProtocolFailure(
					$"enemy-hex-adjustment act={syncContext.ActIndex}",
					$"Invalid enemy hex adjustment: act={syncContext.ActIndex} choiceId={receivedChoiceId} " +
					$"hexes={string.Join(",", payload.MonsterHexes.Select(static hex => hex?.ToString() ?? "None"))} rerolls={string.Join(",", payload.RerollCounts)}");
			}

			syncContext.CurrentMonsterHexSlots.Clear();
			syncContext.CurrentMonsterHexSlots.AddRange(payload.MonsterHexes);
			syncContext.RerollCounts.Clear();
			syncContext.RerollCounts.AddRange(payload.RerollCounts);
			syncContext.Sequence = payload.Sequence + 1;
			screen.ApplyEnemyHexAdjustment(payload.MonsterHexes, payload.RerollCounts);
			HextechLog.Info("Mayhem", $"EnemyHexAdjustmentSync receive: act={syncContext.ActIndex} choiceId={receivedChoiceId} seq={payload.Sequence} hexes={string.Join(",", payload.MonsterHexes.Select(static hex => hex?.ToString() ?? "None"))} rerolls={string.Join(",", payload.RerollCounts)} final={payload.IsFinal}");
			if (payload.IsFinal)
			{
				screen.CompleteEnemyOnlySelection();
				return;
			}

			syncContext.NextChoiceId = syncContext.Synchronizer.ReserveChoiceId(syncContext.AuthorityPlayer);
		}
	}

	private static int GetEnemyHexAdjustmentOperationToken(EnemyHexAdjustmentSyncContext syncContext)
	{
		return HextechChoiceCodec.ComputeOperationToken(
			"enemy-hex-adjustment",
			syncContext.NextChoiceId,
			syncContext.AuthorityPlayer.NetId,
			$"act={syncContext.ActIndex};sequence={syncContext.Sequence}");
	}
}
