namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	/// <summary>
	/// 本幕稀有度抽签。deterministic=true(联机)用稳定哈希,各端独立算出同一结果;
	/// false(单机)推进原版 Niche 随机流。
	/// </summary>
	private static HextechRarityTier RollActRarity(
		HextechMayhemModifier modifier,
		int actIndex,
		RunState runState,
		IReadOnlyList<HextechRarityTier> enabledRarities,
		bool deterministic)
	{
		(HextechRarityWeights weights, IReadOnlyList<HextechRarityTier> eligibleRarities) = ApplyConsecutiveSilverRule(
			modifier.GetRuneRarityWeightsForAct(actIndex),
			enabledRarities,
			modifier.PreventConsecutiveSilverRunes,
			actIndex,
			modifier.GetRarityForAct(actIndex - 1));
		return RollWeightedRarity(runState, weights, deterministic, actIndex, eligibleRarities);
	}

	/// <summary>
	/// "上一幕白银后不再出白银":从第二幕起,上一幕是白银时去掉白银的权重与候选。
	/// 只剩白银权重时改为金色/棱彩各 1;启用稀有度只有白银时候选回落为金色与棱彩。
	/// </summary>
	internal static (HextechRarityWeights Weights, IReadOnlyList<HextechRarityTier> Candidates) ApplyConsecutiveSilverRule(
		HextechRarityWeights configuredWeights,
		IReadOnlyList<HextechRarityTier> enabledRarities,
		bool preventConsecutiveSilverRunes,
		int actIndex,
		HextechRarityTier? previousActRarity)
	{
		if (!preventConsecutiveSilverRunes
			|| actIndex <= 0
			|| previousActRarity != HextechRarityTier.Silver)
		{
			return (configuredWeights, enabledRarities);
		}

		HextechRarityWeights weights = configuredWeights.Gold + configuredWeights.Prismatic > 0
			? configuredWeights with { Silver = 0 }
			: new HextechRarityWeights(0, 1, 1);
		HextechRarityTier[] nonSilverRarities = enabledRarities
			.Where(static rarity => rarity != HextechRarityTier.Silver)
			.ToArray();
		return (weights, nonSilverRarities.Length > 0
			? nonSilverRarities
			: [ HextechRarityTier.Gold, HextechRarityTier.Prismatic ]);
	}

	private sealed record LocalActRoll(
		HextechRarityTier Rarity,
		MonsterHexKind? MonsterHex,
		HextechRunConfigurationSnapshot RunConfigSnapshot,
		IReadOnlySet<string> DisabledPlayerRuneIds);

	private static async Task<(HextechRarityTier Rarity, MonsterHexKind? MonsterHex, int PlayerHexCount)> ResolveActRoll(RunState runState, HextechMayhemModifier modifier, int actIndex)
	{
		RunManager runManager = RunManager.Instance;
		NetGameType gameType = runManager.NetService.Type;
		LocalActRoll local = RollLocalActRoll(runState, modifier, actIndex, isMultiplayer: !HextechPlayerContextHelper.IsSinglePlayerFlow(gameType));
		if (HextechPlayerContextHelper.IsSinglePlayerFlow(gameType))
		{
			modifier.SetRarityForAct(actIndex, local.Rarity);
			if (!modifier.HasPlayerRuneConfigDisabledIdsSnapshot)
			{
				modifier.SetPlayerRuneConfigDisabledIdsSnapshot(local.DisabledPlayerRuneIds, $"local act-roll act={actIndex}");
			}

			modifier.SetRunConfigurationSnapshot(local.RunConfigSnapshot, $"local act-roll act={actIndex}");
			modifier.HostUsesBetterMultiplayerScaling = false;
			return (local.Rarity, local.MonsterHex, ResolvePlayerHexCount(local.RunConfigSnapshot, modifier, actIndex));
		}

		PlayerChoiceSynchronizer synchronizer = RequirePlayerChoiceSynchronizer(runManager);
		Player authorityPlayer = GetActRollAuthorityPlayer(runManager, runState)
			?? throw CreateProtocolFailure(
				$"act-roll act={actIndex}",
				$"No act-roll authority is available for multiplayer act={actIndex}.");
		if (!IsCurrentRun(runState) || !HextechPlayerContextHelper.IsMultiplayerConnected())
		{
			throw new OperationCanceledException(
				$"Multiplayer act-roll transaction is no longer active for act={actIndex}.");
		}

		uint choiceId = synchronizer.ReserveChoiceId(authorityPlayer);
		return gameType == NetGameType.Host
			? SendHostActRoll(modifier, actIndex, local, synchronizer, authorityPlayer, choiceId)
			: await ReceiveHostActRoll(runState, modifier, actIndex, local, synchronizer, authorityPlayer, choiceId);
	}

	/// <summary>
	/// 本机先算出的本幕结果(稀有度、首个敌方海克斯、本局配置快照)。房主据此发送;客户端以房主同步值为准,
	/// 本机结果只写进日志供比对。联机抽签走稳定哈希,不推进 RNG。
	/// </summary>
	private static LocalActRoll RollLocalActRoll(RunState runState, HextechMayhemModifier modifier, int actIndex, bool isMultiplayer)
	{
		HextechRunConfigurationSnapshot localRunConfigSnapshot = modifier.GetEffectiveRunConfigurationSnapshot();
		IReadOnlySet<string> localDisabledPlayerRuneIds = HextechRuneConfiguration.GetDisabledPlayerRuneIds();

		HextechRarityTier? savedRarity = modifier.GetRarityForAct(actIndex);
		HextechRarityTier? forcedRarity = HextechCustomRunModifierCompatibility.GetForcedRarity(runState);
		IReadOnlyList<HextechRarityTier> enabledRarities = HextechRunePoolBuilder.GetEnabledPlayerRuneRarities(runState);
		HextechRarityTier? effectiveForcedRarity = forcedRarity.HasValue && enabledRarities.Contains(forcedRarity.Value)
			? forcedRarity
			: null;
		// 禁用局仍交换房主配置，但不为不会发放的内容抽取 RNG。占位稀有度只用于
		// 既有 act-roll 协议；Core 在同步后按房主冻结值跳过所有内容生成。
		HextechRarityTier localRarity = !modifier.IsModActiveForRun ? HextechRarityTier.Silver : savedRarity
			?? effectiveForcedRarity
			?? RollActRarity(modifier, actIndex, runState, enabledRarities, deterministic: isMultiplayer);
		LogLocalActRollRarity(actIndex, localRarity, savedRarity, forcedRarity, effectiveForcedRarity, enabledRarities);

		IReadOnlyList<MonsterHexKind> previousHexes = modifier.GetActiveMonsterHexesBeforeAct(actIndex);
		int newEnemyHexCount = modifier.GetEnemyHexCountForAct(actIndex);
		MonsterHexKind? savedPrimaryMonsterHex = modifier.GetMonsterHexesForAct(actIndex)
			.Where(hex => !previousHexes.Contains(hex))
			.Cast<MonsterHexKind?>()
			.FirstOrDefault();
		MonsterHexKind? localMonsterHex = !modifier.IsModActiveForRun || newEnemyHexCount <= 0
			? null
			: savedPrimaryMonsterHex
				?? ChooseMonsterHexForAct(modifier, localRarity, runState, actIndex, previousHexes, deterministic: isMultiplayer);
		HextechLog.Info("Mayhem", $"ResolveActRoll enemy count: act={actIndex} newCount={newEnemyHexCount} previous={previousHexes.Count} primary={localMonsterHex}");
		return new LocalActRoll(localRarity, localMonsterHex, localRunConfigSnapshot, localDisabledPlayerRuneIds);
	}

	private static void LogLocalActRollRarity(
		int actIndex,
		HextechRarityTier localRarity,
		HextechRarityTier? savedRarity,
		HextechRarityTier? forcedRarity,
		HextechRarityTier? effectiveForcedRarity,
		IReadOnlyList<HextechRarityTier> enabledRarities)
	{
		if (savedRarity.HasValue)
		{
			return;
		}

		if (effectiveForcedRarity.HasValue)
		{
			HextechLog.Info("Mayhem", $"ResolveActRoll forced rarity: act={actIndex} rarity={localRarity}");
		}
		else if (forcedRarity.HasValue)
		{
			HextechLog.Info("Mayhem", $"ResolveActRoll ignored disabled forced rarity: act={actIndex} forced={forcedRarity} enabled={string.Join(",", enabledRarities)} rarity={localRarity}");
		}
		else if (enabledRarities.Count < Enum.GetValues<HextechRarityTier>().Length)
		{
			HextechLog.Info("Mayhem", $"ResolveActRoll rarity pool filtered by player rune config: act={actIndex} enabled={string.Join(",", enabledRarities)} rarity={localRarity}");
		}
	}

	private static (HextechRarityTier Rarity, MonsterHexKind? MonsterHex, int PlayerHexCount) SendHostActRoll(
		HextechMayhemModifier modifier,
		int actIndex,
		LocalActRoll local,
		PlayerChoiceSynchronizer synchronizer,
		Player authorityPlayer,
		uint choiceId)
	{
		try
		{
			bool hostUsesExternalScaling = HextechMultiplayerScalingCompat.IsBetterMultiplayerScalingLoaded();
			modifier.HostUsesBetterMultiplayerScaling = hostUsesExternalScaling;
			if (!modifier.HasPlayerRuneConfigDisabledIdsSnapshot)
			{
				// 房主冻结点：本机补丁失败的海克斯并进本局禁用列表，下发的就是这份列表。
				string reason = $"host act-roll act={actIndex}";
				modifier.SetPlayerRuneConfigDisabledIdsSnapshot(
					HextechRuntimeRuneCompatibility.MergeHookFailedIntoHostDisabledIds(local.DisabledPlayerRuneIds, reason),
					reason);
			}

			HextechRunConfigurationSnapshot hostSnapshot = modifier.GetEffectiveRunConfigurationSnapshot();
			uint sentChoiceId = SyncLocalHextechChoice(
				synchronizer,
				authorityPlayer,
				choiceId,
				HextechChoiceCodec.CreateActRoll(
					actIndex,
					local.Rarity,
					local.MonsterHex,
					hostUsesExternalScaling,
					modifier.EnemyHexCountsByAct,
					modifier.PlayerRuneConfigDisabledIds,
					hostSnapshot),
				$"act-roll act={actIndex}");

			modifier.SetRarityForAct(actIndex, local.Rarity);
			HextechLog.Info("Mayhem", $"ResolveActRoll host sync: act={actIndex} choiceId={sentChoiceId} authority={authorityPlayer.NetId} rarity={local.Rarity} monsterHex={local.MonsterHex} playerCounts={string.Join(",", hostSnapshot.PlayerHexCountsByAct)} enemyCounts={string.Join(",", modifier.EnemyHexCountsByAct)} playerConfigDisabled={modifier.PlayerRuneConfigDisabledIds.Count} enemyConfigDisabled={hostSnapshot.DisabledMonsterHexIds.Count} forgeConfigDisabled={hostSnapshot.DisabledForgeIds.Count} betterMultiplayerScaling={hostUsesExternalScaling}");
			return (local.Rarity, local.MonsterHex, ResolvePlayerHexCount(hostSnapshot, modifier, actIndex));
		}
		catch (HextechChoiceProtocolException)
		{
			throw;
		}
		catch (Exception ex)
		{
			string message =
				$"Host act-roll transaction failed after reserving choice: " +
				$"act={actIndex} player={authorityPlayer.NetId} choiceId={choiceId}";
			throw CreateProtocolFailure($"act-roll act={actIndex}", message, ex);
		}
	}

	private static async Task<(HextechRarityTier Rarity, MonsterHexKind? MonsterHex, int PlayerHexCount)> ReceiveHostActRoll(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		LocalActRoll local,
		PlayerChoiceSynchronizer synchronizer,
		Player authorityPlayer,
		uint choiceId)
	{
		// 解码结果在 isExpected 回调里捕获:等待只会在它返回 true 时结束,之后不再二次解码。
		HextechRarityTier syncedRarity = default;
		MonsterHexKind? syncedMonsterHex = null;
		bool syncedHostUsesExternalScaling = false;
		int[] syncedEnemyHexCountsByAct = [];
		HashSet<string> syncedDisabledPlayerRuneIds = [];
		HextechRunConfigurationSnapshot syncedRunConfigSnapshot = null!;
		(_, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			runState,
			authorityPlayer,
			choiceId,
			result => HextechChoiceCodec.TryDecodeActRoll(
				result,
				actIndex,
				out syncedRarity,
				out syncedMonsterHex,
				out syncedHostUsesExternalScaling,
				out syncedEnemyHexCountsByAct,
				out syncedDisabledPlayerRuneIds,
				out syncedRunConfigSnapshot),
			$"act-roll act={actIndex}");
		modifier.SetRarityForAct(actIndex, syncedRarity);
		modifier.SetEnemyHexCountsByActSnapshot(syncedEnemyHexCountsByAct, $"host act-roll act={actIndex}");
		modifier.SetPlayerRuneConfigDisabledIdsSnapshot(syncedDisabledPlayerRuneIds, $"host act-roll act={actIndex}");
		modifier.SetRunConfigurationSnapshot(syncedRunConfigSnapshot with
		{
			EnemyHexCountsByAct = syncedEnemyHexCountsByAct,
			DisabledPlayerRuneIds = syncedDisabledPlayerRuneIds
		}, $"host act-roll act={actIndex}");
		modifier.HostUsesBetterMultiplayerScaling = syncedHostUsesExternalScaling;
		HextechRuntimeRuneCompatibility.WarnHookFailedRunesEnabledByHost(syncedDisabledPlayerRuneIds, $"client act-roll act={actIndex}");
		HextechLog.Info("Mayhem", $"ResolveActRoll client sync: act={actIndex} choiceId={receivedChoiceId} authority={authorityPlayer.NetId} rarity={syncedRarity} monsterHex={syncedMonsterHex} playerCounts={string.Join(",", modifier.PlayerHexCountsByAct)} enemyCounts={string.Join(",", modifier.EnemyHexCountsByAct)} playerConfigDisabled={modifier.PlayerRuneConfigDisabledIds.Count} enemyConfigDisabled={modifier.DisabledMonsterHexIdsForPool.Count} forgeConfigDisabled={modifier.DisabledForgeIdsForPool.Count} betterMultiplayerScaling={syncedHostUsesExternalScaling} localRarity={local.Rarity} localMonsterHex={local.MonsterHex}");
		return (syncedRarity, syncedMonsterHex, ResolvePlayerHexCount(syncedRunConfigSnapshot, modifier, actIndex));
	}

	private static int ResolvePlayerHexCount(
		HextechRunConfigurationSnapshot snapshot,
		HextechMayhemModifier modifier,
		int actIndex)
	{
		return HextechHexCountState.GetForAct(
			HextechRuneConfiguration.NormalizePlayerHexCounts(snapshot.PlayerHexCountsByAct),
			actIndex,
			modifier.IsEndlessLoopActive);
	}

	private static Player? GetActRollAuthorityPlayer(RunManager runManager, RunState runState)
	{
		if (runManager.NetService.Type == NetGameType.Host)
		{
			return runState.Players.FirstOrDefault(player => player.NetId == runManager.NetService.NetId);
		}

		return runState.Players.FirstOrDefault();
	}

	private static HextechRarityTier RollWeightedRarity(
		RunState runState,
		HextechRarityWeights configuredWeights,
		bool deterministic,
		int actIndex,
		IReadOnlyList<HextechRarityTier> enabledRarities)
	{
		HextechRarityWeights weights = HextechRarityRollResolver.ApplyEnabledRarities(configuredWeights, enabledRarities);
		if (weights.Total <= 0)
		{
			return RollUniformRarity(runState, deterministic, actIndex, enabledRarities);
		}

		int roll = deterministic
			? HextechStableRandom.Index(runState, weights.Total, "act-roll-weighted-rarity", actIndex.ToString(), weights.Silver.ToString(), weights.Gold.ToString(), weights.Prismatic.ToString())
			: runState.Rng.Niche.NextInt(weights.Total);
		return HextechRarityRollResolver.ResolveWeighted(weights, roll);
	}

	private static HextechRarityTier RollUniformRarity(RunState runState, bool deterministic, int actIndex, IReadOnlyList<HextechRarityTier> enabledRarities)
	{
		HextechRarityTier[] orderedRarities = HextechRarityRollResolver.GetUniformRarityOrder(enabledRarities);

		if (HextechRarityRollResolver.HasAllRarities(orderedRarities))
		{
			int roll = deterministic
				? HextechStableRandom.Index(runState, 3, "act-roll-rarity", actIndex.ToString())
				: runState.Rng.Niche.NextInt(3);
			return orderedRarities[roll];
		}

		int index = deterministic
			? HextechStableRandom.Index(
				runState,
				orderedRarities.Length,
				"act-roll-rarity",
				actIndex.ToString(),
				"enabled",
				string.Join(",", orderedRarities.Select(static rarity => ((int)rarity).ToString()).OrderBy(static value => value, StringComparer.Ordinal)))
			: runState.Rng.Niche.NextInt(orderedRarities.Length);
		return orderedRarities[index];
	}

	/// <summary>
	/// 从本幕敌方海克斯池抽一个。deterministic=true(联机)用稳定哈希(以 ordinal 区分同幕多次抽取),
	/// false(单机)推进原版 Niche 随机流;池为空时两条路径都不消耗随机数。
	/// </summary>
	private static MonsterHexKind? ChooseMonsterHexForAct(
		HextechMayhemModifier modifier,
		HextechRarityTier rarity,
		RunState runState,
		int actIndex,
		IEnumerable<MonsterHexKind>? extraExcludedHexes,
		bool deterministic,
		int ordinal = 0)
	{
		IReadOnlyList<MonsterHexKind> pool = HextechMonsterHexRoller.BuildActPool(rarity, modifier.GetKnownMonsterHexes(), extraExcludedHexes, modifier.DisabledMonsterHexIdsForPool);
		if (pool.Count == 0)
		{
			return null;
		}

		int index = deterministic
			? HextechStableRandom.Index(
				runState,
				pool.Count,
				"act-roll-monster-hex",
				actIndex.ToString(),
				ordinal.ToString(),
				((int)rarity).ToString(),
				string.Join(",", pool.Select(static kind => ((int)kind).ToString()).OrderBy(static key => key, StringComparer.Ordinal)))
			: runState.Rng.Niche.NextInt(pool.Count);
		return pool[index];
	}

	private static IReadOnlyList<MonsterHexKind> ResolveNewMonsterHexesForAct(
		HextechMayhemModifier modifier,
		HextechRarityTier rarity,
		RunState runState,
		int actIndex,
		MonsterHexKind? primaryMonsterHex)
	{
		int newEnemyHexCount = modifier.GetEnemyHexCountForAct(actIndex);
		IReadOnlyList<MonsterHexKind> previousHexes = modifier.GetActiveMonsterHexesBeforeAct(actIndex);
		MonsterHexKind[] checkpointedNewHexes = modifier.GetMonsterHexesForAct(actIndex)
			.Where(hex => !previousHexes.Contains(hex))
			.Distinct()
			.ToArray();
		if (checkpointedNewHexes.Length == newEnemyHexCount)
		{
			HextechLog.Info(
				"Mayhem", $"ResolveNewMonsterHexesForAct: " +
				$"restored checkpoint act={actIndex} newHexes={string.Join(",", checkpointedNewHexes)}");
			return checkpointedNewHexes;
		}

		bool isMultiplayer = !HextechPlayerContextHelper.IsSinglePlayerFlow(RunManager.Instance.NetService.Type);
		IReadOnlyList<MonsterHexKind> resolvedNewHexes = HextechMonsterHexRoller.ResolveNewMonsterHexes(
			newEnemyHexCount,
			previousHexes,
			primaryMonsterHex,
			(excludedHexes, ordinal) => ChooseMonsterHexForAct(modifier, rarity, runState, actIndex, excludedHexes, deterministic: isMultiplayer, ordinal));

		HextechLog.Info("Mayhem", $"ResolveNewMonsterHexesForAct: act={actIndex} newCount={newEnemyHexCount} previous={previousHexes.Count} primary={primaryMonsterHex} newHexes={string.Join(",", resolvedNewHexes)}");
		return resolvedNewHexes;
	}

	private static MonsterHexKind? RerollEnemyHexForAct(
		HextechMayhemModifier modifier,
		HextechRarityTier rarity,
		RunState runState,
		int actIndex,
		MonsterHexKind? currentHex,
		int rerollOrdinal,
		IReadOnlySet<ModelId> excludedIconRelicIds,
		HashSet<MonsterHexKind> seenEnemyHexes)
	{
		if (currentHex.HasValue)
		{
			seenEnemyHexes.Add(currentHex.Value);
		}

		IReadOnlyList<MonsterHexKind> pool = HextechMonsterHexRoller.BuildRerollPool(
			rarity,
			seenEnemyHexes,
			currentHex,
			excludedIconRelicIds,
			GetMonsterHexIconRelicId,
			modifier.DisabledMonsterHexIdsForPool);
		if (pool.Count == 0)
		{
			return currentHex;
		}

		string poolKey = string.Join(",", pool.Select(static kind => ((int)kind).ToString()).OrderBy(static key => key, StringComparer.Ordinal));
		int index = HextechStableRandom.Index(
			runState,
			pool.Count,
			"enemy-hex-reroll",
			actIndex.ToString(),
			((int)rarity).ToString(),
			(currentHex.HasValue ? ((int)currentHex.Value).ToString() : "none"),
			rerollOrdinal.ToString(),
			poolKey);
		MonsterHexKind rerolled = pool[index];
		seenEnemyHexes.Add(rerolled);
		return rerolled;
	}

	private static ModelId GetMonsterHexIconRelicId(MonsterHexKind hex)
	{
		RelicModel relic = MonsterHexCatalog.GetIconRelicForMonsterHex(hex);
		return relic.CanonicalId();
	}
}
