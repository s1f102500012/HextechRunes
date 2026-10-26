namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 不跳过原版：只在 Draw 同步执行（含其中的 ShouldDraw）期间记下请求张数，见 HextechDrawRequest。
	[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool))]
	[HextechPatch("combat.draw-request", "抽牌转换")]
	private static class DrawRequestScopePatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.First)]
		private static void Prefix(decimal count, Player player)
		{
			HextechDrawRequest.Begin(player, count);
		}

		[HarmonyFinalizer]
		private static void Finalizer()
		{
			HextechDrawRequest.End();
		}
	}
}
