namespace HextechRunes;

internal static class HextechForgeRegistry
{
	internal static IReadOnlyList<ForgeRegistration> Registrations => ForgeRegistrations;

	private static readonly IReadOnlyList<ForgeRegistration> ForgeRegistrations =
	[
		Forge<StrengthForge>(HextechRarityTier.Silver),
		Forge<DexterityForge>(HextechRarityTier.Silver),
		Forge<UpgradeForge>(HextechRarityTier.Silver),
		Forge<LifeForge>(HextechRarityTier.Silver),
		Forge<SilverHpForge>(HextechRarityTier.Silver),
		Forge<SilverAttackForge>(HextechRarityTier.Silver),
		Forge<SilverProtectionForge>(HextechRarityTier.Silver),
		Forge<PocketForge>(HextechRarityTier.Silver),
		Forge<PreparedForge>(HextechRarityTier.Silver),
		Forge<SwiftForge>(HextechRarityTier.Silver),
		Forge<FireworksForge>(HextechRarityTier.Silver),
		Forge<VigorForge>(HextechRarityTier.Silver),
		Forge<BlockForge>(HextechRarityTier.Silver),
		Forge<NecrobinderForge>(HextechRarityTier.Silver),
		Forge<SilverStarsForge>(HextechRarityTier.Silver),
		Forge<SilverOrbForge>(HextechRarityTier.Silver),
		Forge<ForgingForge>(HextechRarityTier.Silver),
		Forge<ScissorsForge>(HextechRarityTier.Silver),
		Forge<SteadyForge>(HextechRarityTier.Silver),
		Forge<SilverRecoveryForge>(HextechRarityTier.Silver),
		Forge<AimForge>(HextechRarityTier.Silver),
		Forge<StretchForge>(HextechRarityTier.Silver),
		Forge<JuggernautForge>(HextechRarityTier.Silver),

		Forge<ConstitutionForge>(HextechRarityTier.Gold),
		Forge<DisasterForge>(HextechRarityTier.Gold),
		Forge<GoldLifeForge>(HextechRarityTier.Gold),
		Forge<GoldHpForge>(HextechRarityTier.Gold),
		Forge<GoldAttackForge>(HextechRarityTier.Gold),
		Forge<GoldProtectionForge>(HextechRarityTier.Gold),
		Forge<GoldFocusForge>(HextechRarityTier.Gold),
		Forge<DrawForge>(HextechRarityTier.Gold),
		Forge<RecoveryForge>(HextechRarityTier.Gold),
		Forge<HourglassForge>(HextechRarityTier.Gold),
		Forge<GoldUpgradeForge>(HextechRarityTier.Gold),
		Forge<GlamForge>(HextechRarityTier.Gold),
		Forge<SoulsPowerForge>(HextechRarityTier.Gold),
		Forge<MomentumForge>(HextechRarityTier.Gold),
		Forge<SummonForge>(HextechRarityTier.Gold),
		Forge<FleshForge>(HextechRarityTier.Gold),
		Forge<EmbersForge>(HextechRarityTier.Gold),
		Forge<StarsForge>(HextechRarityTier.Gold),
		Forge<OrbSlotForge>(HextechRarityTier.Gold),
		Forge<PlatingForge>(HextechRarityTier.Gold),
		Forge<ThornsForge>(HextechRarityTier.Gold),
		Forge<ArtifactForge>(HextechRarityTier.Gold),
		Forge<VenomForge>(HextechRarityTier.Gold),
		Forge<ShrinkForge>(HextechRarityTier.Gold),
		Forge<GoldScissorsForge>(HextechRarityTier.Gold),
		Forge<NoxiousFumesForge>(HextechRarityTier.Gold),
		Forge<ArrowForge>(HextechRarityTier.Gold),
		Forge<DarkEmbraceForge>(HextechRarityTier.Gold),

		Forge<PrismaticLifeForge>(HextechRarityTier.Prismatic),
		Forge<AttackForge>(HextechRarityTier.Prismatic),
		Forge<ProtectionForge>(HextechRarityTier.Prismatic),
		Forge<EnergyForge>(HextechRarityTier.Prismatic),
		Forge<RitualForge>(HextechRarityTier.Prismatic),
		Forge<RegenForge>(HextechRarityTier.Prismatic),
		Forge<BufferForge>(HextechRarityTier.Prismatic),
		Forge<SlipperyForge>(HextechRarityTier.Prismatic),
		Forge<PrismaticArtifactForge>(HextechRarityTier.Prismatic),
		Forge<FocusForge>(HextechRarityTier.Prismatic),
		Forge<FortuneForge>(HextechRarityTier.Prismatic),
		Forge<SpiralForge>(HextechRarityTier.Prismatic),
		Forge<VoidForge>(HextechRarityTier.Prismatic),
		Forge<EchoForge>(HextechRarityTier.Prismatic)
	];

	// 愚者专属锻造器：没有愚者的游戏版本上不在界面列出（见 HextechCatalog.IsForgeTypeShownInUi），发放由各自的 IsAvailableForPlayer 拦下。
	internal static IReadOnlySet<Type> FoolOnlyForgeTypes { get; } = new HashSet<Type>
	{
		typeof(AimForge),
		typeof(StretchForge),
		typeof(ArrowForge)
	};

	private static ForgeRegistration Forge<TForge>(HextechRarityTier rarity)
	{
		return new ForgeRegistration(typeof(TForge), rarity);
	}
}
