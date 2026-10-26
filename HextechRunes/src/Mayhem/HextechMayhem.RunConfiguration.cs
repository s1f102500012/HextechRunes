using System.Text.Json;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public int[] PlayerHexCountsByAct => _runContext.PlayerHexCounts.Snapshot;

	internal IReadOnlySet<string> DisabledMonsterHexIdsForPool => GetEffectiveRunConfigurationSnapshot().DisabledMonsterHexIds;

	internal IReadOnlySet<string> DisabledForgeIdsForPool => GetEffectiveRunConfigurationSnapshot().DisabledForgeIds;

	internal HextechRarityWeights GetRuneRarityWeightsForAct(int actIndex)
	{
		return GetEffectiveRunConfigurationSnapshot().GetRuneRarityWeightsForAct(actIndex);
	}

	internal bool PreventConsecutiveSilverRunes => GetEffectiveRunConfigurationSnapshot().PreventConsecutiveSilverRunes;

	internal int GoldenRerollChancePercent => GetEffectiveRunConfigurationSnapshot().GoldenRerollChancePercent;

	internal HextechForgeRarityWeights ForgeRarityWeights => GetEffectiveRunConfigurationSnapshot().ForgeRarityWeights;

	internal int RandomForgeShopPrice => GetEffectiveRunConfigurationSnapshot().RandomForgeShopPrice;

	internal bool RandomForgeDirectGrant => GetEffectiveRunConfigurationSnapshot().RandomForgeDirectGrant;

	// 模组总开关:本局逐 act 的「有效配置值」(联机里来自房主的同步快照)。
	internal bool ModEnabled => _runContext.RunConfigurationSnapshot?.ModEnabled
		?? GetEffectiveRunConfigurationSnapshot().ModEnabled;

	// 首次 act-roll 同步完成后冻结本局开关，之后不随进幕或局内改配置而变。
	// 冻结前使用本局有效配置；客户端必须等房主快照同步后再冻结。
	internal bool IsModActiveForRun => _runContext.ModActiveForRun ?? ModEnabled;

	// act-roll 同步后首次冻结有效配置值；返回 true 表示本局禁用模组。
	internal bool FreezeModActiveForRunAndCheckDisabled()
	{
		if (_runContext.ModActiveForRun == null)
		{
			_runContext.ModActiveForRun = ModEnabled;
			HextechLog.Info("Mayhem", $"Mod-active frozen for run: active={_runContext.ModActiveForRun}");
		}

		return _runContext.ModActiveForRun == false;
	}

	internal int PlayerRuneRerollLimit => GetEffectiveRunConfigurationSnapshot().PlayerRuneRerollLimit;

	internal int MonsterHexRerollLimit => GetEffectiveRunConfigurationSnapshot().MonsterHexRerollLimit;

	internal int GetPlayerHexCountForAct(int actIndex)
	{
		return _runContext.PlayerHexCounts.GetForAct(actIndex, IsEndlessLoopActive);
	}

	internal HextechRunConfigurationSnapshot GetEffectiveRunConfigurationSnapshot()
	{
		// 没有本局快照时:客户端用默认值(不能用本地菜单值),其余读本地配置。数量与禁用表总以本局状态为准。
		HextechRunConfigurationSnapshot baseSnapshot = _runContext.RunConfigurationSnapshot
			?? (HextechPlayerContextHelper.IsClientRun()
				? HextechRuneConfiguration.GetDefaultSnapshot()
				: HextechRuneConfiguration.GetSnapshot());
		return HextechRuneConfiguration.NormalizeSnapshot(baseSnapshot with
		{
			PlayerHexCountsByAct = _runContext.PlayerHexCounts.Snapshot,
			EnemyHexCountsByAct = _runContext.EnemyHexCounts.Snapshot,
			DisabledPlayerRuneIds = PlayerRuneConfigDisabledIds.ToHashSet(StringComparer.Ordinal)
		});
	}

	internal void SetRunConfigurationSnapshot(HextechRunConfigurationSnapshot snapshot, string reason)
	{
		HextechRunConfigurationSnapshot normalized = HextechRuneConfiguration.NormalizeSnapshot(snapshot);
		_runContext.RunConfigurationSnapshot = normalized.Copy();
		_runContext.PlayerHexCounts.Set(normalized.PlayerHexCountsByAct);
		_runContext.EnemyHexCounts.Set(normalized.EnemyHexCountsByAct);
		_runContext.PlayerRuneConfig.Set(normalized.DisabledPlayerRuneIds);
		string runeWeights = string.Join("/", normalized.RuneRarityWeightsByAct.Select(static weights => $"{weights.Silver},{weights.Gold},{weights.Prismatic}"));
		HextechLog.Info("Mayhem", $"Run config snapshot set: reason={reason} playerCounts={string.Join(",", PlayerHexCountsByAct)} enemyCounts={string.Join(",", EnemyHexCountsByAct)} playerRerolls={normalized.PlayerRuneRerollLimit} monsterRerolls={normalized.MonsterHexRerollLimit} runeWeightsByAct={runeWeights} preventConsecutiveSilver={normalized.PreventConsecutiveSilverRunes} goldenRerollChance={normalized.GoldenRerollChancePercent}% playerDisabled={PlayerRuneConfigDisabledIds.Count} enemyDisabled={normalized.DisabledMonsterHexIds.Count} forgeDisabled={normalized.DisabledForgeIds.Count} forgePrice={normalized.RandomForgeShopPrice} forgeDirect={normalized.RandomForgeDirectGrant}");
	}

	private static HextechRunConfigurationSnapshot CreateNewRunConfigurationSnapshot()
	{
		return HextechPlayerContextHelper.IsClientRun(fallbackWhenUnavailable: true)
			? HextechRuneConfiguration.GetDefaultSnapshot()
			: HextechGodslayerTrial.ApplyToNewRun(HextechRuneConfiguration.GetSnapshot());
	}

	private string SerializeRunConfigurationSnapshot()
	{
		HextechRunConfigurationSnapshot? snapshot = _runContext.RunConfigurationSnapshot;
		if (snapshot == null)
		{
			return "";
		}

		// 该 JSON 进 SavedProperty 参与双端比对，集合字段用排序数组序列化，
		// 不依赖 HashSet 未文档化的插入序枚举；属性名与原 record 一致，Restore 仍反序列化回原类型。
		HextechRunConfigurationSnapshot normalized = HextechRuneConfiguration.NormalizeSnapshot(snapshot);
		return JsonSerializer.Serialize(new RunConfigurationSnapshotJson(normalized), HextechTelemetry.JsonOptions);
	}

	private sealed record RunConfigurationSnapshotJson(
		int[] PlayerHexCountsByAct,
		int[] EnemyHexCountsByAct,
		int PlayerRuneRerollLimit,
		int MonsterHexRerollLimit,
		string[] DisabledPlayerRuneIds,
		string[] DisabledMonsterHexIds,
		string[] DisabledForgeIds,
		HextechRarityWeights[] RuneRarityWeightsByAct,
		bool PreventConsecutiveSilverRunes,
		int GoldenRerollChancePercent,
		HextechForgeRarityWeights ForgeRarityWeights,
		int RandomForgeShopPrice,
		bool RandomForgeDirectGrant,
		bool ModEnabled,
		int ChaosRuneChancePercent)
	{
		public RunConfigurationSnapshotJson(HextechRunConfigurationSnapshot snapshot)
			: this(
				snapshot.PlayerHexCountsByAct,
				snapshot.EnemyHexCountsByAct,
				snapshot.PlayerRuneRerollLimit,
				snapshot.MonsterHexRerollLimit,
				OrderedIds(snapshot.DisabledPlayerRuneIds),
				OrderedIds(snapshot.DisabledMonsterHexIds),
				OrderedIds(snapshot.DisabledForgeIds),
				snapshot.RuneRarityWeightsByAct,
				snapshot.PreventConsecutiveSilverRunes,
				snapshot.GoldenRerollChancePercent,
				snapshot.ForgeRarityWeights,
				snapshot.RandomForgeShopPrice,
				snapshot.RandomForgeDirectGrant,
				snapshot.ModEnabled,
				snapshot.ChaosRuneChancePercent)
		{
		}

		private static string[] OrderedIds(IEnumerable<string> ids)
		{
			return ids.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
		}
	}

	private sealed record LegacyRunConfigurationSnapshotJson(
		int[] PlayerHexCountsByAct,
		int[] EnemyHexCountsByAct,
		int PlayerRuneRerollLimit,
		int MonsterHexRerollLimit,
		string[] DisabledPlayerRuneIds,
		string[] DisabledMonsterHexIds,
		string[] DisabledForgeIds,
		HextechRarityWeights FirstActRuneRarityWeights,
		HextechRarityWeights NormalRuneRarityWeights,
		HextechRarityWeights SecondActAfterSilverRuneRarityWeights,
		HextechForgeRarityWeights ForgeRarityWeights,
		int RandomForgeShopPrice,
		bool RandomForgeDirectGrant,
		bool ModEnabled);

	private sealed record PreviousRunConfigurationSnapshotJson(
		int[] PlayerHexCountsByAct,
		int[] EnemyHexCountsByAct,
		int PlayerRuneRerollLimit,
		int MonsterHexRerollLimit,
		string[] DisabledPlayerRuneIds,
		string[] DisabledMonsterHexIds,
		string[] DisabledForgeIds,
		HextechRarityWeights RuneRarityWeights,
		bool PreventConsecutiveSilverRunes,
		int GoldenRerollChancePercent,
		HextechForgeRarityWeights ForgeRarityWeights,
		int RandomForgeShopPrice,
		bool RandomForgeDirectGrant,
		bool ModEnabled);

	private void RestoreRunConfigurationSnapshot(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			_runContext.RunConfigurationSnapshot = null;
			return;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			bool usesActRarityConfig = document.RootElement.EnumerateObject()
				.Any(static property => property.Name.Equals(nameof(HextechRunConfigurationSnapshot.RuneRarityWeightsByAct), StringComparison.OrdinalIgnoreCase));
			bool usesSingleRarityConfig = document.RootElement.EnumerateObject()
				.Any(static property => property.Name.Equals(nameof(PreviousRunConfigurationSnapshotJson.RuneRarityWeights), StringComparison.OrdinalIgnoreCase));
			bool hasGoldenRerollChance = document.RootElement.EnumerateObject()
				.Any(static property => property.Name.Equals(nameof(HextechRunConfigurationSnapshot.GoldenRerollChancePercent), StringComparison.OrdinalIgnoreCase));
			HextechRunConfigurationSnapshot? snapshot;
			if (usesActRarityConfig)
			{
				snapshot = JsonSerializer.Deserialize<HextechRunConfigurationSnapshot>(json, HextechTelemetry.JsonOptions);
				if (snapshot != null && !hasGoldenRerollChance)
				{
					snapshot = snapshot with
					{
						GoldenRerollChancePercent = HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent()
					};
				}
			}
			else if (usesSingleRarityConfig)
			{
				PreviousRunConfigurationSnapshotJson? previous = JsonSerializer.Deserialize<PreviousRunConfigurationSnapshotJson>(json, HextechTelemetry.JsonOptions);
				HextechRarityWeights[] weightsByAct = previous == null
					? HextechRuneConfiguration.GetDefaultRuneRarityWeightsByAct()
					: [ previous.RuneRarityWeights, previous.RuneRarityWeights, previous.RuneRarityWeights ];
				snapshot = previous == null
					? null
					: new HextechRunConfigurationSnapshot(
						previous.PlayerHexCountsByAct,
						previous.EnemyHexCountsByAct,
						previous.PlayerRuneRerollLimit,
						previous.MonsterHexRerollLimit,
						previous.DisabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal),
						previous.DisabledMonsterHexIds.ToHashSet(StringComparer.Ordinal),
						previous.DisabledForgeIds.ToHashSet(StringComparer.Ordinal),
						weightsByAct,
						previous.PreventConsecutiveSilverRunes,
						hasGoldenRerollChance ? previous.GoldenRerollChancePercent : HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent(),
						previous.ForgeRarityWeights,
						previous.RandomForgeShopPrice,
						previous.RandomForgeDirectGrant,
						previous.ModEnabled);
			}
			else
			{
				LegacyRunConfigurationSnapshotJson? legacy = JsonSerializer.Deserialize<LegacyRunConfigurationSnapshotJson>(json, HextechTelemetry.JsonOptions);
				snapshot = legacy == null
					? null
					: new HextechRunConfigurationSnapshot(
						legacy.PlayerHexCountsByAct,
						legacy.EnemyHexCountsByAct,
						legacy.PlayerRuneRerollLimit,
						legacy.MonsterHexRerollLimit,
						legacy.DisabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal),
						legacy.DisabledMonsterHexIds.ToHashSet(StringComparer.Ordinal),
						legacy.DisabledForgeIds.ToHashSet(StringComparer.Ordinal),
						[ legacy.NormalRuneRarityWeights, legacy.NormalRuneRarityWeights, legacy.NormalRuneRarityWeights ],
						HextechRuneConfiguration.GetDefaultPreventConsecutiveSilverRunes(),
						HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent(),
						legacy.ForgeRarityWeights,
						legacy.RandomForgeShopPrice,
						legacy.RandomForgeDirectGrant,
						legacy.ModEnabled);
			}

			if (snapshot != null)
			{
				SetRunConfigurationSnapshot(snapshot, "restore saved run config");
			}
		}
		catch (Exception ex)
		{
			_runContext.RunConfigurationSnapshot = null;
			HextechLog.Warn("Mayhem", $"Run config snapshot restore failed; using runtime fallback: {ex.Message}");
		}
	}
}
