#if STS2_107_1
using MegaCrit.Sts2.Core.Models.Monsters;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

// 0.107.1 的昆虫法师在没有私人蜂巢时 SpitMove 会空引用;0.111.0 原版已修复,其他变体不编译本类。
internal static class HextechEncounterCompatibilityHooks
{
	private const string EntomancerCastSfx = "event:/sfx/enemy/enemy_attacks/entomancer/entomancer_cast";

	internal static MethodInfo? TryResolveEntomancerSpitMove(Type entomancerType, bool warnIfMissing)
	{
		return TryGetMethod(
			entomancerType,
			"SpitMove",
			BindingFlags.Instance | BindingFlags.NonPublic,
			warnIfMissing,
			typeof(IReadOnlyList<Creature>));
	}

	private static async Task EntomancerSpitMoveWithoutPersonalHive(Entomancer entomancer)
	{
		await PowerCmd.Apply<StrengthPower>(entomancer.Creature, 2m, entomancer.Creature, null);
		try
		{
			SfxCmd.Play(EntomancerCastSfx);
			await CreatureCmd.TriggerAnim(entomancer.Creature, "Cast", 0.5f);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Entomancer", $"Cast visual failed: {ex.Message}");
		}
	}

	// 跳过型前缀：缺私人蜂巢时用 0.111.0 原版的行为（+2 力量与施法演出）替换 SpitMove；
	// 原版没有替换怪物行动的 Hook，仅 0.107.1 编译，目标经 HookReflection 解析、缺失即可选降级。
	[HarmonyPatch]
	[HextechPatch("compat.entomancer-spit", "昆虫法师遭遇战兼容", Optional = true)]
	private static class EntomancerSpitMovePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => TryResolveEntomancerSpitMove(typeof(Entomancer), warnIfMissing: true) != null;

		[HarmonyTargetMethod]
		private static MethodBase TargetMethod() => TryResolveEntomancerSpitMove(typeof(Entomancer), warnIfMissing: false)!;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Entomancer __instance, ref Task __result)
		{
			if (__instance.Creature.HasPower<PersonalHivePower>())
			{
				return true;
			}

			__result = EntomancerSpitMoveWithoutPersonalHive(__instance);
			return false;
		}
	}
}
#endif
