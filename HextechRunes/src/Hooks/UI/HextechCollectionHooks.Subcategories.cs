using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;

namespace HextechRunes;

internal static partial class HextechCollectionHooks
{
	private static void AddHextechSubcategory(
		NRelicCollectionCategory self,
		NRelicCollection collection,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (collection.Relics.Any(HextechCatalog.IsHextechRelic))
		{
			return;
		}

		IReadOnlyList<RelicModel> genericRunes = HextechCollectionVisibility.GetGenericRunes();
		IReadOnlyList<HextechCatalog.RuneSeriesGroup> characterGroups = HextechCollectionVisibility.GetCharacterRuneGroups();
		HashSet<RelicModel> visibleHextechRelics = genericRunes
			.Concat(characterGroups.SelectMany(static group => group.Relics))
			.ToHashSet();
		HashSet<RelicModel> seenWithHextech = seenRelics.Concat(visibleHextechRelics).ToHashSet();
		HashSet<RelicModel> unlockedWithHextech = allUnlockedRelics.Concat(visibleHextechRelics).ToHashSet();

		if (CreateAndLoadSubcategory(
			self,
			collection,
			HextechAssets.HextechSubcategoryKey,
			genericRunes,
			seenWithHextech,
			unlockedWithHextech) is not { } subCategory)
		{
			return;
		}

		ApplyCustomHeaderText(subCategory, HextechAssets.HextechSubcategoryKey);

		NRelicCollectionCategory? firstCharacterSubcategory = null;
		foreach (HextechCatalog.RuneSeriesGroup group in characterGroups)
		{
			NRelicCollectionCategory? characterSubcategory = AddCharacterRuneSubcategory(
				subCategory,
				collection,
				group,
				seenWithHextech,
				unlockedWithHextech);
			firstCharacterSubcategory ??= characterSubcategory;
		}

		if (firstCharacterSubcategory != null)
		{
			InsertSpacingBeforeCharacterPools(subCategory, firstCharacterSubcategory);
		}
	}

	private static void AddForgeSubcategory(
		NRelicCollectionCategory self,
		NRelicCollection collection,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (collection.Relics.Any(HextechCatalog.IsHextechForgeRelic))
		{
			return;
		}

		HashSet<RelicModel> visibleForgeRelics = HextechCatalog.GetCanonicalForges().ToHashSet();
		HashSet<RelicModel> seenWithForges = seenRelics.Concat(visibleForgeRelics).ToHashSet();
		HashSet<RelicModel> unlockedWithForges = allUnlockedRelics.Concat(visibleForgeRelics).ToHashSet();

		if (CreateAndLoadSubcategory(
			self,
			collection,
			HextechAssets.ForgeSubcategoryKey,
			HextechCatalog.GetCanonicalForges(),
			seenWithForges,
			unlockedWithForges) is { } subCategory)
		{
			ApplyCustomHeaderText(subCategory, HextechAssets.ForgeSubcategoryKey);
		}
	}

	private static NRelicCollectionCategory? AddCharacterRuneSubcategory(
		NRelicCollectionCategory hextechCategory,
		NRelicCollection collection,
		HextechCatalog.RuneSeriesGroup group,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (group.Relics.Count == 0)
		{
			return null;
		}

		string localizationKey = $"HEXTECH_{group.LocalizationKey}";
		NRelicCollectionCategory? subCategory = CreateAndLoadSubcategory(
			hextechCategory,
			collection,
			localizationKey,
			group.Relics,
			seenRelics,
			allUnlockedRelics);
		if (subCategory != null)
		{
			ApplyCustomHeaderText(subCategory, localizationKey);
		}

		return subCategory;
	}

	/// <summary>
	/// 在 <paramref name="parent"/> 标题下插入一个子分类并载入遗物;私有成员的值类型对不上时返回 null。
	/// 只在 <see cref="CanUseSubcategoryHooks"/> 为真时调用,所需反射成员都已解析。
	/// </summary>
	private static NRelicCollectionCategory? CreateAndLoadSubcategory(
		NRelicCollectionCategory parent,
		NRelicCollection collection,
		string localizationKey,
		IEnumerable<RelicModel> relics,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (SubCategoriesField!.GetValue(parent) is not List<NRelicCollectionCategory> subCategories
			|| HeaderLabelField!.GetValue(parent) is not Control headerLabel
			|| CreateForSubcategoryMethod!.Invoke(parent, null) is not NRelicCollectionCategory subCategory)
		{
			return null;
		}

		int insertIndex = headerLabel.GetIndex() + subCategories.Count + 1;
		subCategories.Add(subCategory);
		parent.AddChild(subCategory);
		parent.MoveChild(subCategory, insertIndex);

		LoadSubcategoryMethod!.Invoke(
			subCategory,
			[
				collection,
				new LocString(HextechRuneLabels.LocTable, localizationKey),
				relics,
				seenRelics,
				allUnlockedRelics
			]);

		return subCategory;
	}

	private static void InsertSpacingBeforeCharacterPools(
		NRelicCollectionCategory hextechCategory,
		NRelicCollectionCategory firstCharacterSubcategory)
	{
		Control spacer = new()
		{
			Name = "HextechGenericToCharacterPoolSpacer",
			CustomMinimumSize = new Vector2(0f, GenericToCharacterPoolSpacing),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		hextechCategory.AddChild(spacer);
		hextechCategory.MoveChild(spacer, firstCharacterSubcategory.GetIndex());
	}
}
