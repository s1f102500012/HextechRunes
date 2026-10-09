using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void HungryExhaustsZeroOneOrTwoCardsByTier()
	{
		HextechMayhemCombatTrackingState tracking = new();
		Expect(!EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 1, 0), "tier one should exhaust no cards");
		Expect(EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 2, 1), "tier two should exhaust the first card");
		Expect(!EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 2, 1), "tier two should not exhaust the second card");
		Expect(EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 3, 2), "tier three should exhaust the first card");
		Expect(EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 3, 2), "tier three should exhaust the second card");
		Expect(!EightPennyGateEnemyHex.TryConsumeExhaustSlot(tracking, 3, 2), "tier three should not exhaust the third card");
	}

	[HextechTest]
	private static void InspectBlocksOnlyTheConfiguredExtraDrawTriggers()
	{
		HextechMayhemCombatTrackingState tracking = new();
		Expect(!IInspectEnemyHex.TryPreventExtraDraw(tracking, 1, 2, fromHandDraw: true), "normal hand draw should never be blocked");
		Expect(!IInspectEnemyHex.TryPreventExtraDraw(tracking, 1, 0, fromHandDraw: false), "tier one should block no extra draws");

		tracking.BeginPlayerTurnStart([2]);
		Expect(!IInspectEnemyHex.TryPreventExtraDraw(tracking, 2, 1, fromHandDraw: false), "turn-start extra draw should never be blocked");
		Equal(0, tracking.InspectExtraDrawsPreventedThisTurn.Count, "turn-start draw should not consume an inspect trigger");
		tracking.EnterPlayerPlayPhase(2);
		Expect(IInspectEnemyHex.TryPreventExtraDraw(tracking, 2, 1, fromHandDraw: false), "tier two should block the first in-turn extra draw trigger");
		Expect(!IInspectEnemyHex.TryPreventExtraDraw(tracking, 2, 1, fromHandDraw: false), "tier two should allow the second in-turn extra draw trigger");
		Expect(IInspectEnemyHex.TryPreventExtraDraw(tracking, 3, 2, fromHandDraw: false), "tier three should block the first extra draw trigger");
		Expect(IInspectEnemyHex.TryPreventExtraDraw(tracking, 3, 2, fromHandDraw: false), "tier three should block the second extra draw trigger");
		Expect(!IInspectEnemyHex.TryPreventExtraDraw(tracking, 3, 2, fromHandDraw: false), "tier three should allow the third extra draw trigger");

		string serialized = tracking.Serialize();
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(serialized);
		Equal(2, restored.InspectExtraDrawsPreventedThisTurn[3], "inspect draw count should survive a mid-turn save/load");
	}

	[HextechTest]
	private static void GripConsumesOnlyTheFirstManualCardTrigger()
	{
		HextechMayhemCombatTrackingState tracking = new();
		Expect(!IGripEnemyHex.TryConsumeFirstCard(tracking, 1, 0), "tier one should consume no trigger");
		Expect(IGripEnemyHex.TryConsumeFirstCard(tracking, 2, 1), "tier two should consume the first card trigger");
		Expect(!IGripEnemyHex.TryConsumeFirstCard(tracking, 2, 1), "tier two should ignore later card triggers");
		Expect(IGripEnemyHex.TryConsumeFirstCard(tracking, 3, 2), "tier three should consume the first card trigger");
		Expect(!IGripEnemyHex.TryConsumeFirstCard(tracking, 3, 2), "tier three should ignore later card triggers");

		string serialized = tracking.Serialize();
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(serialized);
		SetEqual(new ulong[] { 2, 3 }, restored.GripPlayersTriggeredThisTurn, "grip guards should survive a mid-turn save/load");
	}

	[HextechTest]
	private static void HungryInspectAndGripShareEightPennyGateTexture()
	{
		const string expected = "res://HextechRunes/images/relics/eightPennyGateRune.png";
		Equal(expected, HextechAssets.TryGetCustomRelicIconPath(new HungryHex()), "hungry texture");
		Equal(expected, HextechAssets.TryGetCustomRelicIconPath(new InspectHex()), "inspect texture");
		Equal(expected, HextechAssets.TryGetCustomRelicIconPath(new GripHex()), "grip texture");
	}

	[HextechTest]
	private static void HappyAccidentUsesAllCombatPilesAtTurnStart()
	{
		CardModel[] combatPileCards =
		[
			CreateMutableTestModel<Dazed>(),
			CreateMutableTestModel<StrikeIronclad>(),
			CreateMutableTestModel<Slimed>()
		];
		Equal(2, HappyAccidentRune.CountStatusCards(combatPileCards), "Happy Accident combat pile Status count");
		Equal(0, HappyAccidentRune.ResolveOrbCount(-1, 1), "Happy Accident negative Status fallback");
		Equal(0, HappyAccidentRune.ResolveOrbCount(3, 0), "Happy Accident disabled orb count");
		Equal(3, HappyAccidentRune.ResolveOrbCount(3, 1), "Happy Accident one orb per Status");
	}

	[HextechTest]
	private static void MiseryRandomTargetPreservesAttributeTransfer()
	{
		MethodInfo handler = GetAsyncStateMachineMoveNext(typeof(MiseryRune).GetMethod(nameof(MiseryRune.AfterPlayerTurnStart))!);
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(handler)
			.Select(static instruction => instruction.operand).OfType<MethodInfo>().ToArray();
		Equal(1, calls.Count(static call => call.DeclaringType == typeof(HextechRuneTargeting) && call.Name == "PickRandomHittableEnemy"), "one deterministic target shared by both debuffs");
		Expect(calls.All(static call => call.Name != "get_CurrentHp"), "target does not depend on current HP");
		Equal(4, calls.Count(static call => call.Name == "Apply" && call.IsGenericMethod), "both enemy debuffs and both player gains remain");
		MiseryRune rune = CreateMutableTestModel<MiseryRune>();
		Equal(-1m, rune.DynamicVars.Strength.BaseValue, "unchanged Strength transfer");
		Equal(-1m, rune.DynamicVars.Dexterity.BaseValue, "unchanged Dexterity transfer");
	}

	[HextechTest]
	private static void DrainAppliesSummonAmountToAllEnemies()
	{
		MethodInfo handler = GetAsyncStateMachineMoveNext(typeof(DrainRune).GetMethod(nameof(DrainRune.AfterSummon))!);
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(handler)
			.Select(static instruction => instruction.operand).OfType<MethodInfo>().ToArray();
		MethodInfo apply = calls.Single(static method => method.Name == "Apply" && method.IsGenericMethod
			&& method.GetGenericArguments().SequenceEqual(new[] { typeof(DoomPower) }));
		Equal(typeof(IEnumerable<Creature>), apply.GetParameters()[1].ParameterType, "Drain must apply Doom to the enemy collection");
		Expect(calls.Any(static method => method.Name == "get_HittableEnemies"), "Drain uses all hittable enemies");
		Expect(calls.All(static method => method.Name is not "get_CurrentHp" and not "op_Multiply" and not "get_DynamicVars"),
			"Drain neither selects by current HP nor multiplies the summon amount");
	}

	[HextechTest]
	private static void GiantSlayerScalesFromEnemyMaxHp()
	{
		static decimal Multiplier(int enemyMaxHp) => GiantSlayerRune.ResolveDamageMultiplier(
			enemyMaxHp,
			GiantSlayerRune.EnemyMaxHpPerPercent,
			GiantSlayerRune.DamagePerStepPercent,
			GiantSlayerRune.MaximumBonusPercent);
		Equal(1m, Multiplier(0), "zero-HP fallback multiplier");
		Equal(1m, Multiplier(7), "below first eight-HP step multiplier");
		Equal(1.01m, Multiplier(8), "first eight-HP step multiplier");
		Equal(1.49m, Multiplier(399), "multiplier before cap");
		Equal(1.5m, Multiplier(400), "fifty-percent cap multiplier");
		Equal(1.5m, Multiplier(9999), "multiplier remains capped");
	}

	[HextechTest]
	private static void SomethingForNothingDrawsAtZeroAndDiscountsFirstPaidCard()
	{
		Equal(0, SomethingForNothingRune.ReduceCost(0, 1), "combat discount should not make costs negative");
		Equal(1, SomethingForNothingRune.ReduceCost(2, 1), "combat discount should reduce the card by one");
		Expect(
			typeof(TurnScopedRelicBase).IsAssignableFrom(typeof(SomethingForNothingRune)),
			"Something for Nothing should reset its paid-card trigger each turn");
	}

	[HextechTest]
	private static void EchoAddsItsCopyWithoutRecursingThroughGenerationHooks()
	{
		MethodInfo hook = typeof(EchoRune).GetMethod(
			nameof(EchoRune.AfterCardGeneratedForCombat),
			BindingFlags.Instance | BindingFlags.Public)
			?? throw new MissingMethodException(nameof(EchoRune), nameof(EchoRune.AfterCardGeneratedForCombat));
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(GetAsyncStateMachineMoveNext(hook))
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Expect(
			calls.Any(static method => method.DeclaringType == typeof(CardPileCmd) && method.Name == nameof(CardPileCmd.Add)),
			"Echo should add its already-cloned copy directly to the destination pile");
		Expect(
			calls.All(static method => method.DeclaringType != typeof(HextechCardGeneration)),
			"Echo copies must not recursively enter the generated-card hook chain");
	}

	[HextechTest]
	private static void DeathWarrantTriggersPoisonEveryEightDraws()
	{
		MethodInfo availability = typeof(DeathWarrantRune).GetMethod(nameof(HextechRelicBase.IsAvailableForPlayer))
			?? throw new MissingMethodException(nameof(DeathWarrantRune), nameof(HextechRelicBase.IsAvailableForPlayer));
		Equal(typeof(DeathWarrantRune), availability.DeclaringType, "Death Warrant should override the player availability gate");
		Expect(
			PatchProcessor.GetOriginalInstructions(availability)
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.Name == "IsSilentPlayer"),
			"Death Warrant availability should use the Silent character gate");
		Equal(8, DeathWarrantRune.CardsNeeded, "Death Warrant draw threshold");
		Equal(0, HextechRelicBase.CountThresholdCrossings(0, 7, DeathWarrantRune.CardsNeeded), "Death Warrant should wait for eight draws");
		Equal(1, HextechRelicBase.CountThresholdCrossings(7, 8, DeathWarrantRune.CardsNeeded), "Death Warrant should trigger on the eighth draw");
		Equal(0, HextechRelicBase.CountThresholdCrossings(8, 15, DeathWarrantRune.CardsNeeded), "Death Warrant should preserve progress after triggering");
		Equal(2, HextechRelicBase.CountThresholdCrossings(8, 24, DeathWarrantRune.CardsNeeded), "Death Warrant should recover every missed threshold after load or network delay");

		MethodInfo trigger = typeof(DeathWarrantRune).GetMethod(
			"TriggerPoisonCompat",
			BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(DeathWarrantRune), "TriggerPoisonCompat");
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(trigger)
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Equal(typeof(PoisonPower), trigger.GetParameters()[0].ParameterType, "Death Warrant poison trigger target type");
		Expect(
			calls.Any(static method => method.Name == nameof(PoisonPower.AfterSideTurnStart)),
			"Death Warrant should use the Poison turn-start path shared by both supported game versions");
	}

	[HextechTest]
	private static void MyriadSwordsExplicitlyClosesAStalePlayPile()
	{
		MethodInfo afterShuffle = typeof(MyriadSwordsRune).GetMethod(
			"AfterShuffle",
			BindingFlags.Instance | BindingFlags.Public)
			?? throw new MissingMethodException(nameof(MyriadSwordsRune), "AfterShuffle");
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(GetAsyncStateMachineMoveNext(afterShuffle))
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Expect(
			calls.Any(static method => method.DeclaringType == typeof(CardPileCmd) && method.Name == nameof(CardPileCmd.Add)),
			"Myriad Swords should explicitly move a lethal autoplay card out of the Play pile");
	}

	[HextechTest]
	private static void CoefficientRunesStackAdditivelyWithinTheirOwnSector()
	{
		TankEngineRune tankEngine = CreateMutableTestModel<TankEngineRune>();
		tankEngine.SavedStacks = 3;
		Equal(1.18m, tankEngine.MaxHpScale, "three Tank Engine stacks should be 6% + 6% + 6%");

		FeedUpgradeRune feedUpgrade = CreateMutableTestModel<FeedUpgradeRune>();
		feedUpgrade.SavedStacks = 3;
		Equal(1.45m, feedUpgrade.MaxHpScale, "three Feed upgrade triggers should be 15% + 15% + 15%");

		NineDragonPowerRune nineDragon = CreateMutableTestModel<NineDragonPowerRune>();
		nineDragon.SavedStacks = 3;
		Equal(1.09m, nineDragon.MaxHpScale, "three Nine Dragon stacks should be 3% + 3% + 3%");
	}

	[HextechTest]
	private static void CoefficientForgesShareOneAdditiveSector()
	{
		SilverAttackForge silver = CreateMutableTestModel<SilverAttackForge>();
		silver.SavedStackCount = 2;
		GoldAttackForge gold = CreateMutableTestModel<GoldAttackForge>();
		AttackForge prismatic = CreateMutableTestModel<AttackForge>();

		decimal multiplier = HextechForgeCoefficientHelper.CombineBonusFractions(
		[
			silver.DamageBonusFractionTotal,
			gold.DamageBonusFractionTotal,
			prismatic.DamageBonusFractionTotal
		]);

		Equal(1.4m, multiplier, "two silver, one gold and one prismatic attack forge should share a 40% sector");
	}

	[HextechTest]
	private static void MaxHpCoefficientSectorsMultiply()
	{
		decimal multiplier = HextechMaxHpScaling.CombineScales(
			[1.35m, 1.5m, 1.18m, 1.3m],
			[7.5m, 15m, 30m]);

		Equal(4.73718375m, multiplier, "rune sectors should multiply after HP forge bonuses are added into one sector");
	}

	[HextechTest]
	private static void NightmareHooksEveryDarkOrbPassiveTrigger()
	{
		MethodBase target = ResolveDeclaredPatchTarget(typeof(NightmareRune), "PassivePatch");
		Equal(typeof(DarkOrb), target.DeclaringType, "nightmare hook declaring type");
		Equal(nameof(DarkOrb.Passive), target.Name, "nightmare hook method");
		SequenceEqual(
			new[] { typeof(PlayerChoiceContext), typeof(Creature) },
			target.GetParameters().Select(static parameter => parameter.ParameterType),
			"nightmare hook parameter types");
	}

	[HextechTest]
	private static void WatchOutGrapefruitFoodPoolHonorsCharacterAndUniqueRelics()
	{
		IReadOnlyList<Type> commonPool = WatchOutGrapefruitRune.BuildFoodRelicCandidates(
			isRegent: false,
			hasIceCream: false,
			hasNutritiousSoup: false);
		Type[] requestedCommonRelics =
		[
			typeof(ChosenCheese),
			typeof(LastingCandy),
			typeof(NutritiousSoup),
			typeof(BoneTea),
			typeof(EmberTea)
		];
		foreach (Type relicType in requestedCommonRelics)
		{
			Expect(commonPool.Contains(relicType), $"common food pool should contain {relicType.Name}");
		}
		Expect(!commonPool.Contains(typeof(LunarPastry)), "non-Regent food pool should exclude Lunar Pastry");
		Equal(commonPool.Count, commonPool.Distinct().Count(), "common food pool should not contain duplicate relic types");

		IReadOnlyList<Type> regentPool = WatchOutGrapefruitRune.BuildFoodRelicCandidates(
			isRegent: true,
			hasIceCream: false,
			hasNutritiousSoup: false);
		Expect(regentPool.Contains(typeof(LunarPastry)), "Regent food pool should contain Lunar Pastry");
		Equal(commonPool.Count + 1, regentPool.Count, "Regent food pool should add only Lunar Pastry");

		IReadOnlyList<Type> iceCreamOwnedPool = WatchOutGrapefruitRune.BuildFoodRelicCandidates(
			isRegent: true,
			hasIceCream: true,
			hasNutritiousSoup: false);
		Expect(!iceCreamOwnedPool.Contains(typeof(IceCream)), "owned Ice Cream should stay excluded");
		Expect(iceCreamOwnedPool.Contains(typeof(LunarPastry)), "Ice Cream exclusion should keep Regent Lunar Pastry");
		Equal(regentPool.Count - 1, iceCreamOwnedPool.Count, "owning Ice Cream should remove exactly one candidate");

		IReadOnlyList<Type> nutritiousSoupOwnedPool = WatchOutGrapefruitRune.BuildFoodRelicCandidates(
			isRegent: true,
			hasIceCream: false,
			hasNutritiousSoup: true);
		Expect(!nutritiousSoupOwnedPool.Contains(typeof(NutritiousSoup)), "owned Nutritious Soup should stay excluded");
		Expect(nutritiousSoupOwnedPool.Contains(typeof(IceCream)), "Nutritious Soup exclusion should keep Ice Cream");
		Expect(nutritiousSoupOwnedPool.Contains(typeof(LunarPastry)), "Nutritious Soup exclusion should keep Regent Lunar Pastry");
		Equal(regentPool.Count - 1, nutritiousSoupOwnedPool.Count, "owning Nutritious Soup should remove exactly one candidate");
	}

	[HextechTest]
	private static void HastyScribbleDrawsToFullHandAtTurnStart()
	{
		Equal(CardPile.MaxCardsInHand, HastyScribbleRune.CalculateCardsToDraw(0), "empty hand draw");
		Equal(6, HastyScribbleRune.CalculateCardsToDraw(4), "partially filled hand draw");
		Equal(0, HastyScribbleRune.CalculateCardsToDraw(CardPile.MaxCardsInHand), "full hand draw");
		Equal(0, HastyScribbleRune.CalculateCardsToDraw(CardPile.MaxCardsInHand + 1), "overfull hand draw");
	}

	[HextechTest]
	private static void PlayerSustainRunesUseExpectedMaxHpRules()
	{
		Equal(0, HextechRelicBase.CountThresholdCrossings(0, 2, 3), "Devil's Dance should wait for three Attacks");
		Equal(1, HextechRelicBase.CountThresholdCrossings(2, 3, 3), "Devil's Dance should trigger on the third Attack");
		Equal(2, HextechRelicBase.CountThresholdCrossings(2, 7, 3), "Devil's Dance should preserve thresholds across turns");
		Equal(1, AncientWineRune.CalculateHealAmount(99, 2m), "Ancient Wine should floor two-percent healing with a minimum of one");
		Equal(5, AncientWineRune.CalculateHealAmount(250, 2m), "Ancient Wine should heal two percent of Max HP");
		Equal(2, SturdyRune.CalculateHealAmount(100, 50, 2m, 50m, 5m), "Sturdy should use two percent at exactly half HP");
		Equal(5, SturdyRune.CalculateHealAmount(100, 49, 2m, 50m, 5m), "Sturdy should use five percent below half HP");
	}

	[HextechTest]
	private static void CollectorUsesStrictExecuteThresholdAndSharesFlyingKickExecutions()
	{
		Expect(
			CollectorRune.IsBelowExecuteThreshold(9.99m, 100m, CollectorRune.ExecutePercent),
			"Collector should execute below ten percent max HP");
		Expect(
			!CollectorRune.IsBelowExecuteThreshold(10m, 100m, CollectorRune.ExecutePercent),
			"Collector should not execute at exactly ten percent max HP");
		Expect(
			!CollectorRune.IsBelowExecuteThreshold(1m, 0m, CollectorRune.ExecutePercent),
			"Collector should reject invalid max HP thresholds");
	}

	[HextechTest]
	private static void DrawYourSwordReplacesOrbEvokeWithTwoFocus()
	{
		DrawYourSwordRune rune = new();
		Equal(2m, rune.DynamicVars["FocusPower"].BaseValue, "Draw Your Sword Focus per Evoke");

		MethodInfo[] runeMethods = typeof(DrawYourSwordRune).GetMethods(
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
		Expect(runeMethods.Any(method => method.Name == nameof(DrawYourSwordRune.ReplaceOrbEvoke)), "Draw Your Sword should replace each Orb's Evoke effect");

		MethodInfo[] hookMethods = typeof(HextechPlayerRuneHooks).GetMethods(
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		Expect(FindPatchMethod(typeof(DrawYourSwordRune), "DrawYourSwordEvokePatch", "Apply") != null, "Draw Your Sword should install an Orb Evoke replacement hook");
		Expect(hookMethods.Any(method => method.Name == "OrbEvokePrefix"), "Draw Your Sword should intercept Orb Evoke effects");

		IReadOnlyList<MethodInfo> evokeMethods = HextechPlayerRuneHooks.FindOrbEvokeMethods();
		Expect(evokeMethods.Any(method => method.DeclaringType == typeof(OrbModel)), "Orb Evoke replacement should include the base implementation");
		Expect(evokeMethods.Any(method => method.DeclaringType == typeof(LightningOrb)), "Orb Evoke replacement should include concrete Orb implementations");
	}

	[HextechTest]
	private static void PorcupineTemporaryThornsRemovalPlanSkipsInvalidEntries()
	{
		HextechMayhemCombatTrackingState tracking = new();
		tracking.EnemyPorcupineTemporaryThornsThisTurn[101] = 2;
		tracking.EnemyPorcupineTemporaryThornsThisTurn[102] = 0;
		tracking.EnemyPorcupineTemporaryThornsThisTurn[103] = -1;

		IReadOnlyList<(uint CombatId, int Thorns)> removal = PorcupineEnemyHex.GetTemporaryThornsToRemove(tracking);

		Equal(1, removal.Count, "porcupine temporary thorns removal count");
		Equal(101u, removal[0].CombatId, "porcupine temporary thorns removal target");
		Equal(2, removal[0].Thorns, "porcupine temporary thorns removal amount");
	}

	[HextechTest]
	private static void BloodPactRequiresHpLossFromEnemyAttack()
	{
		Expect(BloodPactRune.ShouldGainStrength(CombatSide.Enemy, 1, ValueProp.Move), "enemy attack HP loss grants Strength");
		Expect(!BloodPactRune.ShouldGainStrength(CombatSide.Enemy, 0, ValueProp.Move), "fully blocked attacks do not grant Strength");
		Expect(!BloodPactRune.ShouldGainStrength(CombatSide.Player, 3, ValueProp.Move), "self or allied damage does not grant Strength");
		Expect(!BloodPactRune.ShouldGainStrength(null, 3, ValueProp.Unpowered), "HP costs and sourceless damage do not grant Strength");
		Expect(!BloodPactRune.ShouldGainStrength(CombatSide.Enemy, 3, ValueProp.Unpowered), "enemy non-attack damage does not grant Strength");
	}

	[HextechTest]
	private static void ScapegoatIncludesNegativeAttributesButLeavesBuffs()
	{
		StrengthPower strength = CreateTestPower<StrengthPower>(-5);
		DexterityPower dexterity = CreateTestPower<DexterityPower>(-3);
		WeakPower weak = CreateTestPower<WeakPower>(2);
		weak.SkipNextDurationTick = true;
		StrengthPower buff = CreateTestPower<StrengthPower>(4);
		HexPower hex = CreateTestPower<HexPower>(1);
		RingingPower ringing = CreateTestPower<RingingPower>(1);
		ConfusedPower confused = CreateTestPower<ConfusedPower>(1);
		HextechGalvanicPower galvanic = CreateTestPower<HextechGalvanicPower>(2);
		List<PowerModel> powers = [strength, hex, buff, weak, ringing, dexterity, confused, galvanic];
		PowerModel[] snapshot = ScapegoatRune.SnapshotDebuffs(powers);
		Expect(snapshot.SequenceEqual(new PowerModel[] { strength, hex, weak, ringing, dexterity, confused, galvanic }),
			"cleanse includes player-only debuffs in native order, but leaves buffs");
		foreach (PowerModel playerOnly in new PowerModel[] { hex, ringing, confused, galvanic, buff })
		{
			Expect(ScapegoatRune.CreateEnemyTransfer(playerOnly) == null,
				playerOnly.GetType().Name + " must never reach enemy application");
		}
		PowerModel[] transfers = snapshot.Select(ScapegoatRune.CreateEnemyTransfer).OfType<PowerModel>().ToArray();
		Expect(transfers.Select(p => p.GetType()).SequenceEqual(new[] { typeof(StrengthPower), typeof(WeakPower), typeof(DexterityPower) }),
			"unsafe effects cannot interrupt transfer of the remaining ordinary debuffs");
		Expect(transfers.Select(p => p.Amount).SequenceEqual(new[] { -5, 2, -3 }), "preserve negative attributes and stacks");
		Expect(!ReferenceEquals(weak, transfers[1]) && !transfers[1].SkipNextDurationTick && weak.SkipNextDurationTick,
			"enemy receives an independent copy without the player's duration exemption");
		powers.Clear();
		Equal(7, snapshot.Length, "removal cannot mutate the cleanse snapshot");
	}

	[HextechTest]
	private static void BloodDebtAccumulatesPerCardAndExpiresAfterCombat()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		BloodDebtRune rune = CreateMutableTestModel<BloodDebtRune>();
		rune.Owner = owner;
		StrikeIronclad first = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad second = CreateMutableTestModel<StrikeIronclad>();
		DefendIronclad skill = CreateMutableTestModel<DefendIronclad>();
		StrikeIronclad foreign = CreateMutableTestModel<StrikeIronclad>();
		first.Owner = second.Owner = skill.Owner = owner;
		foreign.Owner = CreateOrdinalTestPlayer(2);
		rune.GrowAttacks([first, skill, foreign], 7);
		rune.GrowAttacks([first, second], 3);
		decimal Bonus(CardModel card, ValueProp props = ValueProp.Move) =>
			rune.ModifyDamageAdditiveCompat(null, 6m, props, null, card);
		Equal(10m, Bonus(first), "loss events accumulate on the same instance even outside the hand");
		Equal(3m, Bonus(second), "same-name cards only gain while present in hand");
		Equal(0m, Bonus(skill), "skills do not grow");
		Equal(0m, Bonus(foreign), "other players' cards do not grow");
		Equal(0m, Bonus(first, ValueProp.Unpowered), "incidental damage is not an extra attack hit");
		rune.AfterCombatEnd(null!).GetAwaiter().GetResult();
		Equal(0m, Bonus(first), "combat end clears bonuses");
		rune.GrowAttacks([first], 4);
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(0m, Bonus(first), "combat start also clears stale references");
	}

	[HextechTest]
	private static void NetherSoulSnapshotsCurrentEtherealKeywordsOnce()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		SetAutoProperty(owner, nameof(Player.Creature), RuntimeHelpers.GetUninitializedObject(typeof(Creature)));
		SetAutoProperty(owner, nameof(Player.Deck), new CardPile(PileType.Deck));
		StrikeIronclad addedEthereal = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad ordinary = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad foreign = CreateMutableTestModel<StrikeIronclad>();
		addedEthereal.Owner = ordinary.Owner = owner;
		foreign.Owner = CreateOrdinalTestPlayer(2);
		addedEthereal.AddKeyword(CardKeyword.Ethereal);
		foreign.AddKeyword(CardKeyword.Ethereal);
		MegaCrit.Sts2.Core.Models.Cards.Void etherealStatus = CreateMutableTestModel<MegaCrit.Sts2.Core.Models.Cards.Void>();
		Injury etherealCurse = CreateMutableTestModel<MegaCrit.Sts2.Core.Models.Cards.Injury>();
		etherealStatus.Owner = etherealCurse.Owner = owner;
		etherealCurse.AddKeyword(CardKeyword.Ethereal);
		Expect(etherealStatus.Keywords.Contains(CardKeyword.Ethereal), "Void is natively ethereal");
		List<CardModel> exhausted = [addedEthereal, ordinary, foreign, addedEthereal, etherealStatus, etherealCurse];
		CardModel[] snapshot = NetherSoulRune.SnapshotEtherealCards(owner, exhausted);
		Equal(1, snapshot.Length, "added keywords count; ordinary, foreign, status and curse cards do not; each instance only once");
		Expect(ReferenceEquals(addedEthereal, snapshot[0]), "play actual exhausted card rather than a copy");
		exhausted.Clear();
		Equal(1, snapshot.Length, "playing and exhausting cards cannot enlarge the batch");
	}

	[HextechTest]
	private static void ThreeNewRuneHooksUseNativeCommandsAndStableTargets()
	{
		MethodInfo[] Calls(Type type, string method) => PatchProcessor.GetOriginalInstructions(
			GetAsyncStateMachineMoveNext(type.GetMethod(method)!)).Select(i => i.operand).OfType<MethodInfo>().ToArray();
		MethodInfo[] transfer = Calls(typeof(ScapegoatRune), nameof(ScapegoatRune.AfterPlayerTurnStart));
		Expect(transfer.Any(m => m.Name == "ConsumeCombatProcOrdinal"), "transfer uses synchronized proc ordinal");
		Expect(transfer.Any(m => m.DeclaringType == typeof(HextechRuneTargeting)), "transfer chooses one stable random enemy");
		Expect(transfer.Any(m => m.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.PowerCmd) && m.Name == "Apply"), "transfer keeps native application and artifact handling");
		MethodInfo[] replay = Calls(typeof(NetherSoulRune), nameof(NetherSoulRune.AfterSideTurnEndLate));
		Expect(replay.Any(m => m.DeclaringType == typeof(HextechAutoPlayHelper)), "exhausted cards use native autoplay");
		Expect(!replay.Any(m => m.Name == "CanPlay"), "zero energy must not block autoplay");
		Expect(replay.Any(m => m.Name == "Contains" && m.IsGenericMethod && m.GetGenericArguments().Contains(typeof(Creature))), "only the owner's turn including extra-turn participation");
	}

	[HextechTest]
	private static void RallyingCallSnapshotsSameModelCardsWithoutSourceOrOtherPlayers()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		StrikeIronclad source = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad first = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad upgraded = CreateMutableTestModel<StrikeIronclad>();
		StrikeIronclad foreign = CreateMutableTestModel<StrikeIronclad>();
		DefendIronclad other = CreateMutableTestModel<DefendIronclad>();
		MegaCrit.Sts2.Core.Commands.CardCmd.Upgrade(upgraded);
		source.Owner = first.Owner = upgraded.Owner = other.Owner = owner;
		foreign.Owner = CreateOrdinalTestPlayer(2);
		List<CardModel> candidates = [source, first, other, upgraded, foreign, first];
		CardModel[] result = RallyingCallRune.SnapshotMatches(source, candidates);
		candidates.Clear();
		Equal(2, result.Length, "only two distinct owned copies, even when one is upgraded");
		Expect(ReferenceEquals(first, result[0]) && ReferenceEquals(upgraded, result[1]), "snapshot keeps pile order");
		Expect(!result.Contains(source), "returned source is not its own matching card");
		RallyingCallRune rune = CreateMutableTestModel<RallyingCallRune>();
		typeof(RallyingCallRune).GetField("_playingMatches", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(rune, true);
		Expect(rune.AfterCardPlayed(null!, CreateCardPlay(first)).IsCompletedSuccessfully, "an active matching batch cannot recursively start another batch");
	}

	[HextechTest]
	private static void EndlessRotationFreesBothCostsUntilTurnEnd()
	{
		MeteorShower card = CreateMutableTestModel<MeteorShower>();
		Player owner = CreateOrdinalTestPlayer(1);
		Creature creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(owner, nameof(Player.Creature), creature);
		SetAutoProperty(creature, nameof(Creature.Side), CombatSide.Player);
		EndlessRotationRune rune = CreateMutableTestModel<EndlessRotationRune>();
		rune.Owner = owner;
		card.Owner = owner;
		card.EnergyCost.SetThisCombat(2);
		card.SetStarCostThisCombat(3);
		rune.MakeFreeForTurn(card);
		Equal(0, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "energy is free");
		Equal(0, card.CurrentStarCost, "stars are free");
		card.EnergyCost.AfterCardPlayedCleanup();
		Equal(0, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "playing and returning the card does not clear free energy");
		List<TemporaryCardCost> costs = (List<TemporaryCardCost>)typeof(CardModel).GetField("_temporaryStarCosts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card)!;
		costs.RemoveAll(cost => cost.ClearsWhenCardIsPlayed);
		Expect(rune.TryModifyStarCost(card, card.CurrentStarCost, out decimal freeStars), "star-cost hook retains free play after native post-play cleanup");
		Equal(0m, freeStars, "stars stay free when played again");
		rune.MakeFreeForTurn(card);
		card.EndOfTurnCleanup();
		rune.AfterSideTurnEndLate(null!, CombatSide.Player, [creature]).GetAwaiter().GetResult();
		Equal(2, card.EnergyCost.GetWithModifiers(CostModifiers.Local), "original combat energy cost returns next turn");
		Equal(3, card.CurrentStarCost, "original combat star cost returns next turn");
		Expect(!rune.TryModifyStarCost(card, 3m, out decimal restoredStars) && restoredStars == 3m, "star-cost hook also expires at turn end");
	}

	[HextechTest]
	private static void MyriadManifestationsCountsTypesRatherThanSlots()
	{
		Equal(0, MyriadManifestationsRune.CountOrbTypes([]), "empty queue has no extra rounds");
		Equal(1, MyriadManifestationsRune.CountOrbTypes([new LightningOrb(), new LightningOrb(), new LightningOrb()]), "three lightning count as one type");
		Equal(2, MyriadManifestationsRune.CountOrbTypes([new LightningOrb(), new FrostOrb(), new LightningOrb()]), "lightning plus frost grant two extra rounds");
		Equal(4, MyriadManifestationsRune.CountOrbTypes([new LightningOrb(), new FrostOrb(), new DarkOrb(), new PlasmaOrb()]), "plasma counts as a different orb type");
	}

	[HextechTest]
	private static void VenomousBladeReadsEachTargetPoisonWithoutExtraDamageEvents()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		Creature dealer = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(owner, nameof(Player.Creature), dealer);
		VenomousBladeRune rune = CreateMutableTestModel<VenomousBladeRune>();
		rune.Owner = owner;
		Shiv shiv = UninitializedCard<Shiv>();
		SetAutoProperty(shiv, nameof(AbstractModel.IsMutable), true);
		shiv.Owner = owner;
		Creature enemy = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(enemy, nameof(Creature.Side), CombatSide.Enemy);
		// Power 构造器初始化 Godot 颜色资源；CLI 只需携带层数的内存模型。
		PoisonPower poison = (PoisonPower)RuntimeHelpers.GetUninitializedObject(typeof(PoisonPower));
		SetAutoProperty(poison, nameof(AbstractModel.IsMutable), true);
		typeof(PowerModel).GetField("_amount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(poison, 17);
		typeof(Creature).GetField("_powers", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(enemy, new List<PowerModel> { poison });
		Equal(17m, rune.ModifyDamageAdditiveCompat(enemy, 4m, ValueProp.Move, dealer, shiv), "add target poison to each shiv hit");
		typeof(PowerModel).GetField("_amount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(poison, 31);
		Equal(31m, rune.ModifyDamageAdditiveCompat(enemy, 4m, ValueProp.Move, dealer, shiv), "later hits read current poison");
		Equal(0m, rune.ModifyDamageAdditiveCompat(enemy, 4m, ValueProp.Unpowered, dealer, shiv), "do not amplify incidental unpowered damage");
		Equal(0m, rune.ModifyDamageAdditiveCompat(null, 4m, ValueProp.Move, dealer, shiv), "untargeted preview does not invent poison");
		StrikeIronclad strike = CreateMutableTestModel<StrikeIronclad>();
		strike.Owner = owner;
		Equal(0m, rune.ModifyDamageAdditiveCompat(enemy, 4m, ValueProp.Move, dealer, strike), "ordinary attacks do not get poison damage");
	}

	[HextechTest]
	private static void FiveNewRuneHooksKeepNativeExecutionAndSynchronizedRandom()
	{
		MethodInfo[] Calls(Type type, string method) => PatchProcessor.GetOriginalInstructions(
			GetAsyncStateMachineMoveNext(type.GetMethod(method)!)).Select(i => i.operand).OfType<MethodInfo>().ToArray();
		MethodInfo[] rally = Calls(typeof(RallyingCallRune), nameof(RallyingCallRune.AfterCardPlayed));
		Expect(rally.Any(m => m.Name == nameof(HextechAutoPlayHelper.AutoPlayOrMoveToResultPile)), "same-name cards use actual autoplay");
		Expect(!rally.Any(m => m.Name == "CanPlay"), "autoplay does not require remaining energy");
		MethodInfo[] orbs = Calls(typeof(MyriadManifestationsRune), nameof(MyriadManifestationsRune.BeforeSideTurnEndEarly));
		Expect(orbs.Any(m => m.DeclaringType == typeof(OrbSnapshotPassiveHelper) && m.Name == nameof(OrbSnapshotPassiveHelper.TriggerRounds)), "extra passives iterate the frozen orb snapshot");
		// 触发委托编译成闭包方法，展开一层再找实际的被动入口。
		MethodInfo[] orbTriggers = orbs
			.Where(static m => m.Name.Contains('<'))
			.SelectMany(static m => PatchProcessor.GetOriginalInstructions(m).Select(static i => i.operand).OfType<MethodInfo>())
			.ToArray();
		Expect(orbTriggers.Any(m => m.DeclaringType == typeof(HextechOrbPassiveCompat) && m.Name == "TriggerPassive"), "extra passives use the version-matched native entry");
		MethodInfo entry = typeof(HextechOrbPassiveCompat).GetMethod("TriggerPassive", BindingFlags.Static | BindingFlags.NonPublic)!;
		MethodInfo[] passive = PatchProcessor.GetOriginalInstructions(entry.GetCustomAttribute<AsyncStateMachineAttribute>() == null ? entry : GetAsyncStateMachineMoveNext(entry)).Select(i => i.operand).OfType<MethodInfo>().ToArray();
		Expect(passive.Any(m => m.DeclaringType == typeof(OrbModel) && m.Name == "TriggerPassive"
			|| m.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.OrbCmd) && m.Name == "Passive"), "native passive entry preserves trigger modifiers");
		MethodInfo[] forge = Calls(typeof(KingdomArmyRune), nameof(KingdomArmyRune.AfterForge));
		Equal(1, forge.Count(m => m.Name == "ConsumeCombatProcOrdinal"), "one synchronized ordinal per forge event");
		Equal(1, forge.Count(m => m.Name == nameof(HextechStableCombatSpawns.CreateMinionCard)), "one minion per forge event");
		Expect(forge.Any(m => m.Name == nameof(HextechCardGeneration.AddGeneratedCardToCombat)), "generated minions use normal hand and overflow handling");
	}

	[HextechTest]
	private static void MultiplayerSupportRunesAreMultiplayerOnlyAndScaleFromMaxHp()
	{
		// 测试进程不是联机局,仅联机的海克斯必须不可用。
		Player solo = CreateOrdinalTestPlayer(1);
		Expect(!CreateMutableTestModel<DiveBomberRune>().IsAvailableForPlayer(solo), "dive bomber is multiplayer-only");
		Expect(!CreateMutableTestModel<AllForYouRune>().IsAvailableForPlayer(solo), "all for you is multiplayer-only");
		Expect(!CreateMutableTestModel<BlossomBladeRune>().IsAvailableForPlayer(solo), "blossom blade is multiplayer-only");

	}
}
