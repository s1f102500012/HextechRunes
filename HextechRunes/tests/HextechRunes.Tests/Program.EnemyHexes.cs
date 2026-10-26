using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void MonsterInteractionPolicyPreservesStructuralMonsterBuffs()
	{
		PowerModel[] structuralEnemyPowers =
		[
			new AdaptablePower(),
			new AsleepPower(),
			new SlumberPower(),
			new SandpitPower(),
			new BattlewornDummyTimeLimitPower(),
			new MinionPower(),
			new InfestedPower(),
		];
		foreach (PowerModel power in structuralEnemyPowers)
		{
			string name = power.GetType().Name;
			Expect(HextechMonsterInteractionPolicy.IsStructuralMonsterBuff(power), $"{name} should be structural");
			Expect(HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(power), $"{name} should survive buff removal");
			Expect(HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(power), $"{name} should not be mirrored to players");
		}

		PowerModel[] enemyHostedPlayerRelations = [new BackAttackLeftPower(), new BackAttackRightPower()];
		foreach (PowerModel power in enemyHostedPlayerRelations)
		{
			string name = power.GetType().Name;
			Expect(!HextechMonsterInteractionPolicy.IsStructuralMonsterBuff(power), $"{name} should be classified as a player relation rather than structural");
			Expect(HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(power), $"{name} should survive enemy buff removal for Surrounded");
			Expect(HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(power), $"{name} should not be mirrored to players");
		}

		PowerModel[] removableMonsterMechanisms =
		[
			new HatchPower(),
			new ReattachPower(),
			new HardToKillPower(),
			new WitheringPresencePower(),
			new NemesisPower(),
			new IllusionPower(),
			new SteamEruptionPower(),
		];
		foreach (PowerModel power in removableMonsterMechanisms)
		{
			string name = power.GetType().Name;
			Expect(!HextechMonsterInteractionPolicy.IsStructuralMonsterBuff(power), $"{name} should be removable while its owner is alive");
			Expect(!HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(power), $"{name} should be removable by Feel the Burn and upgraded Expose");
			Expect(HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(power), $"{name} should not be mirrored to players");
		}

		PowerModel[] nonEnemyPowers =
		[
			new MonologuePower(),
			new CountdownPower(),
			new TheSealedThronePower(),
			new PillarOfCreationPower(),
			new ChildOfTheStarsPower(),
			new PaleBlueDotPower(),
			new TheHuntPower(),
			new DemesnePower(),
			new UnmovablePower(),
			new OrbitPower(),
			new GuardedPower(),
			new InterceptPower(),
			new DieForYouPower(),
			new FastenPower(),
			new HauntPower(),
			new SummonNextTurnPower(),
			new StarNextTurnPower(),
#if STS2_108_OR_NEWER
			new SoulboundPower(),
#endif
		];
		foreach (PowerModel power in nonEnemyPowers)
		{
			string name = power.GetType().Name;
			Expect(!HextechMonsterInteractionPolicy.IsStructuralMonsterBuff(power), $"{name} should not be classified as an enemy structural power");
			Expect(!HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(power), $"{name} should not be protected by enemy buff removal policy");
			Expect(!HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(power), $"{name} should not be classified as a monster mechanism");
		}

		Expect(!HextechMonsterInteractionPolicy.IsStructuralMonsterBuff(new StrengthPower()), "ordinary strength should not be structural");
		Expect(HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(new PersonalHivePower()), "personal hive should not be mirrored to players");
		Expect(HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(new HextechPlayerSlowPower()), "custom Slow should not be mirrored to players");
		Expect(!HextechMonsterInteractionPolicy.IsMonsterMechanismBuff(new StrengthPower()), "ordinary strength should remain mirrorable");
	}

	[HextechTest]
	private static void BuffRemovalPreservesStolenLootPowers()
	{
		Expect(HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(new HeistPower()), "Heist should survive Feel the Burn and upgraded Expose");
		Expect(HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(new SwipePower()), "Swipe should survive Feel the Burn and upgraded Expose");
		Expect(!HextechMonsterInteractionPolicy.ShouldPreserveFromBuffRemoval(new StrengthPower()), "ordinary Strength should remain removable");
	}

	[HextechTest]
	private static void EnemyFossilStalkerUsesExpectedSuckTiers()
	{
		Equal(1, FossilStalkerEnemyHex.ResolveSuckAmount(0), "enemy Fossil Stalker tier zero fallback Suck");
		Equal(1, FossilStalkerEnemyHex.ResolveSuckAmount(1), "enemy Fossil Stalker tier one Suck");
		Equal(2, FossilStalkerEnemyHex.ResolveSuckAmount(2), "enemy Fossil Stalker tier two Suck");
		Equal(3, FossilStalkerEnemyHex.ResolveSuckAmount(3), "enemy Fossil Stalker tier three Suck");
		Equal(3, FossilStalkerEnemyHex.ResolveSuckAmount(99), "enemy Fossil Stalker high-tier clamp Suck");
	}

	[HextechTest]
	private static void EnemyTungstenRodReducesEachHpLossByTier()
	{
		Equal(4m, TungstenRodEnemyHex.ReduceHpLoss(5m, 1), "enemy Tungsten Rod tier one HP loss");
		Equal(3m, TungstenRodEnemyHex.ReduceHpLoss(5m, 2), "enemy Tungsten Rod tier two HP loss");
		Equal(2m, TungstenRodEnemyHex.ReduceHpLoss(5m, 3), "enemy Tungsten Rod tier three HP loss");
		Equal(0m, TungstenRodEnemyHex.ReduceHpLoss(2m, 3), "enemy Tungsten Rod should floor HP loss at zero");
		Equal(0m, TungstenRodEnemyHex.ReduceHpLoss(0m, 3), "enemy Tungsten Rod should preserve zero HP loss");
	}

	[HextechTest]
	private static void EnemySlowHexesUseExpectedBaselinesAndTiers()
	{
		HextechPlayerSlowPower slow = new();
		Equal(MegaCrit.Sts2.Core.Entities.Powers.PowerType.None, slow.Type, "custom Slow should be neither a buff nor a debuff");
		HextechTemporarySlowPower temporarySlow =
			(HextechTemporarySlowPower)RuntimeHelpers.GetUninitializedObject(typeof(HextechTemporarySlowPower));
		Equal(MegaCrit.Sts2.Core.Entities.Powers.PowerType.None, temporarySlow.Type, "temporary custom Slow should be neither a buff nor a debuff");
		Expect(temporarySlow.AllowNegative, "temporary custom Slow should support damage reduction stacks");
		Expect(HextechTemporarySlowPower.ShouldExpireAtSide(CombatSide.Player, roundNumber: 3, appliedRound: 2), "temporary Slow should expire at the next player turn start");
		Expect(!HextechTemporarySlowPower.ShouldExpireAtSide(CombatSide.Player, roundNumber: 3, appliedRound: 3), "temporary Slow applied during this player turn start must survive the same turn (Frost Wraith)");
		Expect(!HextechTemporarySlowPower.ShouldExpireAtSide(CombatSide.Enemy, roundNumber: 3, appliedRound: 2), "temporary Slow should remain during enemy turn start");
		Harmony neutralTypeHarmony = new("Natsuki.HextechRunes.Tests.SlowPowerType");
		neutralTypeHarmony.Patch(
			AccessTools.Method(typeof(PowerModel), nameof(PowerModel.GetTypeForAmount), [typeof(decimal)]),
			postfix: new HarmonyMethod(FindPatchMethod(typeof(HextechCombatHooks), "PowerTypeForAmountPatch", "Postfix")));
		try
		{
			Equal(MegaCrit.Sts2.Core.Entities.Powers.PowerType.None, slow.GetTypeForAmount(8m), "positive custom Slow should remain neutral");
			Equal(MegaCrit.Sts2.Core.Entities.Powers.PowerType.None, slow.GetTypeForAmount(-8m), "negative custom Slow should remain neutral");
			Equal(MegaCrit.Sts2.Core.Entities.Powers.PowerType.None, temporarySlow.GetTypeForAmount(-8m), "negative temporary custom Slow should remain neutral");
			Equal(
				MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff,
				new StrengthPower().GetTypeForAmount(-8m),
				"neutral classification patch should preserve vanilla signed Counter behavior");
		}
		finally
		{
			neutralTypeHarmony.UnpatchAll(neutralTypeHarmony.Id);
		}
		Equal(1.08m, HextechPlayerSlowPower.ResolveDamageMultiplier(8m), "positive Slow should increase damage taken on either side");
		Equal(0.92m, HextechPlayerSlowPower.ResolveDamageMultiplier(-8m), "negative Slow should reduce damage taken on either side");
		Equal(0m, HextechPlayerSlowPower.ResolveDamageMultiplier(-120m), "negative Slow damage multiplier should floor at zero");
		Expect(!FrostWraithEnemyHex.ShouldTriggerForRound(1), "enemy Frost Wraith should not trigger on round one");
		Expect(!FrostWraithEnemyHex.ShouldTriggerForRound(2), "enemy Frost Wraith should wait for three player turns");
		Expect(FrostWraithEnemyHex.ShouldTriggerForRound(3), "enemy Frost Wraith should trigger at the start of the third player turn");
		Expect(FrostWraithEnemyHex.ShouldTriggerForRound(6), "enemy Frost Wraith should trigger every three rounds afterward");
		Equal(3, AncientStatueEnemyHex.ResolveCardSlowGain(0), "Ancient Statue tier zero fallback Slow gain");
		Equal(3, AncientStatueEnemyHex.ResolveCardSlowGain(1), "Ancient Statue tier one Slow gain");
		Equal(5, AncientStatueEnemyHex.ResolveCardSlowGain(2), "Ancient Statue tier two Slow gain");
		Equal(8, AncientStatueEnemyHex.ResolveCardSlowGain(3), "Ancient Statue tier three Slow gain");
		Equal(8, AncientStatueEnemyHex.ResolveCardSlowGain(99), "Ancient Statue high-tier Slow gain clamp");
		Equal(-2, HundredRefinementsEnemyHex.ResolveSlowReduction(0), "Hundred Refinements tier zero fallback Slow reduction");
		Equal(-2, HundredRefinementsEnemyHex.ResolveSlowReduction(1), "Hundred Refinements tier one Slow reduction");
		Equal(-4, HundredRefinementsEnemyHex.ResolveSlowReduction(2), "Hundred Refinements tier two Slow reduction");
		Equal(-6, HundredRefinementsEnemyHex.ResolveSlowReduction(3), "Hundred Refinements tier three Slow reduction");
		Equal(-6, HundredRefinementsEnemyHex.ResolveSlowReduction(99), "Hundred Refinements high-tier Slow reduction clamp");
	}

	[HextechTest]
	private static void EnemyCorrosionAppliesFrailOnEveryUnblockedPlayerHit()
	{
		Expect(CorrosionEnemyHex.ShouldApplyFrail(1m, targetIsPlayer: true), "enemy Corrosion should trigger on unblocked player damage");
		Expect(!CorrosionEnemyHex.ShouldApplyFrail(0m, targetIsPlayer: true), "enemy Corrosion should ignore fully blocked damage");
		Expect(!CorrosionEnemyHex.ShouldApplyFrail(1m, targetIsPlayer: false), "enemy Corrosion should ignore non-player targets");
		SequenceEqual(
			new[] { typeof(FrailPower) },
			MonsterHexCatalog.GetEnemyHexPowerHoverTipTypes(MonsterHexKind.Corrosion),
			"enemy Corrosion should explain Frail");
	}

	[HextechTest]
	private static void EnemyVitalitySurgeScalesAllSustainFromMaxHp()
	{
		Equal(1m, VitalitySurgeEnemyHex.ResolveMultiplier(0m), "Vitality Surge zero-HP multiplier");
		Equal(1m, VitalitySurgeEnemyHex.ResolveMultiplier(19m), "Vitality Surge below first threshold multiplier");
		Equal(1.01m, VitalitySurgeEnemyHex.ResolveMultiplier(20m), "Vitality Surge first threshold multiplier");
		Equal(1.05m, VitalitySurgeEnemyHex.ResolveMultiplier(119m), "Vitality Surge floors partial twenty-HP steps");
		Equal(1.06m, VitalitySurgeEnemyHex.ResolveMultiplier(120m), "Vitality Surge sixth threshold multiplier");
		Equal(1.29m, VitalitySurgeEnemyHex.ResolveMultiplier(599m), "Vitality Surge multiplier below cap");
		Equal(1.30m, VitalitySurgeEnemyHex.ResolveMultiplier(600m), "Vitality Surge multiplier at cap");
		Equal(1.30m, VitalitySurgeEnemyHex.ResolveMultiplier(6000m), "Vitality Surge multiplier above cap");
	}

	[HextechTest]
	private static void EnemyTwilightVeilMirrorsOnlyPositivePlayerBlock()
	{
		Expect(TwilightVeilEnemyHex.ShouldMirrorBlock(CombatSide.Player, 1m), "Twilight Veil should mirror positive player Block");
		Expect(!TwilightVeilEnemyHex.ShouldMirrorBlock(CombatSide.Player, 0m), "Twilight Veil should ignore zero player Block");
		Expect(!TwilightVeilEnemyHex.ShouldMirrorBlock(CombatSide.Enemy, 1m), "Twilight Veil should not recurse from enemy Block");
	}

	[HextechTest]
	private static void EnemyMiserableFateUsesMissingHpDivisors()
	{
		Equal(0, MiserableFateEnemyHex.ResolveBlock(100, 97, 4), "tier one should floor fewer than four missing HP");
		Equal(1, MiserableFateEnemyHex.ResolveBlock(100, 96, 4), "tier one first block threshold");
		Equal(3, MiserableFateEnemyHex.ResolveBlock(100, 90, 3), "tier two should floor missing HP thirds");
		Equal(5, MiserableFateEnemyHex.ResolveBlock(100, 90, 2), "tier three should use two missing HP per block");
		Equal(0, MiserableFateEnemyHex.ResolveBlock(100, 120, 2), "overhealing should not grant block");
		Equal(55, MiserableFateEnemyHex.ResolveBlock(100, -10, 2), "negative HP should count as additional missing HP");
	}

	[HextechTest]
	private static void EnemyHeavyHitterScalesDamageEveryFifteenMaxHp()
	{
		Equal(1m, HeavyHitterEnemyHex.ResolveMultiplier(0m), "Heavy Hitter zero-HP multiplier");
		Equal(1m, HeavyHitterEnemyHex.ResolveMultiplier(14m), "Heavy Hitter below first threshold multiplier");
		Equal(1.01m, HeavyHitterEnemyHex.ResolveMultiplier(15m), "Heavy Hitter first threshold multiplier");
		Equal(1.29m, HeavyHitterEnemyHex.ResolveMultiplier(449m), "Heavy Hitter multiplier below cap");
		Equal(1.30m, HeavyHitterEnemyHex.ResolveMultiplier(450m), "Heavy Hitter multiplier at cap");
		Equal(1.30m, HeavyHitterEnemyHex.ResolveMultiplier(4500m), "Heavy Hitter multiplier above cap");
	}

	[HextechTest]
	private static void EnemyJeweledGauntletOnlyRepeatsStandardIntentTypes()
	{
		IntentType[] repeatable =
		[
			IntentType.Attack,
			IntentType.Buff,
			IntentType.CardDebuff,
			IntentType.Debuff,
			IntentType.DebuffStrong,
			IntentType.Defend,
			IntentType.Heal,
			IntentType.StatusCard
		];
		foreach (IntentType intentType in repeatable)
		{
			Expect(
				HextechCombatHooks.IsJeweledGauntletIntentTypeRepeatable(intentType),
				$"enemy Jeweled Gauntlet should repeat {intentType}");
		}

		IntentType[] excluded =
		[
			IntentType.DeathBlow,
			IntentType.Escape,
			IntentType.Hidden,
			IntentType.Sleep,
			IntentType.Stun,
			IntentType.Summon,
			IntentType.Unknown
		];
		foreach (IntentType intentType in excluded)
		{
			Expect(
				!HextechCombatHooks.IsJeweledGauntletIntentTypeRepeatable(intentType),
				$"enemy Jeweled Gauntlet should exclude {intentType}");
		}
	}

	[HextechTest]
	private static void EnemyJeweledGauntletDuplicatesWholeIntentGroup()
	{
		BuffIntent buff = new();
		DebuffIntent debuff = new();
		IReadOnlyList<AbstractIntent> duplicated =
			HextechCombatHooks.DuplicateJeweledGauntletIntentGroup([buff, debuff]);

		Equal(4, duplicated.Count, "enemy Jeweled Gauntlet duplicated intent count");
		Expect(ReferenceEquals(buff, duplicated[0]), "first intent group should retain buff");
		Expect(ReferenceEquals(debuff, duplicated[1]), "first intent group should retain debuff");
		Expect(ReferenceEquals(buff, duplicated[2]), "second intent group should repeat buff");
		Expect(ReferenceEquals(debuff, duplicated[3]), "second intent group should repeat debuff");
		Expect(
			HextechCombatHooks.AreJeweledGauntletIntentsRepeatable([buff, debuff]),
			"ordinary multi-intent move should be repeatable");
		Expect(
			!HextechCombatHooks.AreJeweledGauntletIntentsRepeatable([buff, new SummonIntent()]),
			"a special intent should exclude the whole move from repetition");
		Expect(
			!HextechCombatHooks.AreJeweledGauntletIntentsRepeatable([]),
			"an empty intent group should not be repeatable");
	}

	[HextechTest]
	private static void EnemyJeweledGauntletNeverRepeatsIntoFinalKnowledgeDemonCurse()
	{
		const string curseMove = "CURSE_OF_KNOWLEDGE_MOVE";
		Expect(
			!HextechCombatHooks.WouldRepeatFinalKnowledgeDemonCurse(curseMove, 0),
			"first Knowledge Demon curse may repeat into its second stage");
		Expect(
			HextechCombatHooks.WouldRepeatFinalKnowledgeDemonCurse(curseMove, 1),
			"second-stage Knowledge Demon curse must not repeat into its third stage");
		Expect(
			HextechCombatHooks.WouldRepeatFinalKnowledgeDemonCurse(curseMove, 2),
			"third-stage Knowledge Demon curse should never repeat");
		Expect(
			HextechCombatHooks.WouldRepeatFinalKnowledgeDemonCurse(curseMove, 3),
			"out-of-range Knowledge Demon curse should remain guarded");
		Expect(
			!HextechCombatHooks.WouldRepeatFinalKnowledgeDemonCurse("SLAP_MOVE", 2),
			"other Knowledge Demon moves should remain repeatable");
	}

	[HextechTest]
	private static void PersonalHiveDazedRecipientAllowsMissingDealer()
	{
		Expect(HextechPersonalHiveSafetyHooks.HasDazedRecipient(null), "no dealer: vanilla skips on its own, so the original may run");
	}

	[HextechTest]
	private static void EnemyMoreTheMerrierUsesPooledRelicsForAllThreeMultipliers()
	{
		(HextechEnemyHexContext context, Player first, Player second) = CreatePrismaticEnemyFixture();
		List<RelicModel> firstRelics = Enumerable.Range(0, 11).Select(_ => (RelicModel)CreateMutableTestModel<MoreTheMerrierRune>()).ToList();
		List<RelicModel> secondRelics = Enumerable.Range(0, 10).Select(_ => (RelicModel)CreateMutableTestModel<MoreTheMerrierRune>()).ToList();
		AccessTools.Field(typeof(Player), "_relics").SetValue(first, firstRelics);
		AccessTools.Field(typeof(Player), "_relics").SetValue(second, secondRelics);
		MoreTheMerrierEnemyHex effect = new();
		Equal(1.10m, effect.ModifyDamageMultiplicative(context, null, 10m, ValueProp.Move, null, null), "21 relics across two players grant ten percent");
		Equal(1.10m, effect.ModifyBlockMultiplicative(context, first.Creature, 10m, ValueProp.Move, null, null), "same block coefficient");
		Equal(1.10m, effect.ModifyEnemyHealMultiplicative(context, first.Creature, 10m), "same healing coefficient");
		Equal(0.10m, effect.GetMaxHpBonusFraction(context, first.Creature), "same max HP bonus");
		secondRelics.Add(CreateMutableTestModel<MoreTheMerrierRune>());
		Equal(1.11m, effect.ModifyEnemyHealMultiplicative(context, first.Creature, 10m), "pool before rounding; changes update immediately");
		firstRelics.Clear();
		secondRelics.Clear();
		Equal(1m, effect.ModifyEnemyHealMultiplicative(context, first.Creature, 10m), "no relics means no bonus");
	}

	[HextechTest]
	private static void EnemyHitThresholdCountsPerEnemyAndCarriesRemainder()
	{
		Dictionary<uint, int> hits = new();
		bool[] fired = Enumerable.Range(0, 6).Select(_ => HextechEnemyHexEffect.ReachesHitThreshold(hits, 7, 3)).ToArray();
		SequenceEqual(new[] { false, false, true, false, false, true }, fired, "every third unblocked hit on the same enemy triggers");
		Expect(!HextechEnemyHexEffect.ReachesHitThreshold(hits, 8, 3), "each enemy keeps its own count");
		Equal(6, hits[7], "remainder carries forward instead of resetting");
		Expect(!HextechEnemyHexEffect.ReachesHitThreshold(new Dictionary<uint, int>(), 1, 0), "a zero threshold never triggers");
		Expect(HextechEnemyHexEffect.ReachesHitThreshold(new Dictionary<uint, int>(), 1, 1), "threshold one triggers on every hit");
	}

	[HextechTest]
	private static void KingdomArmyIgnoresForgesNestedInMinionGeneration()
	{
		// 王国军势生成仆从牌期间嵌套进来的铸造直接返回,凝辉/王令无法把它再次点燃。
		KingdomArmyRune kingdomArmy = CreateMutableTestModel<KingdomArmyRune>();
		typeof(KingdomArmyRune).GetField("_generating", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(kingdomArmy, true);
		Expect(kingdomArmy.AfterForge(3m, null!, null).IsCompletedSuccessfully, "nested forge during minion generation is ignored");
	}

	[HextechTest]
	private static void EnemyGiantSlayerScalesWithPlayerMaxHp()
	{
		Equal(0.33m, GiantSlayerEnemyHex.GetBonus(66), "66 max HP gives +33%");
		Equal(0.40m, GiantSlayerEnemyHex.GetBonus(80), "80 max HP gives +40%");
		Equal(0.40m, GiantSlayerEnemyHex.GetBonus(81), "partial steps round down");
		Equal(1.00m, GiantSlayerEnemyHex.GetBonus(260), "bonus caps at +100%");
		Equal(0m, GiantSlayerEnemyHex.GetBonus(0), "no max HP, no bonus");
		Expect(GiantSlayerEnemyHex.GetBonus(66) > 0.20m, "stays above silver Big Strength's flat +20% at the lowest starting max HP");
	}

	[HextechTest]
	private static void EnemyBlueCandleRaisesPlayerStatusAndCurseCosts()
	{
		(HextechEnemyHexContext context, Player first, Player _) = CreatePrismaticEnemyFixture();
		BlueCandleMedkitEnemyHex effect = new();
		CardModel slimed = CreateMutableTestModel<Slimed>();
		slimed.Owner = first;
		Equal(1, effect.GetBaseEnergyCostIncrease(context, slimed), "playable status cards cost one more");
		slimed.EnergyCost.AddThisTurn(-1, false);
		Equal(1, effect.GetBaseEnergyCostIncrease(context, slimed), "relative discounts still stack on top of the increase");
		slimed.EnergyCost.SetThisTurn(0);
		Equal(0, effect.GetBaseEnergyCostIncrease(context, slimed), "a this-turn free cost (Endless Rotation style) overrides the increase");
		CardModel regret = CreateMutableTestModel<Regret>();
		regret.Owner = first;
		Equal(0, effect.GetBaseEnergyCostIncrease(context, regret), "the unplayable -1 marker is left alone");
		CardModel strike = CreateMutableTestModel<StrikeIronclad>();
		strike.Owner = first;
		Equal(0, effect.GetBaseEnergyCostIncrease(context, strike), "ordinary cards are unaffected");
		// 优先级靠阶段保证:本效果在常规阶段,我方蓝烛药箱的 0 费与敌方开悟的下限都在 Late 阶段。
		const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
		Expect(typeof(BlueCandleMedkitRune).GetMethod(nameof(RelicModel.TryModifyEnergyCostInCombatLate), declared) != null
			&& typeof(BlueCandleMedkitRune).GetMethod(nameof(RelicModel.TryModifyEnergyCostInCombat), declared) == null,
			"our Blue Candle zeroes costs in the Late phase so the enemy increase cannot re-add one");
		Expect(typeof(BlueCandleMedkitEnemyHex).GetMethod(nameof(HextechEnemyHexEffect.ModifyEnergyCostInCombatLate), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null,
			"the enemy increase must stay out of the Late phase");
	}

	[HextechTest]
	private static void EnemyEnlightenmentFloorsDiscountedCostsWithoutChangingBase()
	{
		(HextechEnemyHexContext context, Player first, Player _) = CreatePrismaticEnemyFixture();
		CardModel card = CreateMutableTestModel<StrikeIronclad>();
		card.Owner = first;
		EnlightenmentEnemyHex effect = new();
		card.EnergyCost.SetThisTurn(0);
		Equal(1m, effect.ModifyEnergyCostInCombatLate(context, card, card.EnergyCost.GetWithModifiers(CostModifiers.Local)), "turn-free cards cost one after local modifiers");
		Equal(1m, effect.ModifyEnergyCostInCombatLate(context, card, -1m), "negative modified costs also floor to one");
		Equal(2m, effect.ModifyEnergyCostInCombatLate(context, card, 2m), "positive costs above one remain intact");
		Equal(0, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "floor does not overwrite original temporary cost");
		CardModel x = CreateMutableTestModel<Whirlwind>();
		x.Owner = first;
		Equal(0m, effect.ModifyEnergyCostInCombatLate(context, x, 0m), "X is not converted to fixed cost");
		card.EnergyCost.EndOfTurnCleanup();
		Equal(1, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "native cleanup still restores original card cost");
		SomethingForNothingEnemyHex zeroCostEnemy = new();
		ResourceInfo resources = new ResourceInfo { EnergyValue = 1, EnergySpent = 1, StarValue = 0, StarsSpent = 0 };
		Expect(zeroCostEnemy.ModifyCardPlayResultPileTypeAndPosition(context, card, false, resources, PileType.Discard, CardPilePosition.Bottom) == null, "raised play cost no longer triggers zero-cost exhaust");
	}

	[HextechTest]
	private static void EnemyReforgedHelmetBlocksOnlyEnemyStrengthLoss()
	{
		(HextechEnemyHexContext context, Player first, Player second) = CreatePrismaticEnemyFixture();
		Creature enemy = CreatePrismaticTestCreature(CombatSide.Enemy, (CombatState)first.Creature.CombatState!);
		ReforgedHelmetEnemyHex effect = new();
		Equal(3m, effect.ModifyPowerAmountReceived(context, new StrengthPower(), enemy, 3m, null), "positive enemy strength is no longer doubled");
		Equal(0m, effect.ModifyPowerAmountReceived(context, new StrengthPower(), enemy, -3m, first.Creature), "enemy strength reduction is blocked");
		Equal(0m, effect.ModifyPowerAmountReceived(context, new StrengthPower(), enemy, -3m, enemy), "self-applied strength loss including temporary expiry is blocked");
		Equal(-3m, effect.ModifyPowerAmountReceived(context, new DexterityPower(), enemy, -3m, null), "other stat reductions remain allowed");
		Equal(-3m, effect.ModifyPowerAmountReceived(context, new StrengthPower(), second.Creature, -3m, enemy), "player strength reduction remains allowed");
		Equal(3m, effect.ModifyPowerAmountReceived(context, new DexterityPower(), enemy, 3m, null), "other powers unchanged");
		Equal(3m, effect.ModifyPowerAmountReceived(context, new StrengthPower(), second.Creature, 3m, null), "players unchanged");
	}

	[HextechTest]
	private static void EnemyRotationStacksOnlyCurrentHandUntilTurnEnd()
	{
		(HextechEnemyHexContext context, Player first, Player second) = CreatePrismaticEnemyFixture();
		CardModel card = CreateMutableTestModel<StrikeIronclad>();
		CardModel other = CreateMutableTestModel<StrikeIronclad>();
		card.Owner = first;
		other.Owner = second;
		List<CardModel> hand = (List<CardModel>)AccessTools.Field(typeof(CardPile), "_cards").GetValue(first.PlayerCombatState!.Hand)!;
		hand.Add(card);
		((List<CardModel>)AccessTools.Field(typeof(CardPile), "_cards").GetValue(second.PlayerCombatState!.Hand)!).Add(other);
		EndlessRotationEnemyHex effect = new();
		effect.AfterShuffle(context, null!, first).GetAwaiter().GetResult();
		effect.AfterShuffle(context, null!, first).GetAwaiter().GetResult();
		Equal(3, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "two shuffles add two");
		Equal(1, other.EnergyCost.GetWithModifiers(CostModifiers.Local), "teammate hand unaffected");
		CardModel later = CreateMutableTestModel<StrikeIronclad>();
		later.Owner = first;
		hand.Add(later);
		Equal(1, later.EnergyCost.GetWithModifiers(CostModifiers.Local), "later draw not taxed retroactively");
		card.EnergyCost.AfterCardPlayedCleanup();
		Equal(3, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "returning card keeps whole-turn tax");
		card.EnergyCost.EndOfTurnCleanup();
		Equal(1, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "turn end restores cost");
	}

	[HextechTest]
	private static void EnemyZeroCostExhaustUsesPlayCostRatherThanPayment()
	{
		(HextechEnemyHexContext context, Player first, Player _) = CreatePrismaticEnemyFixture();
		SomethingForNothingEnemyHex effect = new();
		CardModel attack = CreateMutableTestModel<StrikeIronclad>();
		attack.Owner = first;
		ResourceInfo free = new ResourceInfo { EnergyValue = 0, EnergySpent = 0, StarValue = 0, StarsSpent = 0 };
		ResourceInfo auto = new ResourceInfo { EnergyValue = 2, EnergySpent = 0, StarValue = 0, StarsSpent = 0 };
		Equal(PileType.Exhaust, effect.ModifyCardPlayResultPileTypeAndPosition(context, attack, false, free, PileType.Discard, CardPilePosition.Bottom)!.Value.Item1, "discounted zero-cost card exhausts");
		Expect(effect.ModifyCardPlayResultPileTypeAndPosition(context, attack, true, auto, PileType.Discard, CardPilePosition.Bottom) == null, "free autoplay of costly card does not qualify");
		CardModel power = CreateMutableTestModel<Corruption>();
		power.Owner = first;
		Equal(PileType.Exhaust, effect.ModifyCardPlayResultPileTypeAndPosition(context, power, true, free, PileType.None, CardPilePosition.Bottom)!.Value.Item1, "zero-cost power uses native exhaust instead of removal");
	}

	private static readonly List<(CardModel Card, PileType Pile)> EnemyBranchGenerated = [];

	[HextechTest]
	private static void EnemyCorruptedBranchKeepsOwnerAndRestoresRandomSequence()
	{
		Type[] pool = [typeof(Burn), typeof(Dazed), typeof(Slimed), typeof(Wound), typeof(MegaCrit.Sts2.Core.Models.Cards.Void)];
		using IDisposable models = InjectMissingModels(pool);
		Harmony harmony = new("HextechRunes.Tests.EnemyCorruptedBranch");
		try
		{
			// 只隔离牌堆动画与存档 UI；保留真实状态牌创建、随机抽选与战斗序号。
			harmony.Patch(AccessTools.Method(typeof(HextechCardGeneration), "AddGeneratedCardToCombat"),
				prefix: new HarmonyMethod(typeof(Program), nameof(CaptureEnemyBranchGenerated)));
			(HextechEnemyHexContext context, Player first, Player second) = CreatePrismaticEnemyFixture();
			CorruptedBranchEnemyHex effect = new();
			CardModel source = CreateMutableTestModel<StrikeIronclad>();
			source.Owner = first;
			effect.AfterCardExhausted(context, null!, source, true).GetAwaiter().GetResult();
			string saved = context.Tracking.Serialize();
			effect.AfterCardExhausted(context, null!, source, false).GetAwaiter().GetResult();
			Type expectedNext = EnemyBranchGenerated[^1].Card.GetType();
			context.Tracking.Restore(saved);
			effect.AfterCardExhausted(context, null!, source, false).GetAwaiter().GetResult();
			Equal(expectedNext, EnemyBranchGenerated[^1].Card.GetType(), "restored ordinal reproduces next status");
			CardModel teammateSource = CreateMutableTestModel<StrikeIronclad>();
			teammateSource.Owner = second;
			effect.AfterCardExhausted(context, null!, teammateSource, false).GetAwaiter().GetResult();
			Equal(4, EnemyBranchGenerated.Count, "one status per actual exhaust, including ethereal");
			Expect(EnemyBranchGenerated.All(row => row.Pile == PileType.Discard && pool.Contains(row.Card.GetType())), "only fixed status pool into discard pile");
			Expect(EnemyBranchGenerated.Take(3).All(row => row.Card.Owner == first) && EnemyBranchGenerated[^1].Card.Owner == second, "each status belongs to the exhausting player");
			Equal(2, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(context.Tracking, first, nameof(CorruptedBranchEnemyHex)), "first player counter");
			Equal(1, HextechCombatProcTracker.GetPlayerRuneProcsInCombat(context.Tracking, second, nameof(CorruptedBranchEnemyHex)), "independent teammate counter");
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
			EnemyBranchGenerated.Clear();
		}
	}

	private static bool CaptureEnemyBranchGenerated(CardModel card, PileType pileType, bool addedByPlayer, CardPilePosition position, ref Task<CardPileAddResult?> __result)
	{
		Expect(!addedByPlayer && position == CardPilePosition.Random, "enemy generated card uses random insertion");
		EnemyBranchGenerated.Add((card, pileType));
		__result = Task.FromResult<CardPileAddResult?>(null);
		return false;
	}

	[HextechTest]
	private static void EnemyDebuffTriggersRejectOutgoingBuffsAndExpiry()
	{
		(HextechEnemyHexContext _, Player player, Player _) = CreatePrismaticEnemyFixture();
		Creature enemy = CreatePrismaticTestCreature(CombatSide.Enemy, (CombatState)player.Creature.CombatState!);
		WeakPower weak = CreateTestPower<WeakPower>(owner: enemy);
		Expect(HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(weak, 1, player.Creature, null), "receiving Weak triggers");
		Expect(!HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(weak, -1, player.Creature, null), "removing Weak does not trigger");
		Expect(!HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(CreateTestPower<WeakPower>(owner: player.Creature), 1, enemy, null), "outgoing player debuff no longer triggers");
		StrengthPower strength = CreateTestPower<StrengthPower>(owner: enemy);
		Expect(!HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(strength, 1, enemy, null), "self buff no longer triggers");
		Expect(HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(strength, -1, player.Creature, null), "external Strength loss triggers");
		Expect(!HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(strength, -1, enemy, null), "temporary Strength expiry must not re-arm the effects");
		Expect(!HextechEnemyPowerTriggerHelper.IsEnemyDebuffReceived(CreateTestPower<HextechTemporaryStrengthLossPower>(owner: enemy), 1, player.Creature, null), "temporary wrapper does not double-count its underlying Strength change");
		Expect(typeof(TemporaryStrengthPower).IsAssignableFrom(typeof(HextechSlapTemporaryStrengthPower)), "Slap uses native temporary Strength cleanup");
	}

	[HextechTest]
	private static void EnemyDrawProgressPoolsTheWholeTeamAndSurvivesReload()
	{
		HextechMayhemCombatTrackingState state = new();
		Dictionary<ulong, int> counts = state.NightstalkingPlayerCardsDrawnThisCombat;
		int threshold = NightstalkingEnemyHex.CardsPerSlippery;
		Equal(0, HextechEnemyDrawProgress.RecordTotal(counts, 11, threshold), "eleven team draws do not trigger");
		Equal(1, HextechEnemyDrawProgress.RecordTotal(counts, 20, threshold), "teammates' draws pool into one counter (4 players x 5 cards crosses 12)");
		Equal(0, HextechEnemyDrawProgress.RecordTotal(counts, 20, threshold), "repeated multiplayer settlement does not repeat rewards");
		Equal(0, state.WarmogsSpiritPlayerCardsDrawnThisCombat.Count, "Warmog counter remains separate");
		HextechMayhemCombatTrackingState restored = new();
		HextechMayhemCombatTrackingSerializer.Restore(restored, HextechMayhemCombatTrackingSerializer.Serialize(state));
		Equal(1, HextechEnemyDrawProgress.RecordDraw(restored.NightstalkingPlayerCardsDrawnThisCombat, 7), "local draws continue from the restored team total (20 -> 21 crosses 21)");
		Equal(2, HextechEnemyDrawProgress.RecordTotal(restored.NightstalkingPlayerCardsDrawnThisCombat, 36, threshold), "batched draw history grants each crossed threshold once");
		restored.PreparePlayerSideTurnStart();
		Equal(36, restored.NightstalkingPlayerCardsDrawnThisCombat[HextechEnemyDrawProgress.TeamCountKey], "draw counter spans turns");
		restored.Reset();
		Equal(0, restored.NightstalkingPlayerCardsDrawnThisCombat.Count, "next combat resets draws");

		// 旧版本按玩家分别存的进度（战斗中途读档）按合计接上，不会重复发奖。
		Dictionary<ulong, int> legacy = new() { [101UL] = 11, [202UL] = 11 };
		Equal(0, HextechEnemyDrawProgress.RecordTotal(legacy, 22, threshold), "legacy per-player progress is read as the team total");
		Equal(1, HextechEnemyDrawProgress.RecordTotal(legacy, 24, threshold), "pooling continues after the legacy total");
	}
}
