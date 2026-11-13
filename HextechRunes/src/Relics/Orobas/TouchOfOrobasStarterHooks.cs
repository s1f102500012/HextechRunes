using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes;

// 本模组的海克斯、锻造器等遗物底层都是 Starter 稀有度，原版欧洛巴斯之触却把玩家第一个 Starter 遗物当作角色初始遗物；
// 角色初始遗物已不在或排在海克斯之后时，会把海克斯替换成头环。挑选时跳过本模组管理的遗物。
// 欧洛巴斯"+"版不属于本模组管理的遗物，仍按角色初始遗物参与再次强化。
// 判断只读各端一致的遗物列表与注册表；原版第一个 Starter 遗物不是本模组遗物时结果与原版完全相同。
internal static class TouchOfOrobasStarterHooks
{
	internal static bool IsHextechCustomStarterRelic(RelicModel relic)
	{
		return relic.Rarity == RelicRarity.Starter && HextechCatalog.IsHextechCustomRelic(relic);
	}

	internal static RelicModel? FindCharacterStarterRelic(Player player)
	{
		return player.Relics.FirstOrDefault(static relic =>
			relic.Rarity == RelicRarity.Starter && !HextechCatalog.IsHextechCustomRelic(relic));
	}

	// 原版事件与本模组捆绑发放都经 SetupForPlayer 调用这里；返回 null 时 SetupForPlayer 返回 false，
	// 事件不提供该选项，捆绑发放跳过这件遗物。
	[HarmonyPatch(typeof(TouchOfOrobas), "GetStarterRelic", [typeof(Player)])]
	[HextechPatch("relic.orobas-starter-filter", "欧洛巴斯之触跳过海克斯")]
	private static class StarterRelicPatch
	{
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Last)]
		private static void Postfix(Player p, ref RelicModel? __result)
		{
			if (__result != null && IsHextechCustomStarterRelic(__result))
			{
				__result = FindCharacterStarterRelic(p);
			}
		}
	}

	// 未经 SetupForPlayer 获得（控制台、其他模组直接发放）时 StarterRelic 为空，原版改用 Owner.Relics.First(Starter)。
	// 第一个 Starter 是本模组遗物时先写入真正的初始遗物再放行原版；没有真正的初始遗物时跳过原版替换
	// （原版在完全没有 Starter 遗物时会抛异常），欧洛巴斯之触保留为无效果遗物，海克斯不受影响。
	[HarmonyPatch(typeof(TouchOfOrobas), nameof(TouchOfOrobas.AfterObtained), new Type[0])]
	[HextechPatch("relic.orobas-starter-filter.obtain-fallback", "欧洛巴斯之触跳过海克斯")]
	private static class ObtainFallbackPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(TouchOfOrobas __instance, ref Task __result)
		{
			if (__instance.StarterRelic != null)
			{
				return true;
			}

			Player owner = __instance.Owner;
			RelicModel? nativeChoice = owner.Relics.FirstOrDefault(static relic => relic.Rarity == RelicRarity.Starter);
			if (nativeChoice == null || !IsHextechCustomStarterRelic(nativeChoice))
			{
				return true;
			}

			RelicModel? starter = FindCharacterStarterRelic(owner);
			if (starter != null)
			{
				__instance.StarterRelic = starter.Id;
				return true;
			}

			HextechLog.Info("Orobas", $"TouchOfOrobas: player {owner.NetId} has no character starter relic besides hextech relics; replacement skipped.");
			__result = Task.CompletedTask;
			return false;
		}
	}
}
