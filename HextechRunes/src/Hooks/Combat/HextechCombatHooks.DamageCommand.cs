namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static readonly AsyncLocal<long[]?> ActualDamageCommandIds = new();
	private static long _nextActualDamageCommandId;

	internal static long CurrentActualDamageCommandId
	{
		get
		{
			long[]? ids = ActualDamageCommandIds.Value;
			return ids is { Length: > 0 } ? ids[^1] : 0L;
		}
	}

	// 只负责命令结束后的账目清理。AsyncLocal 的出栈不能放在这里:async 方法内对 AsyncLocal 的赋值只作用于
	// 它自己的执行上下文副本,不会回写到调用方;调用方的上下文由 Postfix 在同步返回前恢复(见 DamageCommandPatch)。
	private static async Task<T> CompleteWithActualDamageCommandReset<T>(Task<T> task, long commandId)
	{
		try
		{
			return await task;
		}
		finally
		{
			PiercingThreadRune.ClearPendingDamage(commandId);
			try
			{
				await ConsumeOstyRedirectedSlippery(commandId);
			}
			finally
			{
				// 补扣滑溜走 PowerCmd.Decrement，可能抛异常；本命令的滑溜账目无论如何都要清掉。
				ClearSlipperyReductions(commandId);
			}
		}
	}

	private static void PopActualDamageCommand(long commandId)
	{
		long[]? current = ActualDamageCommandIds.Value;
		if (current is not { Length: > 0 })
		{
			return;
		}

		long[] next;
		if (current[^1] == commandId)
		{
			next = current[..^1];
		}
		else
		{
			next = current.Where(id => id != commandId).ToArray();
		}

		ActualDamageCommandIds.Value = next.Length == 0 ? null : next;
	}

#if STS2_108_OR_NEWER
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay))]
#else
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel))]
#endif
	[HextechPatch("combat.damage-command", "伤害命令作用域")]
	private static class DamageCommandPatch
	{
		[HarmonyPrefix]
		private static void Prefix(out long __state)
		{
			__state = Interlocked.Increment(ref _nextActualDamageCommandId);
			long[] current = ActualDamageCommandIds.Value ?? [];
			long[] next = new long[current.Length + 1];
			Array.Copy(current, next, current.Length);
			next[^1] = __state;
			ActualDamageCommandIds.Value = next;
		}

		// Prefix 在调用方的执行上下文里入栈;原方法(async)的同步段在该上下文上运行,各 await 也捕获了带命令 ID 的上下文,
		// 所以它内部的 Hook 全程都能读到 ID。Postfix 同样运行在调用方上下文里、在返回 Task 之前同步执行,
		// 在这里出栈才能真正恢复调用方:否则调用方在这次伤害之后的预览/独立结算会一直看到残留的非零 ID。
		[HarmonyPostfix]
		private static void Postfix(long __state, ref Task<IEnumerable<DamageResult>> __result)
		{
			__result = CompleteWithActualDamageCommandReset(__result, __state);
			PopActualDamageCommand(__state);
		}

		// 原方法同步段抛异常时 Postfix 不会执行;这里兜底出栈,避免调用方带着残留 ID 继续。
		// __state 为 0 表示排在前面的补丁抛异常、本前缀没有入栈；出栈按 ID 删除，Postfix 之后再执行也不会误删别的命令。
		[HarmonyFinalizer]
		private static Exception? Finalizer(long __state, Exception? __exception)
		{
			if (__exception != null && __state != 0L)
			{
				PopActualDamageCommand(__state);
			}

			return __exception;
		}
	}

	// 原版在伤害命令内部扣血、改能力层数时会发"战斗状态变化"通知，它启动的延迟任务捕获当前执行上下文，
	// 下一帧带着本命令的 ID 调用 CombatStateChanged 的订阅者。随机数预测在这个订阅里用真实生物模拟回合结束，
	// 滑溜、穿针引线等只在真实伤害命令里记账的查询钩子会把模拟当成本次结算，只在本机多记一笔（#22 同类问题）。
	// 启动延迟任务前清空作用域、同步返回前恢复：通知本身是同步方法，Prefix 与 Postfix 在同一个上下文里执行。
	[HarmonyPatch(typeof(CombatStateTracker), "NotifyCombatStateChanged")]
	[HextechPatch("combat.damage-command-state-notify", "伤害命令作用域不带进战斗状态通知")]
	private static class CombatStateNotifyScopePatch
	{
		[HarmonyPrefix]
		private static void Prefix(out long[]? __state)
		{
			__state = ActualDamageCommandIds.Value;
			if (__state != null)
			{
				ActualDamageCommandIds.Value = null;
			}
		}

		[HarmonyPostfix]
		private static void Postfix(ref long[]? __state)
		{
			if (__state != null)
			{
				ActualDamageCommandIds.Value = __state;
				__state = null;
			}
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(long[]? __state, Exception? __exception)
		{
			if (__exception != null && __state != null)
			{
				ActualDamageCommandIds.Value = __state;
			}

			return __exception;
		}
	}
}
