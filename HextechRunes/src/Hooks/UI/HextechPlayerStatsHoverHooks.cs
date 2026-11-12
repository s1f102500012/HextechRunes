using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace HextechRunes;

/// <summary>
/// 顶栏头像悬浮提示末尾追加本机玩家的四项系数。头像归属:原版 <c>NTopBar.Initialize</c> 用
/// <c>LocalContext.GetMe(runState)</c> 初始化头像,<c>NTopBarPortraitTip.Initialize</c> 也按本机玩家生成提示,
/// 所以这里同样取本机玩家(纯 UI,允许 LocalContext),在 Initialize 时按补丁实参记下,聚焦时刷新数值。
/// </summary>
internal static class HextechPlayerStatsHoverHooks
{
	// 原版 NTopBarPortraitTip._hoverTip(IHoverTip,0.107.1 / 0.111.0)。
	private static readonly FieldInfo? PortraitHoverTipField = HextechHookReflection.TryGetField(typeof(NTopBarPortraitTip), "_hoverTip");

	private static readonly ConditionalWeakTable<NTopBarPortraitTip, Player> PortraitOwners = new();

	// 两个补丁依赖的私有成员;缺任一项两个补丁都不安装(缺失项已进启动摘要),原版提示不受影响。
	private static bool MembersAvailable => PortraitHoverTipField != null && HextechHoverTipAccess.CanSetDescription;

	private readonly record struct CoefficientLabels(string Health, string Damage, string Block, string Healing)
	{
		// 每次刷新读一次当前语言文本:不跨调用缓存,切换语言后不会残留旧文案。
		internal static CoefficientLabels Load()
		{
			return new CoefficientLabels(
				Text("HEXTECH_STAT_COEFF_HEALTH"),
				Text("HEXTECH_STAT_COEFF_DAMAGE"),
				Text("HEXTECH_STAT_COEFF_BLOCK"),
				Text("HEXTECH_STAT_COEFF_HEALING"));

			static string Text(string key) => new LocString(HextechRuneLabels.LocTable, key).GetRawText();
		}

		internal bool IsCoefficientLine(string line)
		{
			string trimmed = line.TrimStart();
			return trimmed.StartsWith(Health, StringComparison.Ordinal)
				|| trimmed.StartsWith(Damage, StringComparison.Ordinal)
				|| trimmed.StartsWith(Block, StringComparison.Ordinal)
				|| trimmed.StartsWith(Healing, StringComparison.Ordinal);
		}
	}

	private static void RememberOwner(NTopBarPortraitTip portraitTip, IRunState runState)
	{
		if (LocalContext.GetMe(runState) is { } localPlayer)
		{
			PortraitOwners.AddOrUpdate(portraitTip, localPlayer);
		}
	}

	private static void UpdatePortraitTip(NTopBarPortraitTip portraitTip)
	{
		try
		{
			if (!PortraitOwners.TryGetValue(portraitTip, out Player? player))
			{
				return;
			}

			// 字段类型是 IHoverTip:取出的就是箱体本身,就地改写后字段里的提示同步更新。
			FieldInfo portraitHoverTipField = PortraitHoverTipField!;
			if (portraitHoverTipField.GetValue(portraitTip) is not HoverTip hoverTip)
			{
				return;
			}

			object boxedHoverTip = hoverTip;
			CoefficientLabels labels = CoefficientLabels.Load();
			HextechHoverTipAccess.TrySetDescription(
				boxedHoverTip,
				BuildDescription(RemoveExistingCoefficientLines(hoverTip.Description, labels), player, labels));
			portraitHoverTipField.SetValue(portraitTip, boxedHoverTip);
		}
		catch (Exception ex)
		{
			if (HextechRunLogBudget.TryConsume("ui.player-stats-hover-update-failure", 3))
			{
				HextechLog.Warn("Mayhem", $"Failed to update portrait stat hover tip: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	private static string BuildDescription(string baseDescription, Player player, CoefficientLabels labels)
	{
		HextechPlayerCoefficients coefficients = HextechPlayerCoefficientHelper.Get(player);
		return string.Join(
			'\n',
			[
				baseDescription.TrimEnd(),
				$"{labels.Health}{HextechPlayerCoefficientHelper.FormatPercent(coefficients.Health)}",
				$"{labels.Damage}{HextechPlayerCoefficientHelper.FormatPercent(coefficients.Damage)}",
				$"{labels.Block}{HextechPlayerCoefficientHelper.FormatPercent(coefficients.Block)}",
				$"{labels.Healing}{HextechPlayerCoefficientHelper.FormatPercent(coefficients.Healing)}"
			]);
	}

	private static string RemoveExistingCoefficientLines(string description, CoefficientLabels labels)
	{
		string[] lines = description.Replace("\r\n", "\n").Split('\n');
		int end = lines.Length;
		while (end > 0 && labels.IsCoefficientLine(lines[end - 1]))
		{
			end--;
		}

		return string.Join('\n', lines.Take(end));
	}

	[HarmonyPatch(typeof(NTopBarPortraitTip), "Initialize", typeof(IRunState))]
	[HextechPatch("ui.player-stats-hover.init", "玩家属性悬浮")]
	private static class InitializePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => MembersAvailable;

		[HarmonyPostfix]
		private static void Postfix(NTopBarPortraitTip __instance, IRunState runState)
		{
			try
			{
				RememberOwner(__instance, runState);
			}
			catch (Exception ex)
			{
				if (HextechRunLogBudget.TryConsume("ui.player-stats-hover-update-failure", 3))
				{
					HextechLog.Warn("Mayhem", $"Failed to resolve portrait owner: {ex.GetType().Name}: {ex.Message}");
				}

				return;
			}

			UpdatePortraitTip(__instance);
		}
	}

	[HarmonyPatch(typeof(NTopBarPortraitTip), "OnFocus", new Type[0])]
	[HextechPatch("ui.player-stats-hover.focus", "玩家属性悬浮")]
	private static class OnFocusPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => MembersAvailable;

		[HarmonyPrefix]
		private static void Prefix(NTopBarPortraitTip __instance)
		{
			UpdatePortraitTip(__instance);
		}
	}
}
