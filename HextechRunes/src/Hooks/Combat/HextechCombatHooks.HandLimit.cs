namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 潦草急就(敌方)：战斗中手牌上限减少 1/2/3。原版上限只有这一个静态 getter，补丁打上后 Harmony 会禁止它再被内联。
	[HarmonyPatch(typeof(CardPile), nameof(CardPile.MaxCardsInHand), MethodType.Getter)]
	[HextechPatch("combat.max-cards-in-hand", "潦草急就")]
	private static class MaxCardsInHandPatch
	{
		[HarmonyPostfix]
		private static void Postfix(ref int __result)
		{
			if (!CombatManager.Instance.IsInProgress
				|| HextechMayhemModifier.FindIn(RunManager.Instance.DebugOnlyGetState()) is not HextechMayhemModifier modifier)
			{
				return;
			}

			int reduction = HastyScribbleEnemyHex.GetHandLimitReduction(new HextechEnemyHexContext(modifier));
			if (reduction > 0)
			{
				__result = Math.Max(1, __result - reduction);
			}
		}
	}
}
