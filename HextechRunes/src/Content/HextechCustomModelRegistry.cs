namespace HextechRunes;

internal static class HextechCustomModelRegistry
{
	// 只由欧洛巴斯的再次强化获得；不进入共享随机池或海克斯候选池。
	internal static IReadOnlyList<Type> EventRelicTypes { get; } =
	[
		typeof(HextechBlackBloodPlus),
		typeof(HextechRingOfTheDrakePlus),
		typeof(HextechDivineDestinyPlus),
		typeof(HextechPhylacteryUnboundPlus),
		typeof(HextechInfusedCorePlus)
	];

	// 已退役的自定义稀有度 run modifier 只保留模型注册，避免旧局中的 modifier ID 无法解析。
	internal static IReadOnlyList<Type> CustomRarityModifierTypes { get; } =
	[
		typeof(HextechSilverRunModifier),
		typeof(HextechGoldRunModifier),
		typeof(HextechPrismaticRunModifier)
	];

	// 已退役的预设挑战同样只保留模型注册（见 HextechPresetChallenges.cs）。
	internal static IReadOnlyList<Type> CustomChallengeModifierTypes { get; } =
	[
		typeof(StuffedToRuinChallengeModifier),
		typeof(DefenseCounterMasterChallengeModifier),
		typeof(BruteForceChallengeModifier),
		typeof(EightPennyGateChallengeModifier),
		typeof(ListlessChallengeModifier)
	];

	internal static IReadOnlyList<Type> AllCustomModifierTypes { get; } =
		CustomRarityModifierTypes
			.Concat(CustomChallengeModifierTypes)
			.ToArray();

	internal static IReadOnlyList<Type> ShopOnlyRelicTypes { get; } =
	[
		typeof(RandomForgeShopRelic)
	];

	// 敌方专属海克斯的图标/文案载体 relic：注册进 ModelDb 但不进任何玩家获取池。
	// 注意：新增敌方专属 relic 时这里必须同步登记，否则图标路径不被认定（游戏内显示 NOPE）。
	// 不从 HextechMonsterHexRegistry 派生：本列表顺序就是模型进 SharedRelicPool 的登记顺序，
	// 派生会改变既有顺序。集合一致性由测试 ReviewEnemyHexIconRelicTypesMatchMonsterHexRegistry 守住。
	internal static IReadOnlyList<Type> EnemyHexIconRelicTypes { get; } =
	[
		typeof(SkulkingColonyHex),
		typeof(PhantasmalGardenerHex),
		typeof(QueenHex),
		typeof(LagavulinMatriarchHex),
		typeof(ExoskeletonHex),
		typeof(TestSubjectHex),
		typeof(LeafSlimeHex),
		typeof(ShrinkerBeetleHex),
		typeof(InkletHex),
		typeof(PhrogParasiteHex),
		typeof(VantomHex),
		typeof(AeonglassHex),
		typeof(TheLostHex),
		typeof(TheForgottenHex),
		typeof(SlimedBerserkerHex),
		typeof(GlobeHeadHex),
		typeof(MyteHex),
		typeof(FossilStalkerHex),
		typeof(TungstenRodHex),
		typeof(HundredRefinementsHex),
		typeof(AncientStatueHex),
		typeof(ByrdonisHex),
		typeof(HungryHex),
		typeof(InspectHex),
		typeof(GripHex),
		typeof(LivingFogHex),
		typeof(CeremonialBeastHex),
		typeof(SoulFyshHex),
		typeof(ThievingHopperHex),
		typeof(HauntedShipHex),
		typeof(InfestedPrismHex),
		typeof(CorruptHeartHex),
		typeof(EnlightenmentHex),
		typeof(EntomancerHex),
		typeof(TransmuteSilverHex),
		typeof(TransmuteGoldHex),
		typeof(SpinToWinHex),
		typeof(ChomperHex),
		typeof(VineShamblerHex),
		typeof(SpinyToadHex),
		typeof(ScrollOfBitingHex),
		typeof(SpectralKnightHex),
		typeof(OwlMagistrateHex)
	];

	internal static IReadOnlyList<Type> CustomCardTypes { get; } =
	[
		typeof(TrickMagicCard),
		typeof(BladeWaltzCard),
		typeof(CatalystCard),
		typeof(WhiteHoleCard),
		typeof(SearingAttackCard),
		typeof(FeelTheBurnCard),
		typeof(OkBoomerangCard),
		typeof(ReprogramCard),
		typeof(MikaelsBlessingCard),
		typeof(OstyWishCard),
		typeof(OceanDragonSoulCard),
		typeof(InfernalDragonSoulCard),
		typeof(HextechDragonSoulCard),
		typeof(MountainDragonSoulCard),
		typeof(ChemtechDragonSoulCard),
		typeof(CloudDragonSoulCard),
		typeof(QuantumComputingCard)
	];
}
