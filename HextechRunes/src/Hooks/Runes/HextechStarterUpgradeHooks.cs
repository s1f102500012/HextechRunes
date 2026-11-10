using MegaCrit.Sts2.Core.Models.Events;

namespace HextechRunes;

/// <summary>
/// 升级:打击/防御——将基础打击/防御的升级上限放宽到 999,让原版的多级机制自己工作:
/// MaxUpgradeLevel&gt;1 时,标题原生显示"打击+n",OnUpgrade 每级 +3。
/// 反序列化时 Owner 尚未指定,需要临时放行超过上限的历史等级;普通无主卡保持原版上限,
/// 避免第三方的 <c>while (IsUpgradable)</c> 升满逻辑越过 +1 或永不终止。
/// </summary>
internal static class HextechStarterUpgradeHooks
{
	internal const int UpgradeLevelCap = 999;

	private static readonly HextechScopedDepthGuard CardDeserializationGuard = new();

	internal static int ResolveOwnedMaxUpgradeLevel(int currentUpgradeLevel)
	{
		return Math.Max(currentUpgradeLevel, UpgradeLevelCap);
	}

	internal static int ResolveUnownedMaxUpgradeLevel(int currentUpgradeLevel, bool isDeserializing)
	{
		if (!isDeserializing)
		{
			return 1;
		}

		int nextUpgradeLevel = currentUpgradeLevel == int.MaxValue
			? int.MaxValue
			: currentUpgradeLevel + 1;
		return Math.Max(nextUpgradeLevel, UpgradeLevelCap);
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.MaxUpgradeLevel), MethodType.Getter)]
	[HextechPatch("card.starter-upgrade.max-level", "起始牌多重升级")]
	private static class MaxUpgradeLevelPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, ref int __result)
		{
			if (__result != 1)
			{
				return;
			}

			// 卡牌图鉴/预览等场景传入的是 canonical 实例:其 Owner getter 直接抛
			// CanonicalModelException,会把整个图鉴 InitGrid 打断(卡牌全堆左上角;
			// 第三方 mod 的基础打击变体同样命中判定)。canonical 卡不属于任何玩家,保持原上限。
			if (__instance.IsCanonical)
			{
				return;
			}

			bool isStrike = StrikeUpgradeRune.IsBasicStrike(__instance);
			bool isDefend = !isStrike && DefendUpgradeRune.IsBasicDefend(__instance);
			if (!isStrike && !isDefend)
			{
				return;
			}

			Player? owner = __instance.Owner;
			if (owner == null)
			{
				__result = ResolveUnownedMaxUpgradeLevel(
					__instance.CurrentUpgradeLevel,
					CardDeserializationGuard.IsActive);
				return;
			}

			if ((isStrike && owner.GetRelic<StrikeUpgradeRune>() != null)
				|| (isDefend && owner.GetRelic<DefendUpgradeRune>() != null))
			{
				// 持有者同样兼容历史坏档:等级已超上限时以当前等级为准(不再增长,但可正常游戏)。
				__result = ResolveOwnedMaxUpgradeLevel(__instance.CurrentUpgradeLevel);
			}
		}
	}

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

	internal static bool HasUnlockedUpgradeLevels(CardModel card)
	{
		if (card.IsCanonical || card.Owner is not Player owner)
		{
			return false;
		}

		return (StrikeUpgradeRune.IsBasicStrike(card) && owner.GetRelic<StrikeUpgradeRune>() != null)
			|| (DefendUpgradeRune.IsBasicDefend(card) && owner.GetRelic<DefendUpgradeRune>() != null);
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

	// 胜利后会升级本场打出过的牌库打击/防御：还没打出过的那几张在手牌里显示金色光泽（原版高亮的金色），提示玩家。
	// 只有牌库里的牌（有 DeckVersion）才会被升级，战斗中生成的临时牌不发光。纯本机表现。
	[HarmonyPatch(typeof(CardModel), nameof(CardModel.ShouldGlowGold), MethodType.Getter)]
	[HextechPatch("card.starter-upgrade.unplayed-glow", "起始牌多重升级")]
	private static class UnplayedGoldGlowPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, ref bool __result)
		{
			if (__result
				|| __instance.DeckVersion is not CardModel deckCard
				|| !CombatManager.Instance.IsInProgress
				|| !HasUnlockedUpgradeLevels(__instance))
			{
				return;
			}

			__result = !HextechStarterUpgradeHelper.WasDeckCardPlayedThisCombat(deckCard, __instance.Owner!.NetId);
		}
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable), typeof(SerializableCard))]
	[HextechPatch("card.starter-upgrade.load", "起始牌多重升级")]
	private static class FromSerializablePatch
	{
		[HarmonyPrefix]
		private static void Prefix()
		{
			CardDeserializationGuard.Enter();
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception)
		{
			CardDeserializationGuard.Exit();
			return __exception;
		}
	}
}
