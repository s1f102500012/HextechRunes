using MegaCrit.Sts2.Core.Localization;

namespace HextechRunes;

// 没有任何效果，只由弑神试炼的第一幕发放（见 HextechGodslayerTrial）；持有时建筑师的结局对话会被替换。
// 本机完成弑神后，图鉴里显示它，描述换成 rememberedDescription。
public sealed class GodslayerRune : HextechRelicBase
{
	private const string RememberedDescriptionKey = "GODSLAYER_RUNE.rememberedDescription";

	[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.DynamicDescription), MethodType.Getter)]
	[HextechPatch("rune.godslayer.remembered-description", "弑神", Rune = typeof(GodslayerRune))]
	private static class RememberedDescriptionPatch
	{
		[HarmonyPostfix]
		private static void Postfix(RelicModel __instance, ref LocString __result)
		{
			if (__instance is GodslayerRune
				&& HextechSignalNoise.HasSlain()
				&& LocString.Exists("relics", RememberedDescriptionKey))
			{
				__result = new LocString("relics", RememberedDescriptionKey);
			}
		}
	}
}
