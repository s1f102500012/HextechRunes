namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static readonly HextechScopedDepthGuard GoliathMaxHpGuard = new();

	/// <summary>
	/// Gain/LoseMaxHp 共用：先改主符文记录的基础最大生命，再把本次增减量换算成按系数缩放后的实际变化。
	/// 返回 false 表示换算后实际值不变，调用方跳过原方法；<paramref name="entered"/> 表示已进入守卫。
	/// </summary>
	private static bool ScaleMaxHpDelta(Creature creature, ref decimal amount, int sign, out bool entered)
	{
		entered = false;
		if (GoliathMaxHpGuard.IsActive
			|| creature.Player is not Player player
			|| HextechMaxHpScaling.GetPrimary(player) is not IHextechMaxHpBaseHolder primary)
		{
			return true;
		}

		HextechMaxHpScaling.EnsureBaseInitialized(player, primary, assumeAlreadyScaled: true);
		int oldActual = creature.MaxHp;
		primary.BaseMaxHp += sign * (int)amount;
		int newActual = HextechMaxHpScaling.GetScaledMaxHp(player, primary);
		int delta = Math.Max(0, sign * (newActual - oldActual));
		if (delta == 0)
		{
			return false;
		}

		GoliathMaxHpGuard.Enter();
		entered = true;
		amount = delta;
		return true;
	}

	private static void CompleteMaxHpTaskPostfix(Creature creature, ref bool entered, ref Task result)
	{
		bool wasEntered = entered;
		if (entered)
		{
			result = GoliathMaxHpGuard.WrapEnteredTask(result);
			entered = false;
		}

		if (wasEntered || creature.Player?.GetRelic<NearDeathFeastRune>() != null)
		{
			result = RefreshDeathLimitAfter(result, creature);
		}
	}

	private static async Task RefreshDeathLimitAfter(Task task, Creature creature)
	{
		try
		{
			await task;
		}
		finally
		{
			creature.Player?.GetRelic<NearDeathFeastRune>()?.RefreshDeathLimitDisplay();
		}
	}

	private static async Task<T> RefreshDeathLimitAfter<T>(Task<T> task, Creature creature)
	{
		try
		{
			return await task;
		}
		finally
		{
			creature.Player?.GetRelic<NearDeathFeastRune>()?.RefreshDeathLimitDisplay();
		}
	}

	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp), typeof(Creature), typeof(decimal))]
	[HextechPatch("combat.gain-max-hp", "最大生命换算")]
	private static class GainMaxHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature creature, ref decimal amount, ref Task __result, out bool __state)
		{
			if (ScaleMaxHpDelta(creature, ref amount, sign: 1, out __state))
			{
				return true;
			}

			__result = Task.CompletedTask;
			return false;
		}

		[HarmonyPostfix]
		private static void Postfix(Creature creature, ref bool __state, ref Task __result) => CompleteMaxHpTaskPostfix(creature, ref __state, ref __result);

		[HarmonyFinalizer]
		private static Exception? Finalizer(bool __state, Exception? __exception) => ExitGuardAfterSynchronousFailure(GoliathMaxHpGuard, __state, __exception);
	}

#if STS2_113_OR_NEWER
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp), typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(CardModel))]
#else
	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp), typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(bool))]
#endif
	[HextechPatch("combat.lose-max-hp", "最大生命换算")]
	private static class LoseMaxHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Creature creature, ref decimal amount, ref Task __result, out bool __state)
		{
			if (ScaleMaxHpDelta(creature, ref amount, sign: -1, out __state))
			{
				return true;
			}

			__result = Task.CompletedTask;
			return false;
		}

		[HarmonyPostfix]
		private static void Postfix(Creature creature, ref bool __state, ref Task __result) => CompleteMaxHpTaskPostfix(creature, ref __state, ref __result);

		[HarmonyFinalizer]
		private static Exception? Finalizer(bool __state, Exception? __exception) => ExitGuardAfterSynchronousFailure(GoliathMaxHpGuard, __state, __exception);
	}

	[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxHp), typeof(Creature), typeof(decimal))]
	[HextechPatch("combat.set-max-hp", "最大生命换算")]
	private static class SetMaxHpPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		// 不跳过原版：只把目标值改成换算后的最大生命。
		private static void Prefix(Creature creature, ref decimal amount, out bool __state)
		{
			__state = false;
			if (GoliathMaxHpGuard.IsActive
				|| creature.Player is not Player player
				|| HextechMaxHpScaling.GetPrimary(player) is not IHextechMaxHpBaseHolder primary)
			{
				return;
			}

			primary.BaseMaxHp = (int)Math.Max(1m, amount);
			GoliathMaxHpGuard.Enter();
			__state = true;
			amount = HextechMaxHpScaling.GetScaledMaxHp(player, primary);
		}

		[HarmonyPostfix]
		private static void Postfix(Creature creature, ref bool __state, ref Task<decimal> __result)
		{
			bool wasEntered = __state;
			if (__state)
			{
				__result = GoliathMaxHpGuard.WrapEnteredTask(__result);
				__state = false;
			}

			if (wasEntered || creature.Player?.GetRelic<NearDeathFeastRune>() != null)
			{
				__result = RefreshDeathLimitAfter(__result, creature);
			}
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(bool __state, Exception? __exception) => ExitGuardAfterSynchronousFailure(GoliathMaxHpGuard, __state, __exception);
	}
}
