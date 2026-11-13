using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace HextechRunes;

/// <summary>对局记录遗物栏标题:把本模组遗物从"初始"计数里拆出,单列为"N海克斯"。</summary>
/// <remarks>
/// 本模组遗物底层都是原版 <c>RelicRarity.Starter</c>,原版 <c>NRelicHistory.LoadRelics</c> 按 <c>Rarity</c> 计数后
/// 用 <c>run_history</c> 表的 <c>RELIC_HISTORY.categories</c>(变量 <c>{StarterRelics}</c> 等)拼出标题,海克斯被算进初始。
/// 不改原版本地化表、不改计数字典:后缀确认标题以原版分类串结尾后,只把这段替换成"初始减去海克斯"的分类串
/// 加上本模组的 <c>HEXTECH_RUN_HISTORY_RELICS</c> 片段;没有本模组遗物或标题已被其他模组改写时原样保留。
/// 九种语言的原版分类串都以初始一项收尾,片段按各语言的分隔写法接在其后。
/// </remarks>
internal static class HextechRunHistoryHooks
{
	internal const string HextechRelicsFragmentKey = "HEXTECH_RUN_HISTORY_RELICS";
	internal const string StarterRelicsVariable = "StarterRelics";
	internal const string HextechRelicsVariable = "HextechRelics";

	private static readonly FieldInfo? RelicCategoriesField = HextechHookReflection.TryGetField(typeof(NRelicHistory), "_relicCategories");
	private static readonly FieldInfo? HeaderLabelField = HextechHookReflection.TryGetField(typeof(NRelicHistory), "_headerLabel");

	internal readonly record struct RelicCategorySplit(int StarterRelics, int HextechRelics);

	/// <summary>原版初始计数里有多少属于本模组;海克斯计数不超过原版初始计数。</summary>
	internal static RelicCategorySplit SplitStarterCount(int vanillaStarterRelics, int hextechStarterRelics)
	{
		int hextech = Math.Clamp(hextechStarterRelics, 0, Math.Max(vanillaStarterRelics, 0));
		return new RelicCategorySplit(vanillaStarterRelics - hextech, hextech);
	}

	/// <summary>统计记录里属于本模组、且原版会计入初始的遗物数。</summary>
	internal static int CountHextechStarterRelics(IEnumerable<ModelId?> relicIds, Func<ModelId, bool> isHextechStarterRelic)
	{
		int count = 0;
		foreach (ModelId? id in relicIds)
		{
			if (id != null && isHextechStarterRelic(id))
			{
				count++;
			}
		}

		return count;
	}

	/// <summary>
	/// 重写标题文本:<paramref name="formatCategories"/> 按给定初始计数产出原版分类串(已按原版去掉首尾逗号),
	/// <paramref name="formatFragment"/> 按(剩余初始, 海克斯)产出本模组片段。没有海克斯或标题不以原版分类串结尾时返回原文。
	/// </summary>
	internal static string RewriteHeaderText(
		string headerText,
		int vanillaStarterRelics,
		int hextechStarterRelics,
		Func<int, string> formatCategories,
		Func<RelicCategorySplit, string> formatFragment)
	{
		RelicCategorySplit split = SplitStarterCount(vanillaStarterRelics, hextechStarterRelics);
		if (split.HextechRelics <= 0)
		{
			return headerText;
		}

		string vanillaCategories = formatCategories(vanillaStarterRelics);
		if (!headerText.EndsWith(vanillaCategories, StringComparison.Ordinal))
		{
			return headerText;
		}

		string prefix = headerText[..(headerText.Length - vanillaCategories.Length)];
		return prefix + formatCategories(split.StarterRelics) + formatFragment(split);
	}

	private static bool IsHextechStarterRelicId(ModelId id)
	{
		return HextechCatalog.IsHextechManagedRelicId(id)
			&& ModelDb.GetById<RelicModel>(id).Rarity == RelicRarity.Starter;
	}

	[HarmonyPatch(typeof(NRelicHistory), nameof(NRelicHistory.LoadRelics), typeof(Player), typeof(IEnumerable<SerializableRelic>))]
	[HextechPatch("ui.run-history.relic-categories", "对局记录遗物统计", Optional = true)]
	private static class LoadRelicsPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RelicCategoriesField != null && HeaderLabelField != null;

		[HarmonyPrefix]
		private static void Prefix(ref IEnumerable<SerializableRelic> relics, out int __state)
		{
			// 原版自己也会 ToList;这里先物化,保证计数与原版看到的是同一批遗物。
			List<SerializableRelic> list = relics as List<SerializableRelic> ?? relics.ToList();
			relics = list;
			__state = CountHextechStarterRelics(list.Select(static relic => relic.Id), IsHextechStarterRelicId);
		}

		[HarmonyPostfix]
		private static void Postfix(NRelicHistory __instance, int __state)
		{
			if (__state <= 0
				|| RelicCategoriesField!.GetValue(__instance) is not LocString categories
				|| HeaderLabelField!.GetValue(__instance) is not MegaRichTextLabel headerLabel
				|| !categories.Variables.TryGetValue(StarterRelicsVariable, out object? starterValue)
				|| !LocString.Exists(HextechRuneLabels.LocTable, HextechRelicsFragmentKey))
			{
				return;
			}

			int vanillaStarter = Convert.ToInt32(starterValue);
			string original = headerLabel.Text;
			string rewritten = RewriteHeaderText(
				original,
				vanillaStarter,
				__state,
				starter => FormatCategories(categories, starter),
				FormatFragment);
			// 分类串对象由原版复用,重写后把变量恢复为原版计数。
			categories.Add(StarterRelicsVariable, vanillaStarter);
			if (!string.Equals(rewritten, original, StringComparison.Ordinal))
			{
				headerLabel.Text = rewritten;
			}
		}

		private static string FormatCategories(LocString categories, int starterRelics)
		{
			categories.Add(StarterRelicsVariable, starterRelics);
			return categories.GetFormattedText().Trim(',');
		}

		private static string FormatFragment(RelicCategorySplit split)
		{
			LocString fragment = new(HextechRuneLabels.LocTable, HextechRelicsFragmentKey);
			fragment.Add(StarterRelicsVariable, split.StarterRelics);
			fragment.Add(HextechRelicsVariable, split.HextechRelics);
			return fragment.GetFormattedText();
		}
	}
}
