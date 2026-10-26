using Godot;
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

	private static readonly Lazy<IReadOnlyDictionary<ModelId, MonsterHexKind>> MonsterHexByIconRelicId = new(BuildMonsterHexByIconRelicId);

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
		return MonsterHexByIconRelicId.Value.TryGetValue(id, out hex);
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
		return EnemyHexPowerHoverTipTypes.TryGetValue(hex, out Type[]? powerTypes)
			? powerTypes
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
	/// 它只读字段、不会抛异常，所以这里不再包裸 catch。
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

	private static IReadOnlyDictionary<ModelId, MonsterHexKind> BuildMonsterHexByIconRelicId()
	{
		Dictionary<ModelId, MonsterHexKind> byId = new();
		foreach (KeyValuePair<MonsterHexKind, Type> pair in HextechContentRegistry.MonsterHexMetadata.IconRelicTypes)
		{
			RelicModel iconRelic = ModelDb.GetById<RelicModel>(ModelDb.GetId(pair.Value));
			ModelId id = iconRelic.CanonicalId();
			byId[id] = pair.Key;
		}

		return byId;
	}
}
