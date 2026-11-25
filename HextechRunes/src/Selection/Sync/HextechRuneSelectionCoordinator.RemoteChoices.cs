using System.Collections;
using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using static HextechRunes.HextechHookReflection;
using static HextechRunes.HextechSelectionHelpers;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	// 原版 0.111.0:PlayerChoiceSynchronizer 私有字段 List<ReceivedChoice> _receivedChoices 缓存先到的远端选择,
	// 私有嵌套结构 ReceivedChoice 含 public 字段 senderId/choiceId/completionSource。用于取走在本端开始等待前
	// 已到达的选择;任一成员缺失时进启动摘要并退化为只走 PlayerChoiceReceived 事件。
	private const BindingFlags BufferedChoiceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly FieldInfo? ReceivedChoicesField = TryGetField(
		typeof(PlayerChoiceSynchronizer),
		"_receivedChoices",
		BindingFlags.Instance | BindingFlags.NonPublic);
	private static readonly Type? ReceivedChoiceType = ReceivedChoicesField?.FieldType.GetGenericArguments().FirstOrDefault();
	private static readonly FieldInfo? ReceivedChoiceSenderIdField = TryGetReceivedChoiceField("senderId");
	private static readonly FieldInfo? ReceivedChoiceChoiceIdField = TryGetReceivedChoiceField("choiceId");
	private static readonly FieldInfo? ReceivedChoiceCompletionSourceField = TryGetReceivedChoiceField("completionSource");
	private static readonly PropertyInfo? ReceivedChoiceTaskProperty = ReceivedChoiceCompletionSourceField?.FieldType.GetProperty(
		"Task",
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly bool BufferedChoiceReflectionAvailable = ValidateBufferedChoiceReflection();
	private static readonly TimeSpan RemoteChoiceStillWaitingLogInterval = TimeSpan.FromSeconds(30);

	/// <summary>
	/// 等待远端玩家的指定 choiceId。与原版一样不设超时:只在本局结束、联机断开、shouldRemainActive 返回 false
	/// 或取消时以 OperationCanceledException 退出;收到的载荷不满足 isExpected 时按协议失败处理。
	/// </summary>
	internal static async Task<(PlayerChoiceResult Result, uint ChoiceId)> WaitForRemoteHextechChoice(
		PlayerChoiceSynchronizer synchronizer,
		RunState runState,
		Player player,
		uint choiceId,
		Func<PlayerChoiceResult, bool> isExpected,
		string context,
		Func<bool>? shouldRemainActive = null,
		CancellationToken cancellationToken = default)
	{
		(PlayerChoiceResult remoteChoice, uint receivedChoiceId) = await WaitForRemoteChoiceByEvent(
			synchronizer,
			runState,
			player,
			choiceId,
			context,
			shouldRemainActive,
			cancellationToken);
		if (isExpected(remoteChoice))
		{
			return (remoteChoice, receivedChoiceId);
		}

		throw CreateProtocolFailure(
			context,
			$"Unexpected choice payload context={context} player={player.NetId} " +
			$"choiceId={choiceId} type={remoteChoice.ChoiceType} result={remoteChoice}");
	}

	internal static uint SyncLocalHextechChoice(
		PlayerChoiceSynchronizer synchronizer,
		Player player,
		uint choiceId,
		PlayerChoiceResult result,
		string context)
	{
		try
		{
			synchronizer.SyncLocalChoice(player, choiceId, result);
			return choiceId;
		}
		catch (Exception ex)
		{
			throw CreateProtocolFailure(
				context,
				$"Failed to send local choice context={context} player={player.NetId} choiceId={choiceId}",
				ex);
		}
	}

	private static async Task<(PlayerChoiceResult Result, uint ChoiceId)> WaitForRemoteChoiceByEvent(
		PlayerChoiceSynchronizer synchronizer,
		RunState runState,
		Player player,
		uint choiceId,
		string context,
		Func<bool>? shouldRemainActive,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfSelectionTransactionInactive(runState, shouldRemainActive, context);

		if (TryTakeBufferedRemoteChoice(synchronizer, player, choiceId, out NetPlayerChoiceResult bufferedResult))
		{
			HextechLog.Info("Mayhem", $"RemoteChoice event wait: consumed buffered choice context={context} player={player.NetId} choiceId={choiceId}");
			return (PlayerChoiceResult.FromNetData(player, runState, bufferedResult), choiceId);
		}

		TaskCompletionSource<(uint ChoiceId, NetPlayerChoiceResult Result)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		void OnPlayerChoiceReceived(Player receivedPlayer, uint receivedChoiceId, NetPlayerChoiceResult result)
		{
			if (receivedPlayer.NetId != player.NetId)
			{
				return;
			}

			if (receivedChoiceId == choiceId)
			{
				completion.TrySetResult((receivedChoiceId, result));
			}
		}

		synchronizer.PlayerChoiceReceived += OnPlayerChoiceReceived;
		try
		{
			if (TryTakeBufferedRemoteChoice(synchronizer, player, choiceId, out NetPlayerChoiceResult lateBufferedResult))
			{
				HextechLog.Info("Mayhem", $"RemoteChoice event wait: consumed late buffered choice context={context} player={player.NetId} choiceId={choiceId}");
				return (PlayerChoiceResult.FromNetData(player, runState, lateBufferedResult), choiceId);
			}

			Task<(uint ChoiceId, NetPlayerChoiceResult Result)> waitTask = completion.Task;
			string waitDescription = $"context={context} player={player.NetId} choiceId={choiceId}";
			while (!waitTask.IsCompleted)
			{
				using CancellationTokenSource observerCancellation =
					CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				Task interrupted = WaitForSelectionTransactionInterruptionAsync(
					runState,
					shouldRemainActive,
					waitDescription,
					observerCancellation.Token);
				Task winner = await Task.WhenAny(waitTask, interrupted);
				observerCancellation.Cancel();
				ObserveCompletion(interrupted, $"{context} interruption observer");
				if (winner != waitTask)
				{
					// 观察者只在取消或事务失效时结束;续体执行前状态又恢复时继续等待同一个 choiceId。
					cancellationToken.ThrowIfCancellationRequested();
					ThrowIfSelectionTransactionInactive(runState, shouldRemainActive, context);
				}
			}

			(uint receivedChoiceId, NetPlayerChoiceResult result) = await waitTask;
			TryTakeBufferedRemoteChoice(synchronizer, player, receivedChoiceId, out _);
			HextechLog.Info("Mayhem", $"RemoteChoice event wait: received choice context={context} player={player.NetId} expectedChoiceId={choiceId} receivedChoiceId={receivedChoiceId}");
			return (PlayerChoiceResult.FromNetData(player, runState, result), receivedChoiceId);
		}
		finally
		{
			synchronizer.PlayerChoiceReceived -= OnPlayerChoiceReceived;
		}
	}

	private static void ThrowIfSelectionTransactionInactive(
		RunState runState,
		Func<bool>? shouldRemainActive,
		string context)
	{
		if (!IsCurrentRun(runState)
			|| !HextechPlayerContextHelper.IsMultiplayerConnected()
			|| shouldRemainActive?.Invoke() == false)
		{
			throw new OperationCanceledException(
				$"Multiplayer choice transaction is no longer active: {context}");
		}
	}

	private static async Task WaitForSelectionTransactionInterruptionAsync(
		RunState runState,
		Func<bool>? shouldRemainActive,
		string waitDescription,
		CancellationToken cancellationToken)
	{
		// 按墙钟计时:联机计时类模组会加速帧,按帧数打日志会失真。
		DateTimeOffset nextStillWaitingLog = DateTimeOffset.UtcNow + RemoteChoiceStillWaitingLogInterval;
		while (!cancellationToken.IsCancellationRequested
			&& IsCurrentRun(runState)
			&& HextechPlayerContextHelper.IsMultiplayerConnected()
			&& shouldRemainActive?.Invoke() != false)
		{
			if (DateTimeOffset.UtcNow >= nextStillWaitingLog)
			{
				HextechLog.Warn("Mayhem", $"WaitForRemoteHextechChoice: still waiting {waitDescription}");
				nextStillWaitingLog = DateTimeOffset.UtcNow + RemoteChoiceStillWaitingLogInterval;
			}

			await WaitForProcessFrameOrDelayAsync(cancellationToken);
		}
	}

	private static void ObserveCompletion(Task task, string context)
	{
		_ = ObserveCompletionAsync(task, context);
	}

	private static async Task ObserveCompletionAsync(Task task, string context)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			HextechLog.Error("Mayhem", $"Background choice observer failed: context={context} error={ex}");
		}
	}

	internal static void AbortMultiplayerChoiceTransaction(string context, string reason)
	{
		try
		{
			if (HextechPlayerContextHelper.IsMultiplayerConnected())
			{
				HextechLog.Error("Mayhem", $"Aborting multiplayer choice transaction: context={context} reason={reason}");
				RunManager.Instance.NetService.Disconnect(NetError.InternalError, now: true);
			}
		}
		catch (Exception disconnectError)
		{
			HextechLog.Error("Mayhem", $"Failed to abort multiplayer choice transaction: context={context} error={disconnectError}");
		}
	}

	/// <summary>
	/// 联机选择协议失败的唯一出口:断开本次联机事务、记录一次原因(含内层异常),返回待抛出的异常。
	/// 调用方直接 <c>throw CreateProtocolFailure(...)</c>,不要再自行记录同一条错误。
	/// </summary>
	internal static HextechChoiceProtocolException CreateProtocolFailure(
		string context,
		string reason,
		Exception? innerException = null)
	{
		AbortMultiplayerChoiceTransaction(context, innerException == null ? reason : $"{reason}: {innerException}");
		return innerException == null
			? new HextechChoiceProtocolException(reason)
			: new HextechChoiceProtocolException(reason, innerException);
	}

	private static bool TryTakeBufferedRemoteChoice(
		PlayerChoiceSynchronizer synchronizer,
		Player player,
		uint choiceId,
		out NetPlayerChoiceResult result)
	{
		result = default;
		try
		{
			if (!TryGetBufferedChoices(synchronizer, out IList? receivedChoices))
			{
				return false;
			}

			foreach ((int index, ulong senderId, uint bufferedChoiceId, Task<NetPlayerChoiceResult> task) in EnumerateBufferedChoices(receivedChoices))
			{
				if (senderId != player.NetId || bufferedChoiceId != choiceId)
				{
					continue;
				}

				result = task.Result;
				receivedChoices.RemoveAt(index);
				return true;
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"RemoteChoice buffered read failed: player={player.NetId} choiceId={choiceId} error={ex}");
		}

		return false;
	}

	private static FieldInfo? TryGetReceivedChoiceField(string name)
	{
		return ReceivedChoiceType == null
			? null
			: TryGetField(ReceivedChoiceType, name, BufferedChoiceFieldFlags);
	}

	private static bool TryGetBufferedChoices(PlayerChoiceSynchronizer synchronizer, [NotNullWhen(true)] out IList? receivedChoices)
	{
		receivedChoices = ReceivedChoicesField?.GetValue(synchronizer) as IList;
		return receivedChoices != null;
	}

	private static IEnumerable<(int Index, ulong SenderId, uint ChoiceId, Task<NetPlayerChoiceResult> Task)> EnumerateBufferedChoices(IList receivedChoices)
	{
		if (!BufferedChoiceReflectionAvailable)
		{
			yield break;
		}

		// BufferedChoiceReflectionAvailable 为 true 时四个成员都已解析。
		FieldInfo senderIdField = ReceivedChoiceSenderIdField!;
		FieldInfo choiceIdField = ReceivedChoiceChoiceIdField!;
		FieldInfo completionSourceField = ReceivedChoiceCompletionSourceField!;
		PropertyInfo taskProperty = ReceivedChoiceTaskProperty!;

		for (int i = 0; i < receivedChoices.Count; i++)
		{
			object? entry = receivedChoices[i];
			if (entry == null
				|| senderIdField.GetValue(entry) is not ulong senderId
				|| choiceIdField.GetValue(entry) is not uint choiceId)
			{
				continue;
			}

			object? completionSource = completionSourceField.GetValue(entry);
			if (completionSource == null
				|| taskProperty.GetValue(completionSource) is not Task<NetPlayerChoiceResult> task
				|| !task.IsCompletedSuccessfully)
			{
				continue;
			}

			yield return (i, senderId, choiceId, task);
		}
	}

	private static bool ValidateBufferedChoiceReflection()
	{
		if (ReceivedChoiceType == null)
		{
			HextechLog.Warn("Mayhem", "RemoteChoice buffered reflection unavailable: could not resolve ReceivedChoice type; using event path.");
			return false;
		}

		if (ReceivedChoiceTaskProperty == null)
		{
			HextechLog.Warn("Mayhem", "RemoteChoice buffered reflection unavailable: could not resolve completionSource.Task; using event path.");
			return false;
		}

		return ReceivedChoiceSenderIdField != null
			&& ReceivedChoiceChoiceIdField != null
			&& ReceivedChoiceCompletionSourceField != null;
	}

}

internal sealed class HextechChoiceProtocolException : InvalidOperationException
{
	internal HextechChoiceProtocolException(string message)
		: base(message)
	{
	}

	internal HextechChoiceProtocolException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
