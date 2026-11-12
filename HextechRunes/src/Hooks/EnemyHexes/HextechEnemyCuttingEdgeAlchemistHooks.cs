using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Random;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechEnemyCuttingEdgeAlchemistHooks
{
	private const float PotionRewardMultiplier = 0.5f;
	private const float FloatTolerance = 0.000001f;
	// AbstractOdds._rng（0.107.1/0.111.0 原版私有字段）：药水掉落共用的奖励 RNG。
	// 缺失时本敌方海克斯不再减半药水掉落（保持原版结果），缺失成员进启动摘要。
	private static readonly FieldInfo? OddsRngField = TryGetField(typeof(AbstractOdds), "_rng");

	internal readonly record struct PotionRollState(bool Active, float OriginalValue);

	// 0.107.1 的 PotionRewardOdds.Roll 多一个 AscensionManager 参数（补丁目标签名随版本变化，允许行内 #if）。
#if STS2_107_1
	[HarmonyPatch(typeof(PotionRewardOdds), nameof(PotionRewardOdds.Roll), typeof(Player), typeof(MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager), typeof(RoomType))]
#else
	[HarmonyPatch(typeof(PotionRewardOdds), nameof(PotionRewardOdds.Roll), typeof(Player), typeof(RoomType))]
#endif
	[HextechPatch("enemy-hex.cutting-edge-alchemist", "敌方海克斯:尖端炼金术士")]
	private static class RollPatch
	{
		[HarmonyPrefix]
		private static void Prefix(PotionRewardOdds __instance, Player player, out PotionRollState __state)
		{
			__state = OddsRngField != null && CuttingEdgeAlchemistEnemyHex.IsActiveFor(player)
				? new PotionRollState(true, __instance.CurrentValue)
				: default;
		}

		[HarmonyPostfix]
		private static void Postfix(PotionRewardOdds __instance, ref bool __result, PotionRollState __state)
		{
			if (!__state.Active || !__result)
			{
				return;
			}

			// 概率值没变说明这次是保底强制掉落，保底不减半；否则用同一奖励 RNG 再掷一次决定是否保留。
			bool wasForced = MathF.Abs(__instance.CurrentValue - __state.OriginalValue) <= FloatTolerance;
			if (wasForced || OddsRngField?.GetValue(__instance) is not Rng rng)
			{
				return;
			}

			__result = rng.NextFloat() < PotionRewardMultiplier;
		}
	}
}
