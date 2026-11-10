using System.Collections;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;

namespace HextechRunes;

/// <summary>
/// 牌组界面"查看升级"取消勾选的兜底还原:原版 NCardGrid 的还原链路没有为
/// "已升级但仍可继续升级"的卡(升级:打击/防御的无限升级)设计过——原版不存在
/// MaxUpgradeLevel&gt;1 的卡(已扫全部41个覆写),取消勾选后部分格子停留在 +1 预览。
/// 这里在开关切到 false 时强制把每个格子还原到基础卡并清预览旗标,
/// 纯表现层,不触碰任何卡牌数据。
/// </summary>
internal static class HextechCardGridPreviewHooks
{
	// 原版 NCardGrid._cardRows(0.107.1 / 0.111.0)。
	private static readonly FieldInfo? CardRowsField = HextechHookReflection.TryGetField(typeof(NCardGrid), "_cardRows");

	// 原版 NGridCardHolder._isPreviewingUpgrade(同上)。
	private static readonly FieldInfo? PreviewFlagField = HextechHookReflection.TryGetField(typeof(NGridCardHolder), "_isPreviewingUpgrade");

	// 原版 NGridCardHolder._baseCard(同上)。
	private static readonly FieldInfo? BaseCardField = HextechHookReflection.TryGetField(typeof(NGridCardHolder), "_baseCard");

	[HarmonyPatch(typeof(NCardGrid), nameof(NCardGrid.IsShowingUpgrades), MethodType.Setter)]
	[HextechPatch("ui.card-grid-preview", "牌组升级预览还原")]
	private static class ShowUpgradesPatch
	{
		// 私有成员缺一即不安装(缺失已进启动摘要),原版行为不受影响。
		[HarmonyPrepare]
		private static bool Prepare() => CardRowsField != null && PreviewFlagField != null && BaseCardField != null;

		[HarmonyPostfix]
		private static void Postfix(NCardGrid __instance, bool value)
		{
			if (value || CardRowsField!.GetValue(__instance) is not IEnumerable rows)
			{
				return;
			}

			FieldInfo previewFlagField = PreviewFlagField!;
			FieldInfo baseCardField = BaseCardField!;

			foreach (object? rowObj in rows)
			{
				if (rowObj is not IEnumerable row)
				{
					continue;
				}

				foreach (NGridCardHolder holder in row.OfType<NGridCardHolder>())
				{
					if (previewFlagField.GetValue(holder) is not true
						|| baseCardField.GetValue(holder) is not CardModel baseCard
						|| holder.CardNode == null)
					{
						continue;
					}

					holder.CardNode.Model = baseCard;
					holder.CardNode.UpdateVisuals(holder.CardNode.DisplayingPile, CardPreviewMode.Normal);
					previewFlagField.SetValue(holder, false);
				}
			}
		}
	}
}
