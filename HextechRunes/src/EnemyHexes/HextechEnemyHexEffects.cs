using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

internal static class HextechEnemyHexEffects
{
	private static readonly IReadOnlyList<HextechEnemyHexEffect> OrderedEffects = CreateOrderedEffects(
	[
		new SlapEnemyHex(),
		new EscapePlanEnemyHex(),
		new HeavyHitterEnemyHex(),
		new BigStrengthEnemyHex(),
		new TormentorEnemyHex(),
		new ProtectiveVeilEnemyHex(),
		new RepulsorEnemyHex(),
		new ThornmailEnemyHex(),
		new LightEmUpEnemyHex(),
		new MountainSoulEnemyHex(),
		new FirstAidKitEnemyHex(),
		new SpeedDemonEnemyHex(),
		new FrostWraithEnemyHex(),
		new BloodPactEnemyHex(),
		new StartupRoutineEnemyHex(),
		new DizzySpinningEnemyHex(),
		new BrutalForceEnemyHex(),
		new ZealotEnemyHex(),
		new SwiftAndSafeEnemyHex(),
		new BloodIdolEnemyHex(),
		new SturdyEnemyHex(),
		new DawnbringersResolveEnemyHex(),
		new ShrinkRayEnemyHex(),
		new FirebrandEnemyHex(),
		new SuperBrainEnemyHex(),
		new NightstalkingEnemyHex(),
		new AstralBodyEnemyHex(),
		new TankEngineEnemyHex(),
		new ShrinkEngineEnemyHex(),
		new TwiceThriceEnemyHex(),
		new LoopEnemyHex(),
		new ServantMasterEnemyHex(),
		new CuttingEdgeAlchemistEnemyHex(),
		new DivineInterventionEnemyHex(),
		new SonataEnemyHex(),
		new DevilsDanceEnemyHex(),
		new SkulkingColonyEnemyHex(),
		new DoomsdayEnemyHex(),
		new WarmogsSpiritEnemyHex(),
		new BloodArmorEnemyHex(),
		new JinlianBoxEnemyHex(),
		new MirrorReflectionEnemyHex(),
		new BlueCandleMedkitEnemyHex(),
		new TanksShieldEnemyHex(),
		new PorcupineEnemyHex(),
		new MonarchsGazeEnemyHex(),
		new OmegaEnemyHex(),
		new ManipulateRealityEnemyHex(),
		new ArchmageEnemyHex(),
		new PhantasmalGardenerEnemyHex(),
		new AncientWineEnemyHex(),
		new CourageOfColossusEnemyHex(),
		new GlassCannonEnemyHex(),
		new GoliathEnemyHex(),
		new QueenEnemyHex(),
		new HandOfBaronEnemyHex(),
		new CantTouchThisEnemyHex(),
		new MasterOfDualityEnemyHex(),
		new GoldrendEnemyHex(),
		new BackToBasicsEnemyHex(),
		new MadScientistEnemyHex(),
		new FeyMagicEnemyHex(),
		new FinalFormEnemyHex(),
		new UnmovableMountainEnemyHex(),
		new MikaelsBlessingEnemyHex(),
		new ClownCollegeEnemyHex(),
		new SingularityAIEnemyHex(),
		new ProteinShakeEnemyHex(),
		new GoldenSpatulaEnemyHex(),
		new HailToTheKingEnemyHex(),
		new EightPennyGateEnemyHex(),
		new DuffsVintageEnemyHex(),
		new HastyScribbleEnemyHex(),
		new LagavulinMatriarchEnemyHex(),
		new UpgradeEnemyHex(),
		new NearDeathFeastEnemyHex(),
		new ExoskeletonEnemyHex(),
		new SerpentsFangEnemyHex(),
		new PandorasBoxEnemyHex(),
		new ForbiddenGrimoireEnemyHex(),
		new TezcatarasMercyEnemyHex(),
		new ArcanePunchEnemyHex(),
		new TestSubjectEnemyHex(),
		new MysteryEnemyHex(),
		new MindOverMatterEnemyHex(),
		new CompensationEnemyHex(),
		new OminousPactEnemyHex(),
		new SolidTimeEnemyHex(),
		new ForgottenSoulEnemyHex(),
		new CerberusEnemyHex(),
		new OmniDragonSoulEnemyHex(),
		new CorrosionEnemyHex(),
		new JudicatorEnemyHex(),
		new SoulEaterEnemyHex(),
		new DeathHarvestEnemyHex(),
		new GiantSlayerEnemyHex(),
		new DualWieldEnemyHex(),
		new JeweledGauntletEnemyHex(),
		new LeafSlimeEnemyHex(),
		new ShrinkerBeetleEnemyHex(),
		new InkletEnemyHex(),
		new PhrogParasiteEnemyHex(),
		new VantomEnemyHex(),
		new AeonglassEnemyHex(),
		new TheLostEnemyHex(),
		new TheForgottenEnemyHex(),
		new SlimedBerserkerEnemyHex(),
		new GlobeHeadEnemyHex(),
		new MyteEnemyHex(),
		new FossilStalkerEnemyHex(),
		new ByrdonisEnemyHex(),
		new TungstenRodEnemyHex(),
		new HundredRefinementsEnemyHex(),
		new VitalitySurgeEnemyHex(),
		new AncientStatueEnemyHex(),
		new IInspectEnemyHex(),
		new IGripEnemyHex(),
		new TwilightVeilEnemyHex(),
		new StatsEnemyHex(),
		new StatsOnStatsEnemyHex(),
		new StatsOnStatsOnStatsEnemyHex(),
		new MiserableFateEnemyHex(),
		new LivingFogEnemyHex(),
		new CeremonialBeastEnemyHex(),
		new SoulFyshEnemyHex(),
		new ThievingHopperEnemyHex(),
		new HauntedShipEnemyHex(),
		new ReforgedHelmetEnemyHex(),
		new EndlessRotationEnemyHex(),
		new SomethingForNothingEnemyHex(),
		new CorruptedBranchEnemyHex(),
		new MoreTheMerrierEnemyHex(),
		new EnlightenmentEnemyHex(),
		new CorruptHeartEnemyHex(),
		new BadTasteEnemyHex(),
		new TransmuteSilverEnemyHex(),
		new TransmuteGoldEnemyHex(),
		new SpinToWinEnemyHex(),
		new BloodthirstEnemyHex(),
		new AnthonyBiasEnemyHex(),
		new OverflowEnemyHex(),
		new MindToMatterEnemyHex(),
		new ChomperEnemyHex(),
		new VineShamblerEnemyHex(),
		new SpinyToadEnemyHex(),
		new ScrollOfBitingEnemyHex(),
		new SpectralKnightEnemyHex(),
		new OwlMagistrateEnemyHex()
	]);

	// 惰性枚举：每取一项都按当时的生效状态判断，语义见 HextechActiveEnemyHexCursor。
	internal static IEnumerable<HextechEnemyHexEffect> GetActive(HextechMayhemModifier modifier)
	{
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			yield return effect;
		}
	}

	internal static bool HasActiveAttackCostPreviewEffect(HextechMayhemModifier modifier)
	{
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			if (effect.AffectsPlayerAttackCostPreview)
			{
				return true;
			}
		}

		return false;
	}

	// 按注册顺序取出 active 中的效果；orderIndexes 与返回数组等长，记录各效果在注册顺序中的位置。
	internal static HextechEnemyHexEffect[] CreateActiveEffects(IReadOnlySet<MonsterHexKind> active, out int[] orderIndexes)
	{
		List<HextechEnemyHexEffect> effects = new(active.Count);
		List<int> orders = new(active.Count);
		for (int i = 0; i < OrderedEffects.Count; i++)
		{
			HextechEnemyHexEffect effect = OrderedEffects[i];
			if (active.Contains(effect.Kind))
			{
				effects.Add(effect);
				orders.Add(i);
			}
		}

		orderIndexes = orders.ToArray();
		return effects.ToArray();
	}

	internal static void ResetAllRunScopedState()
	{
		foreach (HextechEnemyHexEffect effect in OrderedEffects)
		{
			effect.ResetRunScopedState();
		}
	}

	private static IReadOnlyList<HextechEnemyHexEffect> CreateOrderedEffects(IReadOnlyList<HextechEnemyHexEffect> effects)
	{
		MonsterHexKind[] duplicateKinds = effects
			.GroupBy(static effect => effect.Kind)
			.Where(static group => group.Count() > 1)
			.Select(static group => group.Key)
			.ToArray();
		if (duplicateKinds.Length > 0)
		{
			throw new InvalidOperationException($"Duplicate enemy hex effects: {string.Join(", ", duplicateKinds)}");
		}

		IReadOnlySet<MonsterHexKind> registeredKinds = HextechContentRegistry.MonsterHexMetadata.AllKinds;
		MonsterHexKind[] missingEffects = registeredKinds
			.Except(effects.Select(static effect => effect.Kind))
			.ToArray();
		if (missingEffects.Length > 0)
		{
			throw new InvalidOperationException($"Missing enemy hex effects: {string.Join(", ", missingEffects)}");
		}

		MonsterHexKind[] unknownEffects = effects
			.Select(static effect => effect.Kind)
			.Except(registeredKinds)
			.ToArray();
		if (unknownEffects.Length > 0)
		{
			throw new InvalidOperationException($"Enemy hex effects without content registrations: {string.Join(", ", unknownEffects)}");
		}

		return effects;
	}
}

// 按注册顺序逐个取当前生效的敌方海克斯效果，与"每取一项时从上一项之后按注册顺序逐个判断 HasActiveMonsterHex"等价：
// 处理器在 await 期间增删敌方海克斯、进出战斗房间或切换阶段时，尚未经过的位置按新状态判断，已经过的位置不回头。
// 状态未变时缓存返回同一数组，直接取下一项；数组被换掉时按注册位置找回续接点。
internal struct HextechActiveEnemyHexCursor
{
	private readonly HextechMayhemModifier _modifier;
	private HextechEnemyHexEffect[]? _effects;
	private int _index;
	private int _nextOrder;

	internal HextechActiveEnemyHexCursor(HextechMayhemModifier modifier)
	{
		_modifier = modifier;
		_effects = null;
		_index = -1;
		_nextOrder = 0;
	}

	internal bool MoveNext([NotNullWhen(true)] out HextechEnemyHexEffect? effect)
	{
		HextechEnemyHexEffect[] effects = _modifier.GetActiveEnemyHexEffects(out int[] orderIndexes);
		int index;
		if (ReferenceEquals(effects, _effects))
		{
			index = _index + 1;
		}
		else
		{
			index = 0;
			while (index < orderIndexes.Length && orderIndexes[index] < _nextOrder)
			{
				index++;
			}
		}

		_effects = effects;
		if (index >= effects.Length)
		{
			_index = effects.Length;
			_nextOrder = int.MaxValue;
			effect = null;
			return false;
		}

		_index = index;
		_nextOrder = orderIndexes[index] + 1;
		effect = effects[index];
		return true;
	}
}
