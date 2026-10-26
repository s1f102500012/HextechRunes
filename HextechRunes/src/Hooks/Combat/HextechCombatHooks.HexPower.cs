using MegaCrit.Sts2.Core.Models.Afflictions;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 原版邪咒附着在牌进入战斗时，若持有者没有原版 HexPower 就把自己清掉；玩家侧恶咒（HextechHexPower）也要留住它。
	[HarmonyPatch(typeof(Hexed), nameof(Hexed.AfterCardEnteredCombat), typeof(CardModel))]
	[HextechPatch("combat.hexed-keep-for-hextech-hex", "升级：幽灵骑士")]
	private static class HexedKeepForHextechHexPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Hexed __instance, CardModel card, ref Task __result)
		{
			if (card != __instance.Card || !HextechHexPower.KeepsHexedAffliction(card))
			{
				return true;
			}

			__result = Task.CompletedTask;
			return false;
		}
	}
}
