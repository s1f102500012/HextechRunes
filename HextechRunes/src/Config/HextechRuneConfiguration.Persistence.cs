using System.Text.Json.Serialization;

namespace HextechRunes;

internal static partial class HextechRuneConfiguration
{
	private const string LogTag = "RuneConfig";

	private static void EnsureLoaded()
	{
		lock (SyncRoot)
		{
			if (_loaded)
			{
				return;
			}

			RuneConfig config = LoadOrCreateConfig();
			// 载入链只按字段迁移/夹值;默认配置与迁移追加的敌方禁用项还没排序和过滤。
			// 这里统一过一遍 NormalizeSnapshot(与保存同口径),之后 GetSnapshot 只需复制。只改内存,不回写文件。
			StoreNormalizedSnapshot(config, NormalizeSnapshot(ReadSnapshot(config)));
			_config = config;
			_loaded = true;
		}
	}

	private static RuneConfig LoadOrCreateConfig()
	{
		return JsonConfigFile.Load(ConfigFileName, LogTag, CreateDefaultConfig, static parsed =>
		{
			int parsedVersion = parsed.ConfigVersion;
			RuneConfig config = NormalizeLoadedConfig(parsed);
			if (parsedVersion > CurrentConfigVersion)
			{
				// 更新版本写的配置退回本版本读取:类型化模型不保留未知字段,回写会把版本号压回并丢掉未来字段。
				// 只在内存里使用规范化结果,不覆盖文件;用户在本版本改设置时才会重写。
				HextechLog.Warn(LogTag, $"Config version {parsedVersion} is newer than supported {CurrentConfigVersion}; using it in memory without rewriting the file.");
				return (config, false);
			}

			return (config, true);
		});
	}

	private static RuneConfig CreateDefaultConfig()
	{
		return new RuneConfig
		{
			ConfigVersion = CurrentConfigVersion,
			DisabledPlayerRuneIds = GetDefaultDisabledPlayerRuneIds().ToHashSet(StringComparer.Ordinal),
			PlayerHexCountsByAct = NormalizePlayerHexCounts(null),
			EnemyHexCountsByAct = NormalizeEnemyHexCounts(null),
			PlayerRuneRerollLimit = DefaultPlayerRuneRerollLimit,
			MonsterHexRerollLimit = DefaultMonsterHexRerollLimit,
			DisabledMonsterHexIds = GetDefaultDisabledMonsterHexIds().ToHashSet(StringComparer.Ordinal),
			DisabledForgeIds = GetDefaultDisabledForgeIds().ToHashSet(StringComparer.Ordinal),
			RuneRarityWeightsByAct = FromRarityWeightsByAct(DefaultRuneRarityWeightsByAct),
			PreventConsecutiveSilverRunes = DefaultPreventConsecutiveSilverRunes,
			GoldenRerollChancePercent = DefaultGoldenRerollChancePercent,
			ForgeRarityWeights = FromRarityWeights(DefaultForgeRarityWeights),
			RandomForgeShopPrice = DefaultRandomForgeShopPrice,
			RandomForgeDirectGrant = DefaultRandomForgeDirectGrant,
			ModEnabled = DefaultModEnabled
		};
	}

	private static HextechRarityWeights ToRarityWeights(RarityWeightConfig? config, HextechRarityWeights fallback)
	{
		return config == null
			? fallback
			: new HextechRarityWeights(config.Silver, config.Gold, config.Prismatic);
	}

	private static HextechRarityWeights[] ToRarityWeightsByAct(
		IReadOnlyList<RarityWeightConfig>? configs,
		IReadOnlyList<HextechRarityWeights> fallback)
	{
		return Enumerable.Range(0, HexActCount)
			.Select(actIndex => configs != null && actIndex < configs.Count
				? ToRarityWeights(configs[actIndex], fallback[Math.Min(actIndex, fallback.Count - 1)])
				: fallback[Math.Min(actIndex, fallback.Count - 1)])
			.ToArray();
	}

	private static RarityWeightConfig FromRarityWeights(HextechRarityWeights weights)
	{
		return new RarityWeightConfig
		{
			Silver = weights.Silver,
			Gold = weights.Gold,
			Prismatic = weights.Prismatic
		};
	}

	private static RarityWeightConfig[] FromRarityWeightsByAct(IEnumerable<HextechRarityWeights> weightsByAct)
	{
		return weightsByAct.Select(FromRarityWeights).ToArray();
	}

	private static void SaveConfig(RuneConfig config)
	{
		JsonConfigFile.Save(ConfigFileName, LogTag, config);
	}

	private sealed class RuneConfig
	{
		[JsonPropertyName("config_version")]
		public int ConfigVersion { get; set; }

		[JsonPropertyName("disabled_player_rune_ids")]
		public HashSet<string> DisabledPlayerRuneIds { get; set; } = new(StringComparer.Ordinal);

		[JsonPropertyName("player_hex_counts_by_act")]
		public int[]? PlayerHexCountsByAct { get; set; }

		[JsonPropertyName("enemy_hex_counts_by_act")]
		public int[]? EnemyHexCountsByAct { get; set; }

		[JsonPropertyName("player_rune_reroll_limit")]
		public int PlayerRuneRerollLimit { get; set; } = DefaultPlayerRuneRerollLimit;

		[JsonPropertyName("monster_hex_reroll_limit")]
		public int MonsterHexRerollLimit { get; set; } = DefaultMonsterHexRerollLimit;

		[JsonPropertyName("disabled_monster_hex_ids")]
		public HashSet<string> DisabledMonsterHexIds { get; set; } = new(StringComparer.Ordinal);

		[JsonPropertyName("disabled_forge_ids")]
		public HashSet<string> DisabledForgeIds { get; set; } = new(StringComparer.Ordinal);

		[JsonPropertyName("rune_rarity_weights")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public RarityWeightConfig? RuneRarityWeights { get; set; }

		[JsonPropertyName("rune_rarity_weights_by_act")]
		public RarityWeightConfig[]? RuneRarityWeightsByAct { get; set; }

		[JsonPropertyName("prevent_consecutive_silver_runes")]
		public bool PreventConsecutiveSilverRunes { get; set; } = DefaultPreventConsecutiveSilverRunes;

		[JsonPropertyName("golden_reroll_chance_percent")]
		public int GoldenRerollChancePercent { get; set; } = DefaultGoldenRerollChancePercent;

		// 刻意保持 PascalCase 键名(其余字段是 snake_case):该字段首次发布时没有 JsonPropertyName,
		// 已发布的 rune_config.json 里存的就是 "ChaosRuneChancePercent"。改成 snake_case 会让降级到旧版本的
		// 玩家读不到该值;显式写出特性是为了把现有键名钉住,不受日后命名策略变更影响。
		[JsonPropertyName("ChaosRuneChancePercent")]
		public int ChaosRuneChancePercent { get; set; } = DefaultChaosRuneChancePercent;

		// 只在 v<28 迁移时读取;旧文件里同期的 first_act_/second_act_after_silver_ 权重从不读取,按未知字段跳过。
		[JsonPropertyName("normal_rune_rarity_weights")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public RarityWeightConfig? NormalRuneRarityWeights { get; set; }

		[JsonPropertyName("forge_rarity_weights")]
		public RarityWeightConfig? ForgeRarityWeights { get; set; }

		[JsonPropertyName("random_forge_shop_price")]
		public int RandomForgeShopPrice { get; set; } = DefaultRandomForgeShopPrice;

		[JsonPropertyName("random_forge_direct_grant")]
		public bool RandomForgeDirectGrant { get; set; } = DefaultRandomForgeDirectGrant;

		[JsonPropertyName("mod_enabled")]
		public bool ModEnabled { get; set; } = DefaultModEnabled;

		// 弑神试炼开关：不进配置快照与分享码，只由口令开启、弑神后自动关闭；为 false 时不写进文件。
		[JsonPropertyName("godslayer_trial")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool GodslayerTrial { get; set; }
	}

	private sealed class RarityWeightConfig
	{
		[JsonPropertyName("silver")]
		public int Silver { get; set; }

		[JsonPropertyName("gold")]
		public int Gold { get; set; }

		[JsonPropertyName("prismatic")]
		public int Prismatic { get; set; }
	}
}
