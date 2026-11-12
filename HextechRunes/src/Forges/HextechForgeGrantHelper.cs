using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

internal static class HextechForgeGrantHelper
{
	private const int ForgeChoiceOptionCount = 3;

	// firstOrdinal 让中断后的补发从已完成的序号接着走：序号与 player.Relics.Count 都是候选盐值，
	// 背包没有其他变化时，接续第 k 个与一次发完时的第 k 个候选相同。afterEachForge 在每一步（含被配置禁用而拦下的一步）
	// 结束后调用，供调用方把进度写进存档。afterObtained 在每个锻造器获得后调用（被配置拦下时也会调用，由回调自己的获得流程再拦一次）。
	internal static async Task<bool> TryObtainRandomForges(
		Player player,
		int count,
		int firstOrdinal = 0,
		Action? afterEachForge = null,
		Func<RelicModel, Task>? afterObtained = null)
	{
		for (int i = firstOrdinal; i < firstOrdinal + count; i++)
		{
			if (!TryCreateStableRandomForgeChoice(player, "obtain-random-forges", i, out List<RelicModel> options))
			{
				return false;
			}

			RelicModel? selected = await HextechForgeSelectionCoordinator.SelectForge(player, options, $"obtain-random-forges:{i}");
			if (selected == null)
			{
				return false;
			}

			await ObtainSelectedForge(player, selected, syncObtainedRelic: false);
			if (afterObtained != null)
			{
				await afterObtained(selected);
			}

			afterEachForge?.Invoke();
		}

		return true;
	}

	public static async Task ObtainRandomForges(
		Player player,
		HextechRarityTier rarity,
		int count,
		Func<Type, bool> forgeTypePredicate,
		string source)
	{
		for (int i = 0; i < count; i++)
		{
			if (!TryCreateStableRandomForgeChoice(player, rarity, source, i, forgeTypePredicate, out List<RelicModel> options))
			{
				return;
			}

			RelicModel? selected = await HextechForgeSelectionCoordinator.SelectForge(player, options, $"{source}:{i}");
			if (selected == null)
			{
				return;
			}

			await ObtainSelectedForge(player, selected, syncObtainedRelic: false);
		}
	}

	public static bool AddRandomForgeReward(Player player, CombatRoom room)
	{
		if (!TryCreateStableRandomForgeChoice(player, "combat-random-forge-reward", 0, out List<RelicModel> options))
		{
			return false;
		}

		room.AddExtraReward(player, new HextechForgeChoiceReward(options, player));
		return true;
	}

	public static bool AddWeightedRandomForgeReward(
		Player player,
		CombatRoom room,
		string source,
		int silverWeight,
		int goldWeight,
		int prismaticWeight)
	{
		if (!TryCreateStableRandomForgeChoice(player, source, 0, silverWeight, goldWeight, prismaticWeight, out List<RelicModel> options))
		{
			return false;
		}

		room.AddExtraReward(player, new HextechForgeChoiceReward(options, player));
		return true;
	}

	public static bool AddRandomForgeReward(Player player, CombatRoom room, HextechRarityTier rarity)
	{
		if (!TryCreateStableRandomForgeChoice(player, rarity, "combat-random-forge-reward-rarity", 0, out List<RelicModel> options))
		{
			return false;
		}

		room.AddExtraReward(player, new HextechForgeChoiceReward(options, player));
		return true;
	}

	public static async Task ObtainSelectedForge(Player player, RelicModel forge, bool syncObtainedRelic)
	{
		// 主机权威复核(联机安全网):决不发放被配置禁用的属性锻造器。
		// 候选池在战斗结束 / 进店那一刻就按当时的有效配置快照(modifier.DisabledForgeIdsForPool)构建,而联机下
		// 客机要到幕开局 ActRoll 把主机配置同步过来之前,快照都是默认的「空禁用」——这段时间窗里被禁锻造器会混进
		// 候选并被稳定随机选中。真正「落地获得」(领奖 / 购买完成)通常晚于建池,此刻主机配置多半已同步到位,故在
		// 这唯一落地点再用最新的有效禁用集兜底校验一次,挡掉任何漏网的被禁锻造器。
		if (IsForgeDisabledForPlayer(player, forge))
		{
			HextechLog.Warn("ForgeChoice", $"Blocked obtaining a config-disabled forge: player={player.NetId} relic={forge.CanonicalId().Entry}");
			return;
		}

		SaveManager.Instance.MarkRelicAsSeen(forge);
		bool syncedBeforePickup = false;
		if (syncObtainedRelic)
		{
			if (HextechPlayerContextHelper.IsMultiplayerConnected())
			{
				// Enchantment forges open a nested deck choice during pickup; remote clients must know about the forge first.
				ModelId forgeId = forge.CanonicalId();
				RelicModel syncCopy = ModelDb.GetById<RelicModel>(forgeId).ToMutable();
				RunManager.Instance.RewardSynchronizer.SyncLocalObtainedRelic(syncCopy);
				syncedBeforePickup = true;
			}
			else if (HextechPlayerContextHelper.IsNetworkMultiplayerRun())
			{
				HextechLog.Warn("ForgeChoice", $"Skipped forge reward sync because multiplayer service is disconnected: relic={forge.Id.Entry}");
			}
		}

		await RelicCmd.Obtain(forge, player);
		if (syncedBeforePickup)
		{
			HextechLog.Info("ForgeChoice", $"Synced obtained forge before pickup effect: player={player.NetId} relic={forge.Id.Entry}");
		}
	}

	internal static bool TryCreateStableShopForgeChoice(Player player, int purchaseOrdinal, out List<RelicModel> options)
	{
		return TryCreateStableRandomForgeChoice(player, "shop-random-forge", purchaseOrdinal, out options);
	}

	private static bool TryCreateStableRandomForgeChoice(Player player, string source, int ordinal, out List<RelicModel> options)
	{
		HextechRarityTier rarity = RollStableForgeRarity(player, source, ordinal);
		return TryCreateStableRandomForgeChoice(player, rarity, source, ordinal, out options);
	}

	private static bool TryCreateStableRandomForgeChoice(
		Player player,
		string source,
		int ordinal,
		int silverWeight,
		int goldWeight,
		int prismaticWeight,
		out List<RelicModel> options)
	{
		HextechRarityTier rarity = RollStableForgeRarity(player, source, ordinal, silverWeight, goldWeight, prismaticWeight);
		return TryCreateStableRandomForgeChoice(player, rarity, source, ordinal, out options);
	}

	// 公开 API 可能传入未定义的稀有度值：按空池处理，不抛异常。
	private static IReadOnlyList<Type> GetForgeTypesForRarity(HextechRarityTier rarity)
	{
		return HextechContentRegistry.ForgeTypesByRarity.GetValueOrDefault(rarity, Array.Empty<Type>());
	}

	private static bool TryCreateStableRandomForgeChoice(Player player, HextechRarityTier rarity, string source, int ordinal, out List<RelicModel> options)
	{
		List<Type> pool = BuildAvailableForgePool(player, GetForgeTypesForRarity(rarity));
		if (pool.Count == 0)
		{
			pool = BuildAvailableForgePool(player, HextechCatalog.GetAllForgeTypes());
		}

		return TryPickForgeChoiceOptions(player, pool, rarity, source, "forge-choice", ordinal, out options);
	}

	private static bool TryCreateStableRandomForgeChoice(
		Player player,
		HextechRarityTier rarity,
		string source,
		int ordinal,
		Func<Type, bool> forgeTypePredicate,
		out List<RelicModel> options)
	{
		List<Type> pool = BuildAvailableForgePool(player, GetForgeTypesForRarity(rarity).Where(forgeTypePredicate));
		return TryPickForgeChoiceOptions(player, pool, rarity, source, "filtered-forge-choice", ordinal, out options);
	}

	// choiceTag 是稳定随机的盐值之一，两种入口各自保留原值，不能合并（否则同样输入抽出的候选会变）。
	private static bool TryPickForgeChoiceOptions(
		Player player,
		List<Type> pool,
		HextechRarityTier rarity,
		string source,
		string choiceTag,
		int ordinal,
		out List<RelicModel> options)
	{
		if (pool.Count == 0)
		{
			options = [];
			return false;
		}

		List<Type> forgeTypes = HextechStableRandom.PickDistinct(
			pool,
			Math.Min(ForgeChoiceOptionCount, pool.Count),
			(RunState)player.RunState,
			HextechStableRandom.TypeModelKey,
			source,
			choiceTag,
			HextechStableRandom.PlayerKey(player),
			ordinal.ToString(),
			((int)rarity).ToString(),
			player.Relics.Count.ToString());
		options = forgeTypes
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable())
			.ToList();
		return options.Count > 0;
	}

	private static List<Type> BuildAvailableForgePool(Player player, IEnumerable<Type> candidateTypes)
	{
		IReadOnlySet<string> disabledForgeIds = GetEffectiveDisabledForgeIds(player);
		return candidateTypes
			.Where(type => !disabledForgeIds.Contains(ModelDb.GetId(type).Entry))
			.Where(type => HextechCatalog.IsAvailableForPlayer(ModelDb.GetById<RelicModel>(ModelDb.GetId(type)), player))
			.ToList();
	}

	internal static HextechForgeRarityWeights ApplyDiceManiacForgeRarityModifier(HextechForgeRarityWeights weights, bool hasDiceManiac)
	{
		weights = NormalizeForgeRarityWeights(weights);
		if (!hasDiceManiac)
		{
			return weights;
		}

		return weights with
		{
			Gold = weights.Gold * DiceManiacRune.ForgeRarityMultiplier,
			Prismatic = weights.Prismatic * DiceManiacRune.ForgeRarityMultiplier
		};
	}

	private static HextechRarityTier RollStableForgeRarity(Player player, string source, int ordinal)
	{
		HextechForgeRarityWeights baseWeights = GetBaseForgeRarityWeights(player);
		return RollStableForgeRarity(player, source, ordinal, baseWeights.Silver, baseWeights.Gold, baseWeights.Prismatic);
	}

	private static HextechRarityTier RollStableForgeRarity(
		Player player,
		string source,
		int ordinal,
		int silverWeight,
		int goldWeight,
		int prismaticWeight)
	{
		HextechForgeRarityWeights weights = GetModifiedForgeRarityWeights(player, silverWeight, goldWeight, prismaticWeight);
		if (weights.Total <= 0)
		{
			return HextechRarityTier.Silver;
		}

		int roll = HextechStableRandom.Index(
			(RunState)player.RunState,
			weights.Total,
			source,
			"forge-rarity",
			HextechStableRandom.PlayerKey(player),
			ordinal.ToString(),
			player.Relics.Count.ToString(),
			weights.Silver.ToString(),
			weights.Gold.ToString(),
			weights.Prismatic.ToString());
		return ResolveForgeRarity(weights, roll);
	}

	private static HextechForgeRarityWeights GetModifiedForgeRarityWeights(
		Player player,
		int silverWeight,
		int goldWeight,
		int prismaticWeight)
	{
		HextechForgeRarityWeights weights = new(silverWeight, goldWeight, prismaticWeight);
		return ApplyDiceManiacForgeRarityModifier(weights, player.GetRelic<DiceManiacRune>() != null);
	}

	private static HextechForgeRarityWeights GetBaseForgeRarityWeights(Player player)
	{
		// 有本局 modifier 时只读同步快照;读取失败不能回落本地配置,客机的本地值会与主机分叉。
		if (player.RunState is RunState runState
			&& HextechMayhemModifier.FindIn(runState) is HextechMayhemModifier modifier)
		{
			return modifier.ForgeRarityWeights;
		}

		return GetForgeConfigurationWithoutRun().ForgeRarityWeights;
	}

	internal static bool IsForgeDisabledForPlayer(Player player, RelicModel forge)
	{
		string entry = forge.CanonicalId().Entry;
		return GetEffectiveDisabledForgeIds(player).Contains(entry);
	}

	private static IReadOnlySet<string> GetEffectiveDisabledForgeIds(Player player)
	{
		if (player.RunState is RunState runState
			&& HextechMayhemModifier.FindIn(runState) is HextechMayhemModifier modifier)
		{
			return modifier.DisabledForgeIdsForPool;
		}

		return GetForgeConfigurationWithoutRun().DisabledForgeIds;
	}

	// 找不到本局 Modifier 时锻造器配置（品质权重、禁用表、随机锻造器售价）的来源：单机读本机菜单配置；
	// 联机读默认配置，各端本地菜单值不能决定共享结果（与 HextechMayhemModifier.IsEnabledForRun 同口径）。
	internal static HextechRunConfigurationSnapshot GetForgeConfigurationWithoutRun()
	{
		return HextechPlayerContextHelper.IsNetworkMultiplayerRun()
			? HextechRuneConfiguration.GetDefaultSnapshot()
			: HextechRuneConfiguration.GetSnapshot();
	}

	private static HextechForgeRarityWeights NormalizeForgeRarityWeights(HextechForgeRarityWeights weights)
	{
		return new HextechForgeRarityWeights(
			Math.Max(0, weights.Silver),
			Math.Max(0, weights.Gold),
			Math.Max(0, weights.Prismatic));
	}

	private static HextechRarityTier ResolveForgeRarity(HextechForgeRarityWeights weights, int roll)
	{
		if (roll < weights.Silver)
		{
			return HextechRarityTier.Silver;
		}

		if (roll < weights.Silver + weights.Gold)
		{
			return HextechRarityTier.Gold;
		}

		return HextechRarityTier.Prismatic;
	}
}
