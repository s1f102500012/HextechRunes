using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes;

internal static class OrobasPlusUpgrades
{
	internal static RelicModel ResolveUpgrade(RelicModel starter, RelicModel originalResult, bool active)
	{
		// 已持有的二次升级即使本局关闭海克斯也不能退化成头环；映射回同一模型使原版替换保持原效果。
		if (starter is OrobasPlusRelicBase)
		{
			return (RelicModel)(starter.CanonicalInstance ?? starter);
		}

		// 只补原版缺失的映射，不覆盖其它模组已经提供的升级结果。
		if (!active || originalResult is not Circlet)
		{
			return originalResult;
		}

		return starter switch
		{
			BlackBlood => ModelDb.Relic<HextechBlackBloodPlus>(),
			RingOfTheDrake => ModelDb.Relic<HextechRingOfTheDrakePlus>(),
			DivineDestiny => ModelDb.Relic<HextechDivineDestinyPlus>(),
			PhylacteryUnbound => ModelDb.Relic<HextechPhylacteryUnboundPlus>(),
			InfusedCore => ModelDb.Relic<HextechInfusedCorePlus>(),
			_ => originalResult
		};
	}

	[HarmonyPatch(typeof(TouchOfOrobas), nameof(TouchOfOrobas.GetUpgradedStarterRelic))]
	[HextechPatch("relic.orobas-plus-upgrade", "欧洛巴斯再次强化")]
	private static class UpgradePatch
	{
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Last)]
		private static void Postfix(RelicModel starterRelic, ref RelicModel __result)
		{
			// 图鉴传入无 Owner 的 canonical 模型。单机旧局/控制台可能没有 Mayhem modifier，
			// 沿用统一开关判断；联机缺少同步快照时该入口不会采用各端本地配置。
			bool active = starterRelic.IsMutable && starterRelic.Owner is Player owner
				&& HextechMayhemModifier.IsEnabledForRun(owner.RunState)
				&& !HextechGodslayerTrial.IsTrialRun(owner.RunState);
			__result = ResolveUpgrade(starterRelic, __result, active);
		}
	}
}
