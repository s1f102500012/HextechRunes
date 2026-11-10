using MegaCrit.Sts2.Core.Context;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	private static async Task SynchronizeActSelectionApplied(
		RunState runState,
		PlayerChoiceSynchronizer synchronizer,
		int actIndex,
		int choiceOrdinal,
		CancellationToken cancellationToken)
	{
		RunManager runManager = RunManager.Instance;
		List<Task> pendingAcks = [];
		foreach (Player player in runState.Players)
		{
			cancellationToken.ThrowIfCancellationRequested();
			uint choiceId = synchronizer.ReserveChoiceId(player);
			if (IsLocalPlayer(runManager, player))
			{
				uint sentChoiceId = SyncLocalHextechChoice(
					synchronizer,
					player,
					choiceId,
					HextechChoiceCodec.CreateActSelectionApplied(actIndex, choiceOrdinal),
					$"act-selection-applied act={actIndex} ordinal={choiceOrdinal}");
				HextechLog.Info("Mayhem", $"ActSelectionApplied sync local: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} choiceId={sentChoiceId}");
				continue;
			}

			pendingAcks.Add(WaitForRemoteActSelectionApplied(
				synchronizer,
				runState,
				player,
				choiceId,
				actIndex,
				choiceOrdinal,
				cancellationToken));
		}

		if (pendingAcks.Count == 0)
		{
			return;
		}

		HextechLog.Info("Mayhem", $"ActSelectionApplied waiting: act={actIndex} ordinal={choiceOrdinal} remoteCount={pendingAcks.Count}");
		await Task.WhenAll(pendingAcks);
		HextechLog.Info("Mayhem", $"ActSelectionApplied complete: act={actIndex} ordinal={choiceOrdinal}");
	}

	private static async Task WaitForRemoteActSelectionApplied(
		PlayerChoiceSynchronizer synchronizer,
		RunState runState,
		Player player,
		uint choiceId,
		int actIndex,
		int choiceOrdinal,
		CancellationToken cancellationToken)
	{
		// isExpected 已完整解码校验;等待只会在它返回 true 时结束,不再二次解码。
		(_, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			runState,
			player,
			choiceId,
			result => HextechChoiceCodec.TryDecodeActSelectionApplied(result, actIndex, choiceOrdinal),
			$"act-selection-applied act={actIndex} ordinal={choiceOrdinal}",
			cancellationToken: cancellationToken);

		HextechLog.Info("Mayhem", $"ActSelectionApplied remote: act={actIndex} ordinal={choiceOrdinal} player={player.NetId} choiceId={receivedChoiceId}");
	}

	internal static PlayerChoiceSynchronizer RequirePlayerChoiceSynchronizer(RunManager runManager)
	{
		return runManager.PlayerChoiceSynchronizer
			?? throw CreateProtocolFailure(
				"player-choice-synchronizer",
				"PlayerChoiceSynchronizer is unavailable during an active multiplayer transaction.");
	}

	internal static bool IsLocalPlayer(RunManager runManager, Player player)
	{
		return LocalContext.IsMe(player)
			|| (player.NetId != 0UL && player.NetId == runManager.NetService.NetId);
	}
}
