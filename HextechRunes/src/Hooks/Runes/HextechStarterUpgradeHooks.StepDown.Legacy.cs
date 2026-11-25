#if !STS2_114_OR_NEWER
using MegaCrit.Sts2.Core.Models.Events;

namespace HextechRunes;

// 0.113.x 及更早：原版 CardCmd.Downgrade 一律清零，这里把指定来源的降级改成只降一级。
// 0.114.0 起原版自己每次只降一级（清零另有 CardCmd.RemoveAllUpgrades），见 HextechStarterUpgradeHooks.StepDown.V114.cs。
internal static partial class HextechStarterUpgradeHooks
{
	// 原版 CardCmd.Downgrade 一律清零。镜中倒影事件与敌方海克斯“神化”的降级在本作用域内进行，
	// 对放开上限的多级打击/防御只降一级；魔法骑士（DampenPower）等其他降级保持清零。
	// 只在主线程同步调用期间计数（事件状态机单步、神化的同步调用），不跨 await。
	[ThreadStatic]
	private static int _stepDownDepth;

	/// <summary>降级一张牌：放开上限的多级打击/防御只降一级，其余照原版清零。</summary>
	internal static void DowngradeOneLevel(CardModel card)
	{
		_stepDownDepth++;
		try
		{
			CardCmd.Downgrade(card);
		}
		finally
		{
			_stepDownDepth--;
		}
	}

	internal static int ResolveSteppedDowngradeLevel(int currentUpgradeLevel, bool inStepDownScope, bool hasUnlockedLevels)
	{
		return inStepDownScope && hasUnlockedLevels && currentUpgradeLevel > 1 ? currentUpgradeLevel - 1 : 0;
	}

	// 不跳过原版：原版照常清零（含降级记录与 DowngradeInternal 的重建），之后按原版读档同口径逐级升回“原等级 - 1”。
	// 战斗结束中原版不降级，等级未变，后缀不会再升。
	[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Downgrade), typeof(CardModel))]
	[HextechPatch("card.starter-upgrade.step-down", "起始牌多重升级")]
	private static class SteppedDowngradePatch
	{
		[HarmonyPrefix]
		private static void Prefix(CardModel card, out int __state)
		{
			__state = ResolveSteppedDowngradeLevel(card.CurrentUpgradeLevel, _stepDownDepth > 0, HasUnlockedUpgradeLevels(card));
		}

		[HarmonyPostfix]
		private static void Postfix(CardModel card, int __state)
		{
			while (__state > 0 && card.CurrentUpgradeLevel < __state)
			{
				card.UpgradeInternal();
				card.FinalizeUpgradeInternal();
			}
		}
	}

	// 镜中倒影（Reflections）“触碰镜子”选项随机降级已升级的牌：事件状态机每一步同步执行期间进入降一级作用域。
	[HarmonyPatch]
	[HextechPatch("card.starter-upgrade.reflections-step-down", "起始牌多重升级", Optional = true)]
	private static class ReflectionsStepDownScopePatch
	{
		private static MethodBase? ResolveTarget() =>
			AccessTools.Method(typeof(Reflections), "TouchAMirror") is MethodInfo method ? AccessTools.AsyncMoveNext(method) : null;

		[HarmonyPrepare]
		private static bool Prepare() => ResolveTarget() != null;

		[HarmonyTargetMethod]
		private static MethodBase TargetMethod() => ResolveTarget()!;

		[HarmonyPrefix]
		private static void Prefix()
		{
			_stepDownDepth++;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception)
		{
			_stepDownDepth--;
			return __exception;
		}
	}
}
#endif
