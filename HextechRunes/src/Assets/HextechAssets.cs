namespace HextechRunes;

internal static class HextechAssets
{
	/// <summary>本模组 PCK 的资源根;所有自有资源路径都从这里拼接。</summary>
	private const string ResourceRoot = "res://" + ModInfo.Id + "/";

	private const string CardImages = ResourceRoot + "images/cards/";

	private const string PowerImages = ResourceRoot + "images/powers/";

	private const string RelicImages = ResourceRoot + "images/relics/";

	private const string EffectImages = ResourceRoot + "images/effects/";

	private const string SlowCookEffectImages = EffectImages + "slow_cook/";

	/// <summary>图片资源根；海克斯选择界面原版特效的数据文件按相对此目录的路径引用贴图（不含扩展名）。</summary>
	public const string ImageRoot = ResourceRoot + "images/";

	public const string KiwiSelectionVfxDataPath = EffectImages + "kiwi_selection/kiwi_selection_vfx.json";

	public const string HextechSubcategoryKey = "HEXTECH_RUNES_SUBCATEGORY";

	public const string ForgeSubcategoryKey = "HEXTECH_FORGES_SUBCATEGORY";

	public const string TrickMagicCardPortraitPath = CardImages + "trickMagicCard.png";

	public const string BladeWaltzCardPortraitPath = CardImages + "bladeWaltzCard.png";

	public const string CatalystCardPortraitPath = CardImages + "catalystCard.png";

	public const string WhiteHoleCardPortraitPath = CardImages + "whiteHoleCard.png";

	public const string SearingAttackCardPortraitPath = CardImages + "searingAttackCard.png";

	public const string FeelTheBurnCardPortraitPath = CardImages + "feelTheBurnCard.png";

	public const string OkBoomerangCardPortraitPath = CardImages + "okBoomerangCard.png";

	public const string QuantumComputingCardPortraitPath = CardImages + "quantumComputingCard.png";

	public const string ReprogramCardPortraitPath = CardImages + "reprogramCard.png";

	public const string MikaelsBlessingCardPortraitPath = CardImages + "mikaelsBlessingCard.png";

	public const string OstyWishCardPortraitPath = CardImages + "ostyWishCard.png";

	public const string OceanDragonSoulCardPortraitPath = CardImages + "oceanDragonSoulCard.png";

	public const string InfernalDragonSoulCardPortraitPath = CardImages + "infernalDragonSoulCard.png";

	public const string HextechDragonSoulCardPortraitPath = CardImages + "hextechDragonSoulCard.png";

	public const string MountainDragonSoulCardPortraitPath = CardImages + "mountainDragonSoulCard.png";

	public const string ChemtechDragonSoulCardPortraitPath = CardImages + "chemtechDragonSoulCard.png";

	public const string CloudDragonSoulCardPortraitPath = CardImages + "cloudDragonSoulCard.png";

	public const string OceanDragonSoulPowerIconPath = PowerImages + "hextechOceanDragonSoulPower.png";

	public const string InfernalDragonSoulPowerIconPath = PowerImages + "hextechInfernalDragonSoulPower.png";

	public const string HextechDragonSoulPowerIconPath = PowerImages + "hextechDragonSoulPower.png";

	public const string MountainDragonSoulPowerIconPath = PowerImages + "hextechMountainDragonSoulPower.png";

	public const string ChemtechDragonSoulPowerIconPath = PowerImages + "hextechChemtechDragonSoulPower.png";

	public const string CloudDragonSoulPowerIconPath = PowerImages + "hextechCloudDragonSoulPower.png";

	public const string BurnPowerIconPath = PowerImages + "hextechBurnPower.png";

	public const string AttackReplayPowerIconPath = PowerImages + "hextechAttackReplayPower.png";

	/// <summary>全透明描边:自定义遗物图标没有配套描边图,原版 NRelic 小图标模式会把 IconOutline 画在图标下层,给它一张空图即可。</summary>
	public const string RelicOutlineEmptyPath = ResourceRoot + "images/ui/relicOutlineEmpty.png";

	public const string HandOfBaronAuraRunePath = EffectImages + "jungle_buff_baron.png";

	/// <summary>通用柔光圆环:男爵之手光环与米凯尔的祝福净化特效共用。</summary>
	public const string SoftRingEffectPath = EffectImages + "ring_soft_02.png";

	/// <summary>通用柔光圆盘:男爵之手光环(含烟雾裁剪圆)与米凯尔的祝福净化特效共用。</summary>
	public const string SoftDiscEffectPath = EffectImages + "disc32.ha_crepe.png";

	public const string HandOfBaronAuraSmokePath = EffectImages + "srx_infernal_smoke_trail.png";

	public const string MikaelsBlessingAoeRunePath = EffectImages + "milio_base_r_aoe_rune.png";

	public const string NearDeathFeastGlowPath = EffectImages + "near_death_feast/soul_nexus_glow.png";

	public const string SlowCookHeatGlowPath = SlowCookEffectImages + "heat_glow.png";

	public const string SlowCookAoeGradientPath = SlowCookEffectImages + "aoe_gradient.png";

	public const string SlowCookAoeGradientSubtlePath = SlowCookEffectImages + "aoe_gradient_subtle.png";

	public const string SlowCookAoeEdgePath = SlowCookEffectImages + "aoe_edge.png";

	public const string SlowCookAoePolarPath = SlowCookEffectImages + "aoe_polar.png";

	public const string SlowCookEdgeAccentPath = SlowCookEffectImages + "edge_accent.png";

	public const string SlowCookGroundRingPath = SlowCookEffectImages + "ground_ring.png";

	public const string SlowCookFlameNoisePath = SlowCookEffectImages + "flame_noise.png";

	public const string SlowCookInnerFirePath = SlowCookEffectImages + "inner_fire.png";

	public const string SlowCookInnerFireBPath = SlowCookEffectImages + "inner_fire_b.png";

	public const string SlowCookFlarePath = SlowCookEffectImages + "flare.png";

	public static string? TryGetCustomRelicIconPath(RelicModel relic)
	{
		if (relic is HundredRefinementsHex)
		{
			return RelicImages + "hundredRefinementsRune.png";
		}

		if (relic is HungryHex or InspectHex or GripHex)
		{
			return RelicImages + "eightPennyGateRune.png";
		}

		if (relic is EntomancerHex)
		{
			return RelicImages + "madScientistRune.png";
		}

		if (relic is SomethingForNothingRune)
		{
			return RelicImages + "acceleratingSorceryRune.png";
		}

		if (HextechCatalog.IsHextechEnemyHexIconRelic(relic))
		{
			ModelId id = relic.CanonicalId();
			return $"{RelicImages}{ToImageFileStem(id.Entry)}.png";
		}

		if (HextechCatalog.IsHextechRelic(relic))
		{
			ModelId id = relic.CanonicalId();
			string assetModId = HextechExternalContentRegistry.GetAssetModId(id) ?? ModInfo.Id;
			return $"res://{assetModId}/images/relics/{ToImageFileStem(id.Entry)}.png";
		}

		if (HextechCatalog.TryGetForgeRarity(relic, out HextechRarityTier forgeRarity))
		{
			return GetForgeIconPath(forgeRarity);
		}

		if (HextechCatalog.IsHextechShopRelic(relic))
		{
			return RelicImages + "silverForge.png";
		}

		return null;
	}

	public static string GetForgeIconPath(HextechRarityTier rarity)
	{
		string iconStem = rarity switch
		{
			HextechRarityTier.Silver => "silverForge",
			HextechRarityTier.Gold => "goldForge",
			HextechRarityTier.Prismatic => "prismaticForge",
			_ => "silverForge"
		};
		return $"{RelicImages}{iconStem}.png";
	}

	internal static string ToImageFileStem(string entry)
	{
		string[] parts = entry.ToLowerInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			return entry;
		}

		return parts[0] + string.Concat(parts.Skip(1).Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
	}
}
