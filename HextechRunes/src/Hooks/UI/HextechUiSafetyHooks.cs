using System.Collections;
using Godot;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Relics;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechUiSafetyHooks
{
	// 原版 NMultiplayerPlayerIntentHandler 的私有字段(0.107.1 / 0.111.0)。
	private static readonly FieldInfo? IntentCardInPlayAwaitingPlayerChoiceField = TryGetField(typeof(NMultiplayerPlayerIntentHandler), "_cardInPlayAwaitingPlayerChoice");
	private static readonly FieldInfo? IntentCardThinkyDotsField = TryGetField(typeof(NMultiplayerPlayerIntentHandler), "_cardThinkyDots");
	private static readonly FieldInfo? IntentHitboxField = TryGetField(typeof(NMultiplayerPlayerIntentHandler), "_hitbox");
	private static readonly FieldInfo? IntentCardIntentField = TryGetField(typeof(NMultiplayerPlayerIntentHandler), "_cardIntent");
	private static readonly FieldInfo? IntentIsInPlayerChoiceField = TryGetField(typeof(NMultiplayerPlayerIntentHandler), "_isInPlayerChoice");

	// 原版 NCardPlayQueue._playQueue(List<QueueItem>)与私有嵌套类 QueueItem 的公开字段 card/action/currentTween(同上版本)。
	private static readonly FieldInfo? PlayQueueField = TryGetField(typeof(NCardPlayQueue), "_playQueue");
	private static readonly Type? QueueItemType = TryGetNestedType(typeof(NCardPlayQueue), "QueueItem");
	private static readonly FieldInfo? QueueItemCardField = QueueItemType == null ? null : TryGetField(QueueItemType, "card", BindingFlags.Instance | BindingFlags.Public);
	private static readonly FieldInfo? QueueItemActionField = QueueItemType == null ? null : TryGetField(QueueItemType, "action", BindingFlags.Instance | BindingFlags.Public);
	private static readonly FieldInfo? QueueItemTweenField = QueueItemType == null ? null : TryGetField(QueueItemType, "currentTween", BindingFlags.Instance | BindingFlags.Public);
	private static readonly MethodInfo? BeforeRemoteCardPlayResumedMethod = TryGetMethod(
		typeof(NCardPlayQueue),
		"BeforeRemoteCardPlayResumedAfterPlayerChoice",
		BindingFlags.Instance | BindingFlags.NonPublic,
		typeof(GameAction));
	private static readonly MethodInfo? TweenAllToQueuePositionMethod = TryGetMethod(
		typeof(NCardPlayQueue),
		"TweenAllToQueuePosition",
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	// 原版 GameAction.BeforeResumedAfterPlayerChoice 事件的 backing field(同上版本)。
	private static readonly FieldInfo? BeforeResumedAfterPlayerChoiceEventField = TryGetField(
		typeof(GameAction),
		"BeforeResumedAfterPlayerChoice",
		BindingFlags.Instance | BindingFlags.NonPublic);

	private static async Task PlayNewlyAcquiredAnimationSafely(Task original, NRelicInventoryHolder self)
	{
		try
		{
			await original;
		}
		catch (NullReferenceException) when (!IsNodeUsable(self))
		{
			LogRelicAnimationSkipped("holder-left-tree");
		}
		catch (ObjectDisposedException) when (!GodotObject.IsInstanceValid(self))
		{
			LogRelicAnimationSkipped("holder-disposed");
		}
	}

	private static bool IsNodeUsable(Node node)
	{
		return GodotObject.IsInstanceValid(node) && node.IsInsideTree();
	}

	/// <summary>持有者展示的是不是海克斯遗物;节点已释放或模型未设置时视为不是(交给原版)。</summary>
	private static bool HoldsHextechRelic(NRelicInventoryHolder holder)
	{
		if (!GodotObject.IsInstanceValid(holder) || holder.Relic is not { } relicNode || !GodotObject.IsInstanceValid(relicNode))
		{
			return false;
		}

		try
		{
			return HextechCatalog.IsHextechCustomRelic(relicNode.Model);
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	private static bool TryFindQueueItem(NCardPlayQueue queue, GameAction action, out IList? playQueue, out int index, out object? item, out NCard? card)
	{
		playQueue = PlayQueueField?.GetValue(queue) as IList;
		index = -1;
		item = null;
		card = null;
		if (playQueue == null || QueueItemActionField == null || QueueItemCardField == null)
		{
			return false;
		}

		for (int i = 0; i < playQueue.Count; i++)
		{
			object? candidate = playQueue[i];
			if (candidate == null || !ReferenceEquals(QueueItemActionField.GetValue(candidate), action))
			{
				continue;
			}

			index = i;
			item = candidate;
			card = QueueItemCardField.GetValue(candidate) as NCard;
			return true;
		}

		return false;
	}

	private static bool HasUsableParent(Node node)
	{
		if (!GodotObject.IsInstanceValid(node))
		{
			return false;
		}

		Node? parent = node.GetParent();
		return parent != null && GodotObject.IsInstanceValid(parent);
	}

	private static void RestoreRemoteIntentUi(NMultiplayerPlayerIntentHandler intent)
	{
		IntentIsInPlayerChoiceField?.SetValue(intent, false);
		IntentCardInPlayAwaitingPlayerChoiceField?.SetValue(intent, null);

		Node? cardThinkyDots = IntentCardThinkyDotsField?.GetValue(intent) as Node;
		Node? hitbox = IntentHitboxField?.GetValue(intent) as Node;
		Node? cardIntent = IntentCardIntentField?.GetValue(intent) as Node;
		SafeMoveNode(cardThinkyDots, cardIntent);
		SafeMoveNode(hitbox, intent);
		SetUiNodeHidden(cardThinkyDots);
		SetUiNodeHidden(hitbox);
	}

	private static void SafeMoveNode(Node? node, Node? targetParent)
	{
		if (node == null || targetParent == null || !GodotObject.IsInstanceValid(node) || !GodotObject.IsInstanceValid(targetParent))
		{
			return;
		}

		Node? currentParent = node.GetParent();
		if (currentParent == targetParent)
		{
			return;
		}

		if (currentParent == null)
		{
			targetParent.AddChildSafely(node);
		}
		else
		{
			node.Reparent(targetParent);
		}
	}

	private static void SetUiNodeHidden(Node? node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			return;
		}

		if (node is CanvasItem canvasItem)
		{
			canvasItem.Visible = false;
		}

		node.ProcessMode = Node.ProcessModeEnum.Disabled;
	}

	private static void KillQueueItemTween(object? item)
	{
		if (item != null && QueueItemTweenField?.GetValue(item) is Tween tween && GodotObject.IsInstanceValid(tween))
		{
			tween.Kill();
		}
	}

	private static void UnsubscribeCardPlayQueueResume(NCardPlayQueue queue, GameAction action)
	{
		if (BeforeRemoteCardPlayResumedMethod == null || BeforeResumedAfterPlayerChoiceEventField == null)
		{
			return;
		}

		if (BeforeResumedAfterPlayerChoiceEventField.GetValue(action) is not Action<GameAction> current)
		{
			return;
		}

		Delegate? updated = current;
		foreach (Delegate handler in current.GetInvocationList())
		{
			if (ReferenceEquals(handler.Target, queue) && handler.Method == BeforeRemoteCardPlayResumedMethod)
			{
				updated = Delegate.Remove(updated, handler);
			}
		}

		BeforeResumedAfterPlayerChoiceEventField.SetValue(action, updated);
	}

	private static void LogRelicAnimationSkipped(string reason)
	{
		if (HextechRunLogBudget.TryConsume("ui.relic-animation-skip", 5))
		{
			HextechLog.Warn("Mayhem", $"Relic acquired animation skipped: {reason}");
		}
	}

	private static void LogRemoteIntentSkipped(GameAction action, string reason)
	{
		if (HextechRunLogBudget.TryConsume("ui.remote-intent-skip", 10))
		{
			HextechLog.Warn("UI", $"Remote intent card resume UI skipped: {reason}; action={action}");
		}
	}

	private static void LogRemotePlayQueueSkipped(GameAction action, string reason)
	{
		if (HextechRunLogBudget.TryConsume("ui.remote-play-queue-skip", 10))
		{
			HextechLog.Warn("UI", $"Remote play queue card resume UI skipped: {reason}; action={action}");
		}
	}

	/// <summary>海克斯遗物的获得动画:持有者已离开场景树时跳过,动画途中持有者离树/被释放时吞掉由此产生的异常。</summary>
	/// <remarks>
	/// 跳过型前缀:原版 <c>NRelicInventoryHolder.PlayNewlyAcquiredAnimation</c> 直接 <c>AwaitProcessFrame</c> 与
	/// <c>GetTree().CreateTween()</c>,持有者不在树上时抛空引用;没有 Hook 能在动画前取消它。
	/// 激活条件:只对海克斯遗物(<see cref="HextechCatalog.IsHextechCustomRelic"/>)的持有者生效,
	/// 原版与第三方遗物的异常原样抛出(设计哲学第 2 节:兜底只限本模组能证明是自己造成的对象)。
	/// 版本:0.107.1 / 0.111.0 原方法逻辑一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NRelicInventoryHolder), nameof(NRelicInventoryHolder.PlayNewlyAcquiredAnimation), typeof(Vector2?), typeof(Vector2?))]
	[HextechPatch("ui.safety.newly-acquired-animation", "遗物获取动画安全")]
	private static class NewlyAcquiredAnimationPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NRelicInventoryHolder __instance, ref Task __result, out bool __state)
		{
			__state = HoldsHextechRelic(__instance);
			if (!__state)
			{
				return true;
			}

			if (!IsNodeUsable(__instance))
			{
				LogRelicAnimationSkipped("holder-not-in-tree");
				__state = false;
				__result = Task.CompletedTask;
				return false;
			}

			return true;
		}

		[HarmonyPostfix]
		private static void Postfix(NRelicInventoryHolder __instance, bool __state, ref Task __result)
		{
			if (__state)
			{
				__result = PlayNewlyAcquiredAnimationSafely(__result, __instance);
			}
		}
	}

	/// <summary>远端玩家意图 UI:等待选择的卡牌节点已脱离场景时,只复原意图 UI、不再把失效节点塞回出牌队列。</summary>
	/// <remarks>
	/// 跳过型前缀:原版私有 <c>NMultiplayerPlayerIntentHandler.BeforeActionReadyToResumeAfterPlayerChoice</c> 无条件
	/// <c>Reparent</c> 并调用 <c>NCardPlayQueue.ReAddCardAfterPlayerChoice</c>,卡牌节点已脱树时抛异常,打断联机恢复流程;
	/// 原版没有 Hook 能拦下这一步。
	/// 激活条件:只在"等待中的卡牌节点存在但已无有效父节点"时跳过,其它情况交给原版。
	/// 版本:0.107.1 / 0.111.0 原方法一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NMultiplayerPlayerIntentHandler), "BeforeActionReadyToResumeAfterPlayerChoice")]
	[HextechPatch("ui.safety.multiplayer-intent", "联机意图恢复安全", Optional = true)]
	private static class MultiplayerIntentPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NMultiplayerPlayerIntentHandler __instance, GameAction action)
		{
			NCard? card = IntentCardInPlayAwaitingPlayerChoiceField?.GetValue(__instance) as NCard;
			if (card == null || HasUsableParent(card))
			{
				return true;
			}

			RestoreRemoteIntentUi(__instance);
			LogRemoteIntentSkipped(action, "awaiting-card-detached");
			return false;
		}
	}

	/// <summary>远端出牌恢复:队列项的卡牌节点缺失或已脱树时,移除该项并退订恢复事件,不再对失效节点做动画。</summary>
	/// <remarks>
	/// 跳过型前缀:原版私有 <c>NCardPlayQueue.BeforeRemoteCardPlayResumedAfterPlayerChoice</c> 直接对队列项的卡牌
	/// <c>Reparent</c> 并播放进出牌堆动画,卡牌节点失效时抛异常;原版没有 Hook 能拦下这一步。
	/// 替换体复刻原版的"退订事件 + 移出队列",只省略对失效卡牌节点的重挂与动画。
	/// 激活条件:只在找到对应队列项且其卡牌节点缺失/脱树时跳过,其它情况交给原版。
	/// 版本:0.107.1 / 0.111.0 原方法一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NCardPlayQueue), "BeforeRemoteCardPlayResumedAfterPlayerChoice")]
	[HextechPatch("ui.safety.card-play-queue", "联机出牌队列恢复安全", Optional = true)]
	private static class CardPlayQueuePatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NCardPlayQueue __instance, GameAction action)
		{
			if (!TryFindQueueItem(__instance, action, out IList? playQueue, out int index, out object? item, out NCard? card))
			{
				return true;
			}

			if (card != null && HasUsableParent(card))
			{
				return true;
			}

			// TryFindQueueItem 返回 true 时 playQueue/index 有效;退订与 Tween.Kill 都不改动队列。
			UnsubscribeCardPlayQueueResume(__instance, action);
			KillQueueItemTween(item);
			playQueue!.RemoveAt(index);
			TweenAllToQueuePositionMethod?.Invoke(__instance, null);

			LogRemotePlayQueueSkipped(action, card == null ? "missing-card-node" : "card-node-detached");
			return false;
		}
	}
}
