namespace HextechRunes;

internal static partial class HextechRuneConfiguration
{
	// v4~v14 的历史迁移段与配套数组已删除:v15(0.8.4)强制重置使 ConfigVersion<15 一律整体回默认,
	// 那些分支永不可达。活跃链从 v16 起。
	/// <summary>
	/// 我方海克斯默认开关迁移:ConfigVersion 低于 <see cref="PlayerRuneDefaultMigration.BelowVersion"/> 的配置
	/// 依次把 Enable 移出禁用集、把 Disable 加入禁用集。按版本顺序逐项执行(同一符文可能先启用后禁用,
	/// 如回力OK镖 v17 启用、v27 禁用),每项只迁移一次,之后尊重玩家的手动选择。
	/// </summary>
	private readonly record struct PlayerRuneDefaultMigration(int BelowVersion, Type[] Enable, Type[] Disable);

	private static readonly PlayerRuneDefaultMigration[] PlayerRuneDefaultMigrations =
	[
		// 腐化树枝生成分布加权(攻击40/技能20/能力40)后无限风险可控,转为默认启用。
		new(16, [ typeof(CorruptedBranchRune) ], []),
		// 感受燃烧/回力OK镖重做为"获得时给卡"(0.8.4 数据驱动重做),转为默认启用。
		new(17, [ typeof(FeelTheBurnRune), typeof(OkBoomerangRune) ], []),
		// 星界躯体改为百分比生命加成(50%)后强度自洽,转为默认启用。
		new(18, [ typeof(AstralBodyRune) ], []),
		// 设计审查批次:咔咔!(代价先付收益小)转为默认禁用。同批的和平主义者已于 0.9.6 整体移除。
		new(19, [], [ typeof(KakaRune) ]),
		// 小猪存钱罐(鼓励挨打赚钱与防御方向相悖)转为默认禁用。
		new(20, [], [ typeof(PiggyBankRune) ]),
		// 升级打击/防御(围绕不该保留的牌做增强,遥测垫底)与验牌(每回合选牌拖慢节奏)转为默认禁用。
		new(21, [], [ typeof(StrikeUpgradeRune), typeof(DefendUpgradeRune), typeof(CardInspectionRune) ]),
		// 罪恶快感(开局+击杀双重资源滚雪球)转为默认禁用。
		new(22, [], [ typeof(GetExcitedRune) ]),
		// 0.8.5 遥测(69.8万局)选取率垫底批次转为默认禁用:豪猪7.7%/巨像的勇气10.6%/瓦库11.4%/
		// 死亡收割11.5%/最终形态12.8%(全体中位数30.3%)。
		new(23, [], [ typeof(ShoulderVakuRune), typeof(PorcupineRune), typeof(CourageOfColossusRune), typeof(DeathHarvestRune), typeof(FinalFormRune) ]),
		// 升级:打击/防御重做为"最高+999且战后升级本场打出过的"(棱彩),转为默认启用。
		new(24, [ typeof(StrikeUpgradeRune), typeof(DefendUpgradeRune) ], []),
		// 安东尼的偏见转为默认启用(0.8.6)。
		new(25, [ typeof(AnthonyBiasRune) ], []),
		// 高风险或流程偏慢的通用海克斯转为默认禁用;豪猪已在 v23 禁用,不重复覆盖玩家后续选择。
		new(27, [], [ typeof(OmegaRune), typeof(OkBoomerangRune), typeof(FeyMagicRune), typeof(AstralBodyRune) ]),
		// 以进为退转为默认启用。
		new(30, [ typeof(AdvanceToRetreatRune) ], []),
		// 歪打正着重做为回合开始时按消耗牌堆状态牌生成充能球，转为默认启用。
		new(31, [ typeof(HappyAccidentRune) ], []),
		new(35, [], [ typeof(AutoPatrolRune) ]),
		// 只禁用我方；敌方"无本万利"不受影响。
		new(37, [], [ typeof(SomethingForNothingRune), typeof(SoulCallingRune) ]),
		new(39, [], [ typeof(GhostFormRune), typeof(DieForYouRune) ]),
		new(40, [], [ typeof(SearingAttackRune), typeof(ScapegoatRune), typeof(TwilightVeilRune) ])
	];

	/// <summary>敌方海克斯默认禁用迁移:只迁移一次,之后尊重玩家手动开启。</summary>
	private static readonly (int BelowVersion, MonsterHexKind Kind)[] MonsterHexDefaultDisableMigrations =
	[
		// 我方已在 v22 默认禁用。
		(36, MonsterHexKind.GetExcited),
		// 我方"你肩上的瓦库"早已默认禁用。
		(38, MonsterHexKind.ShoulderVaku)
	];

	private static RuneConfig NormalizeLoadedConfig(RuneConfig config)
	{
		// 0.8.4 一次性强制回默认:旧配置(含用户自定义)整体丢弃,不走增量迁移链。
		if (config.ConfigVersion < ForceResetBelowConfigVersion)
		{
			HextechLog.Info("RuneConfig", $"Config version {config.ConfigVersion} < {ForceResetBelowConfigVersion}; forcing full reset to defaults (0.8.4).");
			return CreateDefaultConfig();
		}

		int previousConfigVersion = config.ConfigVersion;
		HashSet<string> disabledIds = NormalizeDisabledPlayerRuneIds(config.DisabledPlayerRuneIds);
		HashSet<string> disabledMonsterHexIds = NormalizeDisabledMonsterHexIds(config.DisabledMonsterHexIds);
		ApplyPlayerRuneDefaultMigrations(previousConfigVersion, disabledIds);
		ApplyMonsterHexDefaultMigrations(previousConfigVersion, disabledMonsterHexIds);
		MigrateLegacyScalarSettings(config, previousConfigVersion);

		config.ConfigVersion = CurrentConfigVersion;
		config.DisabledPlayerRuneIds = disabledIds;
		config.DisabledMonsterHexIds = disabledMonsterHexIds;
		ClampLoadedConfigValues(config);
		return config;
	}

	private static void ApplyPlayerRuneDefaultMigrations(int previousConfigVersion, HashSet<string> disabledIds)
	{
		foreach (PlayerRuneDefaultMigration migration in PlayerRuneDefaultMigrations)
		{
			if (previousConfigVersion >= migration.BelowVersion)
			{
				continue;
			}

			if (migration.Enable.Length > 0)
			{
				disabledIds.ExceptWith(GetPlayerRuneIds(migration.Enable));
			}

			if (migration.Disable.Length > 0)
			{
				disabledIds.UnionWith(GetPlayerRuneIds(migration.Disable));
			}
		}
	}

	private static void ApplyMonsterHexDefaultMigrations(int previousConfigVersion, HashSet<string> disabledMonsterHexIds)
	{
		foreach ((int belowVersion, MonsterHexKind kind) in MonsterHexDefaultDisableMigrations)
		{
			if (previousConfigVersion < belowVersion)
			{
				disabledMonsterHexIds.Add(kind.ToString());
			}
		}
	}

	private static void MigrateLegacyScalarSettings(RuneConfig config, int previousConfigVersion)
	{
		if (previousConfigVersion < 28)
		{
			config.RuneRarityWeights = config.NormalRuneRarityWeights;
			config.PreventConsecutiveSilverRunes = DefaultPreventConsecutiveSilverRunes;
		}

		if (previousConfigVersion < 29)
		{
			config.GoldenRerollChancePercent = DefaultGoldenRerollChancePercent;
		}

		if (previousConfigVersion < 32)
		{
			HextechRarityWeights legacyWeights = ToRarityWeights(config.RuneRarityWeights, DefaultRuneRarityWeights);
			config.RuneRarityWeightsByAct = FromRarityWeightsByAct([ legacyWeights, legacyWeights, legacyWeights ]);
		}

		if (previousConfigVersion < 33 && config.MonsterHexRerollLimit == InfiniteRerollLimit)
		{
			config.MonsterHexRerollLimit = DefaultMonsterHexRerollLimit;
		}
	}

	private static void ClampLoadedConfigValues(RuneConfig config)
	{
		config.PlayerHexCountsByAct = NormalizePlayerHexCounts(config.PlayerHexCountsByAct);
		config.EnemyHexCountsByAct = NormalizeEnemyHexCounts(config.EnemyHexCountsByAct);
		config.PlayerRuneRerollLimit = ClampRerollLimit(config.PlayerRuneRerollLimit);
		config.MonsterHexRerollLimit = ClampRerollLimit(config.MonsterHexRerollLimit);
		config.DisabledForgeIds = NormalizeDisabledForgeIds(config.DisabledForgeIds);
		config.RuneRarityWeightsByAct = FromRarityWeightsByAct(NormalizeRarityWeightsByAct(
			ToRarityWeightsByAct(config.RuneRarityWeightsByAct, DefaultRuneRarityWeightsByAct),
			DefaultRuneRarityWeightsByAct));
		config.RuneRarityWeights = null;
		config.GoldenRerollChancePercent = ClampGoldenRerollChancePercent(config.GoldenRerollChancePercent);
		config.ChaosRuneChancePercent = ClampChaosRuneChancePercent(config.ChaosRuneChancePercent);
		config.NormalRuneRarityWeights = null;
		config.ForgeRarityWeights = FromRarityWeights(NormalizeRarityWeights(
			ToRarityWeights(config.ForgeRarityWeights, DefaultForgeRarityWeights),
			DefaultForgeRarityWeights));
		config.RandomForgeShopPrice = ClampRandomForgeShopPrice(config.RandomForgeShopPrice);
	}

	// 测试钩子:用真实迁移链跑一份合成配置,返回迁移后的版本号与禁用集(仅 HextechRunes.Tests 使用)。
	internal static (int ConfigVersion, IReadOnlySet<string> DisabledPlayerRuneIds) MigrateDisabledIdsForTests(int configVersion, IEnumerable<string> disabledIds)
	{
		RuneConfig config = new()
		{
			ConfigVersion = configVersion,
			DisabledPlayerRuneIds = disabledIds.ToHashSet(StringComparer.Ordinal)
		};
		RuneConfig normalized = NormalizeLoadedConfig(config);
		return (normalized.ConfigVersion, normalized.DisabledPlayerRuneIds);
	}

	internal static (int ConfigVersion, IReadOnlySet<string> DisabledMonsterHexIds) MigrateDisabledMonsterHexIdsForTests(
		int configVersion,
		IEnumerable<string> disabledIds)
	{
		RuneConfig config = new()
		{
			ConfigVersion = configVersion,
			DisabledMonsterHexIds = disabledIds.ToHashSet(StringComparer.Ordinal)
		};
		RuneConfig normalized = NormalizeLoadedConfig(config);
		return (normalized.ConfigVersion, normalized.DisabledMonsterHexIds);
	}

	internal static (int ConfigVersion, HextechRarityWeights RuneRarityWeights, bool PreventConsecutiveSilverRunes) MigrateRarityConfigForTests(
		int configVersion,
		HextechRarityWeights normalWeights)
	{
		RuneConfig config = new()
		{
			ConfigVersion = configVersion,
			NormalRuneRarityWeights = FromRarityWeights(normalWeights)
		};
		RuneConfig normalized = NormalizeLoadedConfig(config);
		return (
			normalized.ConfigVersion,
			ToRarityWeightsByAct(normalized.RuneRarityWeightsByAct, DefaultRuneRarityWeightsByAct)[0],
			normalized.PreventConsecutiveSilverRunes);
	}

	internal static HextechRarityWeights[] MigrateSingleRarityConfigForTests(
		int configVersion,
		HextechRarityWeights weights)
	{
		RuneConfig config = new()
		{
			ConfigVersion = configVersion,
			RuneRarityWeights = FromRarityWeights(weights)
		};
		RuneConfig normalized = NormalizeLoadedConfig(config);
		return ToRarityWeightsByAct(normalized.RuneRarityWeightsByAct, DefaultRuneRarityWeightsByAct);
	}

	internal static (int ConfigVersion, int MonsterHexRerollLimit) MigrateMonsterHexRerollLimitForTests(
		int configVersion,
		int rerollLimit)
	{
		RuneConfig config = new()
		{
			ConfigVersion = configVersion,
			MonsterHexRerollLimit = rerollLimit
		};
		RuneConfig normalized = NormalizeLoadedConfig(config);
		return (normalized.ConfigVersion, normalized.MonsterHexRerollLimit);
	}

	private static HashSet<string> GetPlayerRuneIds(IEnumerable<Type> runeTypes)
	{
		return HextechPlayerRuneConfigIds.FromTypes(runeTypes);
	}
}
