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

	// 原版恶咒移除时（幽灵骑士死亡）会清掉持有者所有牌上的邪咒；玩家侧恶咒还在时立即补回外观，虚无本身不受影响。
	[HarmonyPatch(typeof(HexPower), nameof(HexPower.AfterRemoved), typeof(Creature))]
	[HextechPatch("combat.hex-power-removed-restore", "升级：幽灵骑士")]
	private static class HexPowerRemovedRestorePatch
	{
		[HarmonyPostfix]
		private static void Postfix(Creature oldOwner, ref Task __result)
		{
			if (oldOwner.GetPower<HextechHexPower>() is { } power)
			{
				__result = power.RestoreHexedAfter(__result);
			}
		}
	}
}
