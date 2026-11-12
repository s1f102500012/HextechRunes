using System.Text.Json;
using System.Text.Json.Serialization;

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
		if (HextechPlayerContextHelper.IsClientRun(fallbackWhenUnavailable: true))
		{
			return HextechRuneConfiguration.GetDefaultSnapshot();
		}

		HextechRunConfigurationSnapshot frozen = FreezeNewRunConfigurationSnapshot(
			HextechGodslayerTrial.ApplyToNewRun(HextechRuneConfiguration.GetSnapshot()),
			HextechModelDbState.IsContentRegistered,
			HextechPlayerContextHelper.IsNetworkMultiplayerRun() ? HextechRuntimeRuneCompatibility.GetHookFailedPlayerRuneIds() : null,
			out List<string> addedHookFailedIds,
			out List<string> skippedHookFailedIds);
		HextechRuntimeRuneCompatibility.LogHostHookFailedMerge("new run", addedHookFailedIds, skippedHookFailedIds);
		return frozen;
	}

	/// <summary>
	/// 房主或单机新开局时冻结本局快照（客户端不走这里，等房主幕掷骰下发）。
	/// 锻造器禁用列表只保留注册表里存在的 ID，只改本局快照，不改本机配置文件；内容尚未登记完时原样保留，以免误删。
	/// 联机房主传入本机补丁失败的海克斯 ID（单机传 null），其中可配置的并入本局禁用列表，之后随幕掷骰载荷下发、
	/// 随存档保存，各端只按这份列表过滤。
	/// </summary>
	internal static HextechRunConfigurationSnapshot FreezeNewRunConfigurationSnapshot(
		HextechRunConfigurationSnapshot snapshot,
		bool isContentRegistered,
		IReadOnlyCollection<string>? hostHookFailedRuneIds,
		out List<string> addedHookFailedIds,
		out List<string> skippedHookFailedIds)
	{
		HextechRunConfigurationSnapshot frozen = isContentRegistered
			? snapshot with { DisabledForgeIds = FilterRegisteredForgeIds(snapshot.DisabledForgeIds) }
			: snapshot;
		if (hostHookFailedRuneIds == null)
		{
			addedHookFailedIds = [];
			skippedHookFailedIds = [];
			return frozen;
		}

		return frozen with
		{
			DisabledPlayerRuneIds = HextechRuntimeRuneCompatibility.MergeHookFailedIntoDisabledIds(
				frozen.DisabledPlayerRuneIds,
				hostHookFailedRuneIds,
				HextechRuntimeRuneCompatibility.GetConfigurablePlayerRuneEntries(),
				out addedHookFailedIds,
				out skippedHookFailedIds)
		};
	}

	private static HashSet<string> FilterRegisteredForgeIds(IEnumerable<string> disabledForgeIds)
	{
		HashSet<string> registeredIds = HextechCatalog.GetAllForgeTypes()
			.Select(static type => ModelDb.GetId(type).Entry)
			.ToHashSet(StringComparer.Ordinal);
		return HextechRuneConfiguration.NormalizeStringIds(disabledForgeIds, registeredIds);
	}

	private string SerializeRunConfigurationSnapshot()
	{
		HextechRunConfigurationSnapshot? snapshot = _runContext.RunConfigurationSnapshot;
		if (snapshot == null)
		{
			return "";
		}

		// 每幕数量与玩家符文禁用表另有单独的存档属性，运行时真值在 _runContext；快照里的这三项写出当前真值，
		// 两份在写出时恒等，读档时无论原版先还原哪一份结果都相同（原版按类型分组还原：int[] 先于 string）。
		// 禁用表没有本局快照时保留快照自身的值，不让存档属性的 getter 读本机菜单配置。
		// 该 JSON 存在 Modifier 上，不参与原版联机校验，两端一致靠本模组保证；集合字段用排序数组序列化，
		// 不依赖 HashSet 未文档化的插入序枚举；属性名与原 record 一致，Restore 仍反序列化回原类型。
		HextechRunConfigurationSnapshot normalized = HextechRuneConfiguration.NormalizeSnapshot(snapshot with
		{
			PlayerHexCountsByAct = _runContext.PlayerHexCounts.Snapshot,
			EnemyHexCountsByAct = _runContext.EnemyHexCounts.Snapshot,
			DisabledPlayerRuneIds = _runContext.PlayerRuneConfig.CopySnapshotOrNull() ?? snapshot.DisabledPlayerRuneIds
		});
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
		int ChaosRuneChancePercent,
		// 普通局为 false 不写出，存档 JSON 与不带该字段时逐字节一致。
		[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		bool GodslayerTrial)
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
				snapshot.ChaosRuneChancePercent,
				snapshot.GodslayerTrial)
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
