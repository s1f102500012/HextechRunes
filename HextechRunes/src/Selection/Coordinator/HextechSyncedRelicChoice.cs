namespace HextechRunes;

/// <summary>
/// 锻造选择与遗物选项选择共用的“本地选、同步下标、远端按同一候选还原”事务。
/// 两种选择只差消息类型、日志标签和本地界面;候选由各端按同一稳定输入生成,远端核对候选 ID 逐个相同后
/// 直接返回本端的同位置候选实例,与本地分支的返回值口径一致。
/// </summary>
internal static class HextechSyncedRelicChoice
{
	internal sealed record ChoiceKind(
		HextechRelicChoiceKind CodecKind,
		string LogTag,
		string OperationKind,
		string ContextPrefix,
		string Description);

	internal static readonly ChoiceKind Forge = new(
		HextechRelicChoiceKind.Forge,
		"ForgeChoice",
		"forge-selection",
		"forge-choice",
		"forge");

	internal static readonly ChoiceKind RelicOption = new(
		HextechRelicChoiceKind.RelicOption,
		"RelicOptionChoice",
		"relic-option-selection",
		"relic-option-choice",
		"relic option");

	internal static async Task<RelicModel?> SelectAsync(
		ChoiceKind kind,
		Player player,
		IReadOnlyList<RelicModel> options,
		string context,
		bool syncMultiplayerChoice,
		Func<Task<RelicModel?>> selectLocal)
	{
		RunManager runManager = RunManager.Instance;
		if (HextechPlayerContextHelper.IsSinglePlayerFlow(runManager.NetService.Type))
		{
			return await selectLocal();
		}

		if (!syncMultiplayerChoice)
		{
			if (HextechRuneSelectionCoordinator.IsLocalPlayer(runManager, player))
			{
				return await selectLocal();
			}

			HextechLog.Warn(kind.LogTag, $"Unsynced {kind.Description} selection ignored for remote player={player.NetId} context={context}");
			return null;
		}

		PlayerChoiceSynchronizer synchronizer = HextechRuneSelectionCoordinator.RequirePlayerChoiceSynchronizer(runManager);
		uint choiceId = synchronizer.ReserveChoiceId(player);
		int operationToken = HextechChoiceCodec.ComputeOperationToken(
			kind.OperationKind,
			choiceId,
			player.NetId,
			context);
		string protocolContext = $"{kind.ContextPrefix} {context}";
		if (HextechRuneSelectionCoordinator.IsLocalPlayer(runManager, player))
		{
			return await SelectLocalAndSyncAsync(kind, player, options, context, runManager, synchronizer, choiceId, operationToken, protocolContext, selectLocal);
		}

		HextechLog.Info(kind.LogTag, $"Wait remote: player={player.NetId} choiceId={choiceId} context={context}");
		// 解码与候选核对都在 isExpected 里完成:等待只会在它返回 true 时结束,之后直接用捕获的下标。
		int selectedIndex = -1;
		(_, uint receivedChoiceId) = await HextechRuneSelectionCoordinator.WaitForRemoteHextechChoice(
			synchronizer,
			(RunState)player.RunState,
			player,
			choiceId,
			choice => HextechChoiceCodec.TryDecodeRelicChoice(kind.CodecKind, choice, operationToken, out selectedIndex, out List<ModelId> optionIds)
				&& HextechChoiceCodec.MatchesOptionIds(optionIds, options),
			protocolContext);
		HextechLog.Info(kind.LogTag, $"Remote received: player={player.NetId} choiceId={receivedChoiceId} context={context}");
		return ResolveRemoteChoice(kind, player, options, selectedIndex, context, protocolContext);
	}

	private static async Task<RelicModel?> SelectLocalAndSyncAsync(
		ChoiceKind kind,
		Player player,
		IReadOnlyList<RelicModel> options,
		string context,
		RunManager runManager,
		PlayerChoiceSynchronizer synchronizer,
		uint choiceId,
		int operationToken,
		string protocolContext,
		Func<Task<RelicModel?>> selectLocal)
	{
		try
		{
			RelicModel? selected = await selectLocal();
			if (selected == null)
			{
				uint canceledChoiceId = HextechRuneSelectionCoordinator.SyncLocalHextechChoice(
					synchronizer,
					player,
					choiceId,
					HextechChoiceCodec.CreateRelicChoice(kind.CodecKind, operationToken, selectedIndex: -1, options),
					protocolContext);
				HextechLog.Info(kind.LogTag, $"Local selection canceled: player={player.NetId} choiceId={canceledChoiceId} context={context}");
				return null;
			}

			int selectedIndex = HextechSelectionHelpers.IndexOfRelicById(options, selected);
			if (selectedIndex < 0)
			{
				string message = $"Local {kind.Description} selection is not in the synchronized option set: player={player.NetId} context={context}";
				throw HextechRuneSelectionCoordinator.CreateProtocolFailure(protocolContext, message);
			}

			if (!runManager.NetService.IsConnected)
			{
				throw new OperationCanceledException(
					$"Local {kind.Description} selection ended after multiplayer disconnected: player={player.NetId} context={context}");
			}

			uint sentChoiceId = HextechRuneSelectionCoordinator.SyncLocalHextechChoice(
				synchronizer,
				player,
				choiceId,
				HextechChoiceCodec.CreateRelicChoice(kind.CodecKind, operationToken, selectedIndex, options),
				protocolContext);
			HextechLog.Info(kind.LogTag, $"Sync local: player={player.NetId} choiceId={sentChoiceId} index={selectedIndex} context={context}");
			return selected;
		}
		catch (HextechChoiceProtocolException)
		{
			throw;
		}
		catch (OperationCanceledException) when (!runManager.NetService.IsConnected)
		{
			throw;
		}
		catch (Exception ex)
		{
			string message =
				$"Local {kind.Description} transaction failed after reserving choice: " +
				$"player={player.NetId} choiceId={choiceId} context={context}";
			throw HextechRuneSelectionCoordinator.CreateProtocolFailure(protocolContext, message, ex);
		}
	}

	private static RelicModel? ResolveRemoteChoice(
		ChoiceKind kind,
		Player player,
		IReadOnlyList<RelicModel> expectedOptions,
		int selectedIndex,
		string context,
		string protocolContext)
	{
		if (selectedIndex == -1)
		{
			HextechLog.Info(kind.LogTag, $"Remote selection canceled: player={player.NetId} context={context}");
			return null;
		}

		// 协议失败统一交给 CreateProtocolFailure:它断开联机并记录一次原因,这里不再重复写错误日志。
		if (selectedIndex < 0 || selectedIndex >= expectedOptions.Count)
		{
			throw HextechRuneSelectionCoordinator.CreateProtocolFailure(
				protocolContext,
				$"Invalid {kind.Description} selected index: player={player.NetId} index={selectedIndex} count={expectedOptions.Count} context={context}");
		}

		// 候选 ID 已逐个核对,返回本端同位置的候选实例,与本地分支返回 options 中实例的口径一致。
		return expectedOptions[selectedIndex];
	}
}
