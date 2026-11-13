using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using VanillaBurningBlood = MegaCrit.Sts2.Core.Models.Relics.BurningBlood;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>检视界面类别行:本模组管理的各类遗物统一用"海克斯遗物"键,原版与第三方遗物不接管。</summary>
	[HextechTest]
	private static void InspectRarityLabelUsesHextechRelicKeyForManagedRelics()
	{
		RelicModel[] managed =
		[
			new ColorDiscoveryRune(),
			new StrengthForge(),
			new QueenHex(),
			new RandomForgeShopRelic(),
			new HextechBlackBloodPlus()
		];
		foreach (RelicModel relic in managed)
		{
			string name = relic.GetType().Name;
			Equal(RelicRarity.Starter, relic.Rarity, $"{name} keeps the vanilla Starter rarity");
			Equal<string?>(
				HextechInspectHooks.HextechRelicRarityKey,
				HextechInspectHooks.GetInspectRarityLocKey(relic),
				$"{name} inspect rarity key");
		}

		Equal<string?>(null, HextechInspectHooks.GetInspectRarityLocKey(new VanillaBurningBlood()), "vanilla starter relic stays vanilla");
		Equal<string?>(null, HextechInspectHooks.GetInspectRarityLocKey(new CompatibilityExternalInspectRelicA()), "third-party relic stays vanilla");
	}

	/// <summary>对局记录初始计数拆分:海克斯不超过原版初始数,没有海克斯时标题原样保留。</summary>
	[HextechTest]
	private static void RunHistoryRelicCategoriesSplitHextechFromStarter()
	{
		Equal(new HextechRunHistoryHooks.RelicCategorySplit(2, 0), HextechRunHistoryHooks.SplitStarterCount(2, 0), "no hextech");
		Equal(new HextechRunHistoryHooks.RelicCategorySplit(0, 3), HextechRunHistoryHooks.SplitStarterCount(3, 3), "only hextech");
		Equal(new HextechRunHistoryHooks.RelicCategorySplit(1, 2), HextechRunHistoryHooks.SplitStarterCount(3, 2), "mixed");
		Equal(new HextechRunHistoryHooks.RelicCategorySplit(0, 1), HextechRunHistoryHooks.SplitStarterCount(1, 4), "hextech is clamped to the vanilla starter count");

		ModelId[] ids =
		[
			ModelDb.GetId(typeof(VanillaBurningBlood)),
			ModelDb.GetId(typeof(ColorDiscoveryRune)),
			ModelDb.GetId(typeof(StrengthForge)),
			ModelDb.GetId(typeof(HextechBlackBloodPlus))
		];
		Equal(
			3,
			HextechRunHistoryHooks.CountHextechStarterRelics([.. ids, null], HextechCatalog.IsHextechManagedRelicId),
			"managed relics are counted, vanilla and missing ids are not");

		// 用中文原版分类串的形态模拟格式化:初始一项收尾,片段按剩余初始数决定是否带分隔。
		static string Categories(int starter) => "罕见，" + (starter > 0 ? $"{starter}初始" : string.Empty);
		static string Fragment(HextechRunHistoryHooks.RelicCategorySplit split) =>
			(split.StarterRelics > 0 ? "，" : string.Empty) + $"{split.HextechRelics}海克斯";
		const string Header = "[gold][b]遗物（5）：[/b][/gold]";

		string vanillaOnly = Header + Categories(2);
		Expect(
			ReferenceEquals(vanillaOnly, HextechRunHistoryHooks.RewriteHeaderText(vanillaOnly, 2, 0, Categories, Fragment)),
			"without hextech relics the header text is returned untouched");
		Equal(
			Header + "罕见，3海克斯",
			HextechRunHistoryHooks.RewriteHeaderText(Header + Categories(3), 3, 3, Categories, Fragment),
			"only hextech starter relics");
		Equal(
			Header + "罕见，1初始，2海克斯",
			HextechRunHistoryHooks.RewriteHeaderText(Header + Categories(3), 3, 2, Categories, Fragment),
			"mixed starter and hextech relics");
		string rewrittenElsewhere = Header + "其他模组改写的标题";
		Expect(
			ReferenceEquals(rewrittenElsewhere, HextechRunHistoryHooks.RewriteHeaderText(rewrittenElsewhere, 3, 2, Categories, Fragment)),
			"a header that no longer ends with the vanilla categories is left alone");
	}
}
