namespace HextechRunes;

/// <summary>
/// 图鉴与检视列表“此刻列出哪些海克斯”的口径：在注册表的静态清单（<see cref="HextechCatalog"/>）之上，
/// 叠加玩家的配置开关与本机进度。注册表只回答“有哪些、怎么分组”，不查配置与进度。
/// </summary>
internal static class HextechCollectionVisibility
{
	public static bool IsPlayerRuneVisible(Type runeType)
	{
		PlayerRuneMetadataCatalog metadata = HextechContentRegistry.PlayerRuneMetadata;
		if (!metadata.AllTypes.Contains(runeType) || !HextechCatalog.IsPlayerRuneTypeShownInUi(runeType))
		{
			return false;
		}

		// 弑神不进任何池子；本机完成弑神后作为纪念出现在图鉴里。
		if (runeType == typeof(GodslayerRune))
		{
			return HextechSignalNoise.HasSlain();
		}

		if (metadata.IsConfigurable(runeType))
		{
			return HextechRuneConfiguration.IsPlayerRuneEnabled(ModelDb.GetId(runeType).Entry);
		}

		return metadata.IsVisible(runeType);
	}

	public static IReadOnlyList<RelicModel> GetGenericRunes()
	{
		return HextechCatalog.GetCanonicalGenericRunes(IsPlayerRuneVisible);
	}

	public static IReadOnlyList<HextechCatalog.RuneSeriesGroup> GetCharacterRuneGroups()
	{
		return HextechCatalog.GetCharacterRuneGroups(IsPlayerRuneVisible);
	}

	public static IReadOnlyList<RelicModel> GetCustomRelics()
	{
		return HextechCatalog.GetCanonicalCustomRelics(IsPlayerRuneVisible);
	}
}
