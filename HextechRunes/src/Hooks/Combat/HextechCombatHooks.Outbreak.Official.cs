namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 0.110.0 将疫情从持续监听中毒施加的 OutbreakPower 重做为技能牌:
	// 整个 OnPlay 内先施加中毒再主动触发。守卫覆盖这段完整响应链,维持即死与补偿的安全边界。
	[HarmonyPatch(typeof(Outbreak), "OnPlay", typeof(PlayerChoiceContext), typeof(CardPlay))]
	[HextechPatch("combat.outbreak", "即死与补偿安全边界")]
	private static class OutbreakPatch
	{
		// __state 标记本前缀确实入栈：排在前面的补丁抛异常时本前缀不会执行，Finalizer 不能出栈。
		[HarmonyPrefix]
		private static void Prefix(out bool __state)
		{
			__state = true;
			OutbreakPowerPoisonResponseGuard.Enter();
		}

		[HarmonyPostfix]
		private static void Postfix(ref bool __state, ref Task __result) => WrapOutbreakResponse(ref __state, ref __result);

		[HarmonyFinalizer]
		private static Exception? Finalizer(bool __state, Exception? __exception) => ExitGuardAfterSynchronousFailure(OutbreakPowerPoisonResponseGuard, __state, __exception);
	}
}
