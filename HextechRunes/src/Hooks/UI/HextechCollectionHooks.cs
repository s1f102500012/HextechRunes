using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Unlocks;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCollectionHooks
{
	private const float GenericToCharacterPoolSpacing = 96f;

	// 以下均为原版 NRelicCollectionCategory / NRelicCollection 的私有成员(0.107.1 / 0.111.0 同名同签名)。
	// 缺失时不逐项告警,由 CollectionHooksAvailable 按"整体停用/退化为平铺"汇总一次。
	private static readonly FieldInfo? HeaderLabelField = TryGetField(
		typeof(NRelicCollectionCategory),
		"_headerLabel",
		BindingFlags.Instance | BindingFlags.NonPublic,
		warnIfMissing: false);

	private static readonly FieldInfo? SubCategoriesField = TryGetField(
		typeof(NRelicCollectionCategory),
		"_subCategories",
		BindingFlags.Instance | BindingFlags.NonPublic,
		warnIfMissing: false);

	private static readonly FieldInfo? RelicsContainerField = TryGetField(
		typeof(NRelicCollectionCategory),
		"_relicsContainer",
		BindingFlags.Instance | BindingFlags.NonPublic,
		warnIfMissing: false);

	private static readonly MethodInfo? CreateForSubcategoryMethod = TryGetMethod(
		typeof(NRelicCollectionCategory),
		"CreateForSubcategory",
		BindingFlags.Instance | BindingFlags.NonPublic,
		warnIfMissing: false);

	private static readonly MethodInfo? LoadSubcategoryMethod = TryGetMethod(
		typeof(NRelicCollectionCategory),
		"LoadSubcategory",
		BindingFlags.Instance | BindingFlags.NonPublic,
		warnIfMissing: false,
		typeof(NRelicCollection),
		typeof(LocString),
		typeof(IEnumerable<RelicModel>),
		typeof(HashSet<RelicModel>),
		typeof(HashSet<RelicModel>));

	private static string? _starterHeaderTemplate;

	private static bool _loggedFlatFallback;

	private static bool _loggedMissingFallbackContainer;

	/// <summary>图鉴分类依赖原版 NRelicCollectionCategory 的一组私有成员;子分类成员缺失退化为平铺网格,平铺所需的容器也缺失时整体停用。</summary>
	private static bool CollectionHooksAvailable
	{
		get
		{
			List<string> missingSubcategoryDependencies = GetMissingSubcategoryDependencies().ToList();
			if (missingSubcategoryDependencies.Count > 0)
			{
				if (RelicsContainerField == null)
				{
					HextechLog.Warn("Mayhem", $"Relic collection hooks disabled: missing {string.Join(", ", missingSubcategoryDependencies.Append("NRelicCollectionCategory._relicsContainer"))}.");
					return false;
				}

				HextechLog.Warn("Mayhem", $"Relic collection subcategory hooks unavailable: missing {string.Join(", ", missingSubcategoryDependencies)}; using flat starter-grid fallback.");
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(NRelicCollectionCategory), nameof(NRelicCollectionCategory.LoadRelics))]
	[HextechPatch("ui.relic-collection", "遗物图鉴分类", Optional = true)]
	private static class LoadRelicsPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => CollectionHooksAvailable;

		[HarmonyPostfix]
		private static void Postfix(
			NRelicCollectionCategory __instance,
			RelicRarity relicRarity,
			NRelicCollection collection,
			LocString header,
			HashSet<RelicModel> seenRelics,
			UnlockState unlockState,
			HashSet<RelicModel> allUnlockedRelics)
		{
			if (relicRarity != RelicRarity.Starter)
			{
				return;
			}

			_starterHeaderTemplate ??= header.GetRawText();
			if (!CanUseSubcategoryHooks())
			{
				AddFlatFallbackRelics(__instance, collection);
				return;
			}

			AddHextechSubcategory(__instance, collection, seenRelics, allUnlockedRelics);
			AddForgeSubcategory(__instance, collection, seenRelics, allUnlockedRelics);
		}
	}
}
