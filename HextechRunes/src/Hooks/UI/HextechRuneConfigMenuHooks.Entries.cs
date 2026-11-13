using MegaCrit.Sts2.Core.Localization;

namespace HextechRunes;

internal static partial class HextechRuneConfigMenuHooks
{
	private static List<RuneConfigEntry> BuildRuneEntries()
	{
		List<RuneConfigEntry> entries = [];
		foreach (Type runeType in HextechCatalog.GetAllConfigurableRuneTypes())
		{
			RelicModel relic = ModelDb.GetById<RelicModel>(ModelDb.GetId(runeType));
			// 生成型符文(由其它符文当场生成)不进池,也不在配置界面列出;本变体没有对应角色的专属符文(旧变体的愚者池)同样不列出,
			// 它们的启用状态仍按 ID 保留在配置里。
			if (relic is IHextechGeneratedRune || !HextechCatalog.IsPlayerRuneTypeShownInUi(runeType))
			{
				continue;
			}

			ModelId id = relic.CanonicalId();
			HextechRarityTier rarity = GetRuneRarity(runeType);
			string rarityKey = rarity.ToString().ToUpperInvariant();
			string poolKey = HextechCatalog.GetPlayerRunePoolKey(relic);
			string tagKey = HextechCatalog.GetPlayerRuneTagKey(relic);
			string sourceKey = GetConfigSourceKey(id);
			string sourceText = GetConfigSourceText(id);
			entries.Add(new RuneConfigEntry(
				id.Entry,
				relic,
				relic.Title.GetFormattedText(),
				new LocString(HextechRuneLabels.LocTable, "HEXTECH_SERIES." + rarityKey).GetRawText(),
				(int)rarity,
				poolKey,
				tagKey,
				sourceKey,
				sourceText));
		}

		return entries
			.OrderBy(static entry => entry.RarityOrder)
			.ThenBy(static entry => entry.SourceKey, StringComparer.Ordinal)
			.ThenBy(static entry => entry.PoolKey, StringComparer.Ordinal)
			.ThenBy(static entry => entry.TagKey, StringComparer.Ordinal)
			.ThenBy(static entry => entry.Title, StringComparer.CurrentCulture)
			.ToList();
	}

	private static List<RuneConfigEntry> BuildEnemyHexEntries()
	{
		List<RuneConfigEntry> entries = [];
		foreach (MonsterHexKind kind in Enum.GetValues<HextechRarityTier>()
			.SelectMany(MonsterHexCatalog.GetMonsterHexesForRarity))
		{
			RelicModel relic = MonsterHexCatalog.GetIconRelicForMonsterHex(kind);
			HextechRarityTier rarity = MonsterHexCatalog.GetMonsterHexRarity(kind);
			string rarityKey = rarity.ToString().ToUpperInvariant();
			entries.Add(new RuneConfigEntry(
				kind.ToString(),
				relic,
				relic.Title.GetFormattedText(),
				new LocString(HextechRuneLabels.LocTable, "HEXTECH_SERIES." + rarityKey).GetRawText(),
				(int)rarity,
				EnemyPoolKey,
				kind.ToString(),
				BaseConfigSourceKey,
				L("HEXTECH_CONFIG_SOURCE_BASE")));
		}

		return entries
			.OrderBy(static entry => entry.RarityOrder)
			.ThenBy(static entry => entry.Title, StringComparer.CurrentCulture)
			.ToList();
	}

	private static List<RuneConfigEntry> BuildForgeEntries()
	{
		List<RuneConfigEntry> entries = [];
		foreach (Type forgeType in HextechCatalog.GetAllForgeTypes())
		{
			RelicModel relic = ModelDb.GetById<RelicModel>(ModelDb.GetId(forgeType));
			ModelId id = relic.CanonicalId();
			HextechRarityTier rarity = HextechCatalog.TryGetForgeRarity(relic, out HextechRarityTier resolvedRarity)
				? resolvedRarity
				: HextechRarityTier.Gold;
			string rarityKey = rarity.ToString().ToUpperInvariant();
			string sourceKey = GetConfigSourceKey(id);
			string sourceText = GetConfigSourceText(id);
			entries.Add(new RuneConfigEntry(
				id.Entry,
				relic,
				relic.Title.GetFormattedText(),
				new LocString(HextechRuneLabels.LocTable, "HEXTECH_SERIES." + rarityKey).GetRawText(),
				(int)rarity,
				ForgePoolKey,
				forgeType.Name,
				sourceKey,
				sourceText));
		}

		return entries
			.OrderBy(static entry => entry.RarityOrder)
			.ThenBy(static entry => entry.SourceKey, StringComparer.Ordinal)
			.ThenBy(static entry => entry.Title, StringComparer.CurrentCulture)
			.ToList();
	}

	private static string GetConfigSourceKey(ModelId id)
	{
		string? assetModId = HextechExternalContentRegistry.GetAssetModId(id);
		return string.IsNullOrWhiteSpace(assetModId) || string.Equals(assetModId, ModInfo.Id, StringComparison.Ordinal)
			? BaseConfigSourceKey
			: ExternalConfigSourcePrefix + assetModId;
	}

	private static string GetConfigSourceText(ModelId id)
	{
		string? assetModId = HextechExternalContentRegistry.GetAssetModId(id);
		if (string.IsNullOrWhiteSpace(assetModId) || string.Equals(assetModId, ModInfo.Id, StringComparison.Ordinal))
		{
			return L("HEXTECH_CONFIG_SOURCE_BASE");
		}

		string? titleKey = HextechExternalContentRegistry.GetConfigSectionTitleKey(assetModId);
		string? customTitle = titleKey == null ? null : HextechRuneLabels.TryGetText(titleKey);
		if (!string.IsNullOrWhiteSpace(customTitle))
		{
			return customTitle;
		}

		if (string.Equals(assetModId, HextechExternalContentRegistry.SponsorPackModId, StringComparison.Ordinal))
		{
			return L("HEXTECH_CONFIG_SOURCE_EXTRA_PACK");
		}

		return string.Format(L("HEXTECH_CONFIG_SOURCE_EXTERNAL"), assetModId);
	}

	private static HextechRarityTier GetRuneRarity(Type runeType)
	{
		if (HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(HextechRarityTier.Silver).Contains(runeType))
		{
			return HextechRarityTier.Silver;
		}

		if (HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(HextechRarityTier.Prismatic).Contains(runeType))
		{
			return HextechRarityTier.Prismatic;
		}

		return HextechRarityTier.Gold;
	}

	/// <summary>页脚摘要:符文池页显示双方启用数,锻造页显示锻造启用数,其它页留空(仍占一行高度)。</summary>
	private static string BuildSummaryText(ConfigPage page, ConfigPoolIds poolIds, PendingConfig pending)
	{
		return page switch
		{
			ConfigPage.RunePools => $"{L("HEXTECH_PLAYER_POOL_TITLE")} {ConfigPoolIds.CountEnabled(poolIds.Player, pending.DisabledPlayerRuneIds)}/{poolIds.Player.Count}  |  {L("HEXTECH_ENEMY_POOL_TITLE")} {ConfigPoolIds.CountEnabled(poolIds.Enemy, pending.DisabledMonsterHexIds)}/{poolIds.Enemy.Count}",
			ConfigPage.Forges => $"{L("HEXTECH_CONFIG_TAB_FORGES")} {ConfigPoolIds.CountEnabled(poolIds.Forge, pending.DisabledForgeIds)}/{poolIds.Forge.Count}",
			_ => string.Empty
		};
	}

	private static MonsterHexKind? GetEnemyHexKind(RuneConfigEntry entry)
	{
		return entry.PoolKey == EnemyPoolKey && Enum.TryParse(entry.Id, out MonsterHexKind monsterHex)
			? monsterHex
			: null;
	}

	/// <summary>本菜单的文案;缺键(外部模组未提供等)时显示键名本身,不让整个菜单构建失败。</summary>
	private static string L(string key)
	{
		return HextechRuneLabels.TryGetText(key) ?? key;
	}
}
