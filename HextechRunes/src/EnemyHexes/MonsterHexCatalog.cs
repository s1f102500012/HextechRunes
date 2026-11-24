using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;

namespace HextechRunes;

internal static class MonsterHexCatalog
{
	private static readonly IReadOnlyDictionary<MonsterHexKind, Type[]> EnemyHexPowerHoverTipTypes =
		new Dictionary<MonsterHexKind, Type[]>
		{
			[MonsterHexKind.Slap] = [typeof(StrengthPower)],
			[MonsterHexKind.SpinToWin] = [typeof(DrawCardsNextTurnPower), typeof(EnergyNextTurnPower)],
			[MonsterHexKind.VineShambler] = [typeof(TangledPower)],
			[MonsterHexKind.SpinyToad] = [typeof(ThornsPower)],
			[MonsterHexKind.ScrollOfBiting] = [typeof(PaperCutsPower)],
			[MonsterHexKind.SpectralKnight] = [typeof(HextechHexPower)],
			[MonsterHexKind.OwlMagistrate] = [typeof(HextechSoarPower)],
			[MonsterHexKind.Tormentor] = [typeof(HextechBurnPower)],
			[MonsterHexKind.Firebrand] = [typeof(HextechBurnPower)],
			[MonsterHexKind.Corrosion] = [typeof(FrailPower)],
			[MonsterHexKind.EscapePlan] = [typeof(ShrinkPower)],
			[MonsterHexKind.ProtectiveVeil] = [typeof(ArtifactPower)],
			[MonsterHexKind.Repulsor] = [typeof(SlipperyPower)],
			[MonsterHexKind.Thornmail] = [typeof(ThornsPower)],
			[MonsterHexKind.FrostWraith] = [typeof(HextechPlayerSlowPower)],
			[MonsterHexKind.DawnbringersResolve] = [typeof(RegenPower)],
			[MonsterHexKind.ShrinkRay] = [typeof(ShrinkPower)],
			[MonsterHexKind.SuperBrain] = [typeof(PlatingPower)],
			[MonsterHexKind.Nightstalking] = [typeof(SlipperyPower)],
			[MonsterHexKind.ShrinkEngine] = [typeof(SlipperyPower)],
			[MonsterHexKind.ServantMaster] = [typeof(IllusionPower)],
			[MonsterHexKind.DivineIntervention] = [typeof(IntangiblePower)],
			[MonsterHexKind.CourageOfColossus] = [typeof(PlatingPower)],
			[MonsterHexKind.HandOfBaron] = [typeof(ShrinkPower)],
			[MonsterHexKind.CantTouchThis] = [typeof(BufferPower)],
			[MonsterHexKind.MasterOfDuality] = [typeof(StrengthPower), typeof(DexterityPower)],
			[MonsterHexKind.FeyMagic] = [typeof(ShrinkPower), typeof(NoDrawPower)],
			[MonsterHexKind.UnmovableMountain] = [typeof(BarricadePower)],
			[MonsterHexKind.BloodPact] = [typeof(StrengthPower)],
			[MonsterHexKind.BrutalForce] = [typeof(StrengthPower)],
			[MonsterHexKind.Zealot] = [typeof(VigorPower)],
			[MonsterHexKind.BloodArmor] = [typeof(PlatingPower)],
			[MonsterHexKind.Doomsday] = [typeof(DisintegrationPower)],
			[MonsterHexKind.WarmogsSpirit] = [typeof(PlatingPower)],
			[MonsterHexKind.ClownCollege] = [typeof(SlipperyPower)],
			[MonsterHexKind.HailToTheKing] = [typeof(ArtifactPower), typeof(PlatingPower), typeof(RegenPower)],
			[MonsterHexKind.NearDeathFeast] = [typeof(StrengthPower)],
			[MonsterHexKind.SerpentsFang] = [typeof(PoisonPower)],
			[MonsterHexKind.Porcupine] = [typeof(ThornsPower)],
			[MonsterHexKind.MonarchsGaze] = [typeof(StrengthPower)],
			[MonsterHexKind.SwiftAndSafe] = [typeof(ArtifactPower)],
			[MonsterHexKind.ArcanePunch] = [typeof(HextechVitalSparkPower), typeof(TaintedPower)],
			[MonsterHexKind.Omega] = [typeof(DisintegrationPower)],
			[MonsterHexKind.OminousPact] = [typeof(DoomPower)],
			[MonsterHexKind.Cerberus] = [typeof(VigorPower)],
			[MonsterHexKind.OmniDragonSoul] = [typeof(WeakPower), typeof(FrailPower), typeof(VulnerablePower)],
			[MonsterHexKind.SkulkingColony] = [typeof(HardenedShellPower)],
			[MonsterHexKind.PhantasmalGardener] = [typeof(SkittishPower)],
			[MonsterHexKind.Queen] = [typeof(ChainsOfBindingPower)],
			[MonsterHexKind.LagavulinMatriarch] = [typeof(StrengthPower), typeof(DexterityPower)],
			[MonsterHexKind.Exoskeleton] = [typeof(HardToKillPower)],
			[MonsterHexKind.TestSubject] = [typeof(EnragePower), typeof(PainfulStabsPower)],
			[MonsterHexKind.ShrinkerBeetle] = [typeof(ShrinkPower)],
			[MonsterHexKind.Inklet] = [typeof(SlipperyPower)],
			[MonsterHexKind.Vantom] = [typeof(SlipperyPower)],
			[MonsterHexKind.TheLost] = [typeof(StrengthPower)],
			[MonsterHexKind.TheForgotten] = [typeof(DexterityPower)],
			[MonsterHexKind.Byrdonis] = [typeof(TerritorialPower)],
			[MonsterHexKind.CeremonialBeast] = [typeof(StrengthPower)],
			[MonsterHexKind.ThievingHopper] = [typeof(SwipePower)],
			[MonsterHexKind.ReforgedHelmet] = [typeof(StrengthPower)],
			[MonsterHexKind.Compensation] = [typeof(HextechNextTurnDamagePower)],
			[MonsterHexKind.SolidTime] = [typeof(HextechGalvanicPower)],
			[MonsterHexKind.FossilStalker] = [typeof(SuckPower)],
			[MonsterHexKind.AncientStatue] = [typeof(HextechPlayerSlowPower)],
			[MonsterHexKind.HundredRefinements] = [typeof(HextechPlayerSlowPower)],
			[MonsterHexKind.MadScientist] = [typeof(PersonalHivePower)],
		};

	// 能力之外的补充悬浮提示（卡牌、关键词），排在能力提示之后。
	private static readonly IReadOnlyDictionary<MonsterHexKind, Func<IHoverTip>[]> EnemyHexExtraHoverTips =
		new Dictionary<MonsterHexKind, Func<IHoverTip>[]>
		{
			[MonsterHexKind.SoulFysh] = [static () => HoverTipFactory.FromCard<Beckon>()],
			[MonsterHexKind.HauntedShip] = [static () => HoverTipFactory.FromCard<Dazed>()],
			[MonsterHexKind.SomethingForNothing] = [static () => HoverTipFactory.FromKeyword(CardKeyword.Exhaust)],
			[MonsterHexKind.CorruptedBranch] = [static () => HoverTipFactory.FromKeyword(CardKeyword.Exhaust)],
		};

	private static readonly object MonsterHexByIconRelicIdLock = new();
	private static MonsterHexMetadataCatalog? _monsterHexByIconRelicIdCatalog;
	private static IReadOnlyDictionary<ModelId, MonsterHexKind>? _monsterHexByIconRelicId;

	// 只在单人局出现的敌方海克斯；联机局抽取、重掷与质变都不会给出。
	private static readonly HashSet<MonsterHexKind> SinglePlayerOnlyMonsterHexes = [MonsterHexKind.TestSubject];

	public static bool IsSinglePlayerOnly(MonsterHexKind hex)
	{
		return SinglePlayerOnlyMonsterHexes.Contains(hex);
	}

	public static IReadOnlyList<MonsterHexKind> GetMonsterHexesForRarity(HextechRarityTier rarity)
	{
		return HextechContentRegistry.MonsterHexMetadata.EnabledKindsByRarity.GetValueOrDefault(rarity, Array.Empty<MonsterHexKind>());
	}

	public static HextechRarityTier GetMonsterHexRarity(MonsterHexKind hex)
	{
		if (HextechContentRegistry.MonsterHexMetadata.RarityByKind.TryGetValue(hex, out HextechRarityTier rarity))
		{
			return rarity;
		}

		throw new ArgumentOutOfRangeException(nameof(hex), hex, "Unknown monster hex rarity.");
	}

	public static RelicModel GetIconRelicForMonsterHex(MonsterHexKind hex)
	{
		if (!HextechContentRegistry.MonsterHexMetadata.IconRelicTypes.TryGetValue(hex, out Type? relicType))
		{
			throw new ArgumentOutOfRangeException(nameof(hex), hex, "Unknown monster hex icon relic.");
		}

		return ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType));
	}

	public static bool TryGetMonsterHexKind(RelicModel relic, out MonsterHexKind hex)
	{
		ModelId id = relic.CanonicalId();
		return GetMonsterHexByIconRelicId().TryGetValue(id, out hex);
	}

	/// <summary>存档、联机载荷里的整数是否是本机注册表里的敌方海克斯（内置或外部登记）；不认识的数值由调用方丢弃。</summary>
	public static bool TryGetRegisteredMonsterHex(int rawHex, out MonsterHexKind hex)
	{
		hex = (MonsterHexKind)rawHex;
		return HextechContentRegistry.MonsterHexMetadata.AllKinds.Contains(hex);
	}

	/// <summary>
	/// 敌方海克斯的配置 ID：内置是枚举名，外部登记的是展示遗物的 ModelId.Entry。禁用列表、分享码、控制台、遥测与界面节点名都用它，
	/// 与 <see cref="TryGetMonsterHexByConfigId"/> 成对使用，不要直接写 <c>kind.ToString()</c> / <c>Enum.TryParse</c>。
	/// </summary>
	public static string GetConfigId(MonsterHexKind hex)
	{
		return HextechContentRegistry.MonsterHexMetadata.ConfigIdByKind.TryGetValue(hex, out string? configId)
			? configId
			: hex.ToString();
	}

	public static bool TryGetMonsterHexByConfigId(string? configId, out MonsterHexKind hex)
	{
		hex = default;
		return configId != null
			&& HextechContentRegistry.MonsterHexMetadata.KindByConfigId.TryGetValue(configId, out hex);
	}

	/// <summary>
	/// 本局能否抽到该敌方海克斯。内置条目恒为 true；外部条目的展示遗物稀有度必须是 Starter（与外部我方符文同一约定），
	/// 并经过登记时附带的过滤委托。委托抛异常时排除该项并限额告警；委托仍须只依赖同步状态，各端才会得到同一个池。
	/// <paramref name="runState"/> 为空（离线预览、测试）时不调用委托。
	/// </summary>
	public static bool IsAvailableInRun(MonsterHexKind hex, IRunState? runState)
	{
		if (!HextechContentRegistry.MonsterHexMetadata.ExternalByKind.TryGetValue(hex, out ResolvedExternalEnemyHex? external))
		{
			return true;
		}

		Type displayRelicType = external.Registration.DisplayRelicType;
		RelicModel displayRelic = ModelDb.GetById<RelicModel>(HextechExternalContentRegistry.ModelIdResolver(displayRelicType));
		if (displayRelic.Rarity != RelicRarity.Starter)
		{
			if (HextechRunLogBudget.TryConsume("external-content.non-starter-enemy-hex", 12))
			{
				HextechLog.Warn("ExternalContent", $"External enemy hex display relic {displayRelicType.FullName} has rarity {displayRelic.Rarity}; only Starter display relics are drawn.");
			}

			return false;
		}

		Func<IRunState, bool>? availability = HextechExternalContentRegistry.GetEnemyHexAvailability(displayRelicType);
		if (availability == null || runState == null)
		{
			return true;
		}

		try
		{
			return availability(runState);
		}
		catch (Exception ex)
		{
			if (HextechRunLogBudget.TryConsume("external-content.enemy-hex-availability-failed", 12))
			{
				HextechLog.Warn("ExternalContent", $"Availability predicate for enemy hex {displayRelicType.FullName} threw and the hex was excluded: {ex.GetType().Name}: {ex.Message}");
			}

			return false;
		}
	}

	/// <summary>
	/// 联机时会按玩家数（×N）放大的敌方 hex 层数：仅 mod 强制 ×玩家数 的 Slippery/Artifact。
	/// 每项是描述里的变量占位名 + 单人基数；联机时填 base×玩家数、单人填 base，让选择/hover 描述显示实际层数。
	/// 注意：SwiftAndSafe 的人工制品走裸 PowerCmd.Apply、不被缩放，故不在此列。
	/// </summary>
	private static readonly IReadOnlyDictionary<MonsterHexKind, (string Var, int Base)[]> PlayerCountScaledStacks =
		new Dictionary<MonsterHexKind, (string, int)[]>
		{
			[MonsterHexKind.Repulsor] = new[] { ("Stacks1", 1), ("Stacks2", 2), ("Stacks3", 3) },
			[MonsterHexKind.ClownCollege] = new[] { ("Stacks", 1) },
			[MonsterHexKind.CantTouchThis] = new[] { ("Stacks", 1) },
			[MonsterHexKind.ShrinkEngine] = new[] { ("Stacks1", 0), ("Stacks2", 1), ("Stacks3", 2) },
			[MonsterHexKind.ProtectiveVeil] = new[] { ("Stacks1", 1), ("Stacks2", 2), ("Stacks3", 3) },
			[MonsterHexKind.HailToTheKing] = new[] { ("Stacks", 3) },
			[MonsterHexKind.Inklet] = new[] { ("Stacks1", 1), ("Stacks2", 2), ("Stacks3", 3) },
		};

	internal static readonly IReadOnlyDictionary<MonsterHexKind, (string Var, int Base)> PlayerCountScaledThresholds =
		new Dictionary<MonsterHexKind, (string, int)>
		{
			// 同 Porcupine：字面量供 TXT 同步脚本渲染，测试断言与各效果类的 HpPerPercentPerPlayer 一致。
			[MonsterHexKind.HeavyHitter] = ("HpPerPercent", 15),
			[MonsterHexKind.VitalitySurge] = ("HpPerPercent", 20),
			[MonsterHexKind.MoreTheMerrier] = ("RelicsNeeded", 1),
			// 写字面量:tools/sync_content_txt.py 按字面量渲染 TXT 里的"4N";测试断言它与效果类的常数一致。
			[MonsterHexKind.Porcupine] = ("HitsNeeded", 4),
			[MonsterHexKind.HundredRefinements] = ("HitsNeeded", 1),
			[MonsterHexKind.AncientWine] = ("SkillsNeeded", 1),
			[MonsterHexKind.AnthonyBias] = ("CardsNeeded", 1),
			[MonsterHexKind.MindToMatter] = ("CardsNeeded", 1),
		};

	public static string GetEnemyHexDescriptionFormatted(MonsterHexKind hex)
	{
		RelicModel relic = GetIconRelicForMonsterHex(hex);
		string localizationKey = GetEnemyHexDescriptionKey(relic);
		try
		{
			LocString locString = new("relics", localizationKey);
			if (PlayerCountScaledStacks.TryGetValue(hex, out (string Var, int Base)[]? scaledStacks))
			{
				int playerCount = GetScalingPlayerCount();
				foreach ((string varName, int baseValue) in scaledStacks)
				{
					locString.Add(varName, baseValue * playerCount);
				}
			}
			if (PlayerCountScaledThresholds.TryGetValue(hex, out (string Var, int Base) scaledThreshold))
			{
				locString.Add(scaledThreshold.Var, scaledThreshold.Base * GetScalingPlayerCount());
			}

			return locString.GetFormattedText();
		}
		catch (Exception ex)
		{
			// 敌方描述缺键或格式化失败时退回玩家版描述，不让悬浮提示和选择界面整体失败。
			HextechLog.Warn("Mayhem", $"Enemy hex description fallback: hex={hex} key={localizationKey} error={ex.Message}");
			return relic.DynamicDescription.GetFormattedText();
		}
	}

	public static IEnumerable<IHoverTip> GetEnemyHexHoverTips(MonsterHexKind hex)
	{
		RelicModel relic = GetIconRelicForMonsterHex(hex);
		HoverTip mainTip = new(relic.Title, GetEnemyHexDescriptionFormatted(hex), GetEnemyHexHoverIcon(relic) ?? relic.Icon);
		List<IHoverTip> tips = [mainTip];

		foreach (Type powerType in GetEnemyHexPowerHoverTipTypes(hex))
		{
			PowerModel power = ModelDb.GetById<PowerModel>(ModelDb.GetId(powerType));
			tips.Add(HoverTipFactory.FromPower(power));
		}

		if (EnemyHexExtraHoverTips.TryGetValue(hex, out Func<IHoverTip>[]? extraTips))
		{
			foreach (Func<IHoverTip> createTip in extraTips)
			{
				tips.Add(createTip());
			}
		}

		return tips;
	}

	internal static IReadOnlyList<Type> GetEnemyHexPowerHoverTipTypes(MonsterHexKind hex)
	{
		if (EnemyHexPowerHoverTipTypes.TryGetValue(hex, out Type[]? powerTypes))
		{
			return powerTypes;
		}

		// 外部登记的条目直接取登记的能力。
		return HextechContentRegistry.MonsterHexMetadata.ExternalByKind.TryGetValue(hex, out ResolvedExternalEnemyHex? external)
			? [ external.Registration.PowerType ]
			: Array.Empty<Type>();
	}

	private static Texture2D? GetEnemyHexHoverIcon(RelicModel relic)
	{
		string? path = HextechAssets.TryGetCustomRelicIconPath(relic);
		return path == null ? null : HextechTextures.LoadUiTexture(path);
	}

	private static string GetEnemyHexDescriptionKey(RelicModel relic)
	{
		ModelId id = relic.CanonicalId();
		return HextechAssets.ToImageFileStem(id.Entry) + ".enemyDescription";
	}

	/// <summary>
	/// 用于描述显示的玩家数：单人（或非联机/取不到状态）为 1，联机时取本局玩家数并夹到 [1,16]，
	/// 与 <c>HextechEnemyPowerScalingHooks.MultiplyByPlayerCount</c> 的实际缩放保持一致。
	/// 原版 RunManager 没有公开的当前 RunState 取法（只有 DebugOnlyGetState，本体其它入口也都用它）；
	/// 它只读字段、不会抛异常，所以这里不包裸 catch。
	/// </summary>
	private static int GetScalingPlayerCount()
	{
		if (!HextechPlayerContextHelper.IsNetworkMultiplayerRun())
		{
			return 1;
		}

		int count = RunManager.Instance.DebugOnlyGetState() is RunState runState ? runState.Players.Count : 1;
		return HextechEnemyHexContext.ClampScalingPlayerCount(count);
	}

	// 跟着注册表目录重建：外部条目晚于本体初始化登记，目录定型前的映射不能留用。
	private static IReadOnlyDictionary<ModelId, MonsterHexKind> GetMonsterHexByIconRelicId()
	{
		MonsterHexMetadataCatalog catalog = HextechContentRegistry.MonsterHexMetadata;
		lock (MonsterHexByIconRelicIdLock)
		{
			if (_monsterHexByIconRelicId == null || !ReferenceEquals(_monsterHexByIconRelicIdCatalog, catalog))
			{
				_monsterHexByIconRelicId = BuildMonsterHexByIconRelicId(catalog);
				_monsterHexByIconRelicIdCatalog = catalog;
			}

			return _monsterHexByIconRelicId;
		}
	}

	private static IReadOnlyDictionary<ModelId, MonsterHexKind> BuildMonsterHexByIconRelicId(MonsterHexMetadataCatalog catalog)
	{
		Dictionary<ModelId, MonsterHexKind> byId = new();
		foreach (KeyValuePair<MonsterHexKind, Type> pair in catalog.IconRelicTypes)
		{
			RelicModel iconRelic = ModelDb.GetById<RelicModel>(ModelDb.GetId(pair.Value));
			ModelId id = iconRelic.CanonicalId();
			byId[id] = pair.Key;
		}

		return byId;
	}
}
