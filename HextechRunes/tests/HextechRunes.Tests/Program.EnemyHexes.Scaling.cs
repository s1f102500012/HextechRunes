using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using HextechRunes;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using System.Text.Json;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void EnemyCoefficientAddsWithinHexAndMultipliesAcrossHexes()
	{
		decimal oneHex = HextechEnemyCoefficientHelper.CombineBonusFractionsByHex(
		[
			(MonsterHexKind.TankEngine, 0.05m),
			(MonsterHexKind.TankEngine, 0.05m),
			(MonsterHexKind.TankEngine, 0.05m),
			(MonsterHexKind.TankEngine, 0.05m),
			(MonsterHexKind.TankEngine, 0.05m)
		]);
		Equal(1.25m, oneHex, "five Tank Engine contributions should add inside one enemy hex sector");

		decimal crossHex = HextechEnemyCoefficientHelper.CombineBonusFractionsByHex(
		[
			(MonsterHexKind.Goliath, 0.20m),
			(MonsterHexKind.AstralBody, 0.30m)
		]);
		Equal(1.56m, crossHex, "different enemy hex sectors should multiply");
	}

	[HextechTest]
	private static void EnemyMaxHpCoefficientSectorsUseBaseHp()
	{
		decimal scale = HextechEnemyCoefficientHelper.CombineBonusFractionsByHex(
		[
			(MonsterHexKind.Goliath, 0.20m),
			(MonsterHexKind.AstralBody, 0.20m),
			(MonsterHexKind.GoldenSpatula, 0.25m),
			(MonsterHexKind.MadScientist, -0.30m)
		]);

		Equal(1.26m, scale, "enemy max HP hex sectors");
		Equal(126m, Math.Floor(100m * scale), "enemy max HP should derive once from the tracked base HP");
	}

	[HextechTest]
	private static void EnemyMaxHpLegacyMigrationRecoversMixedSinglePlayerEffects()
	{
		Equal(
			100,
			HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
				currentMaxHp: 113,
				rawMonsterMaxHp: 100,
				appliedFixedBonusFractions: [0.20m, 0.30m, 0.25m],
				madScientistLossFraction: 0.30m,
				tankEngineStacks: 5),
			"legacy max HP migration should reverse fixed targets, Mad Scientist and compounded Tank Engine stacks");
		Equal(
			100,
			HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
				currentMaxHp: 130,
				rawMonsterMaxHp: 100,
				appliedFixedBonusFractions: [0.20m, 0.30m],
				madScientistLossFraction: 0m,
				tankEngineStacks: 0),
			"an old fixed target masks smaller unknown scaling, so migration should use the raw monster base");
		Equal(
			100,
			HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
				currentMaxHp: 156,
				rawMonsterMaxHp: null,
				appliedFixedBonusFractions: [0.20m, 0.30m],
				madScientistLossFraction: 0m,
				tankEngineStacks: 0),
			"legacy max HP migration should reverse chained fixed bonuses when the raw monster base is unavailable");

		int rawlessMixedBase = HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
			currentMaxHp: 110,
			rawMonsterMaxHp: null,
			appliedFixedBonusFractions: [0.20m, 0.30m],
			madScientistLossFraction: 0.30m,
			tankEngineStacks: 0);
		Equal(
			110m,
			Math.Floor(rawlessMixedBase * 1.20m * 1.30m * 0.70m),
			"rawless legacy migration should preserve the observed max HP after the new coefficient projection");
	}

	[HextechTest]
	private static void EnemyMaxHpLegacyMigrationPreservesMultiplayerScaling()
	{
		Equal(
			200,
			HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
				currentMaxHp: 161,
				rawMonsterMaxHp: 100,
				appliedFixedBonusFractions: [0.20m, 0.30m],
				madScientistLossFraction: 0.30m,
				tankEngineStacks: 3),
			"legacy max HP migration should retain a multiplayer-scaled base above every old fixed target");
		Equal(
			200,
			HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
				currentMaxHp: 200,
				rawMonsterMaxHp: 100,
				appliedFixedBonusFractions: [],
				madScientistLossFraction: 0m,
				tankEngineStacks: 0),
			"a fresh externally-scaled enemy should keep its current max HP as the coefficient base");
	}

	[HextechTest]
	private static void EnemyAttributeBoostsUseExpectedTiersAndCrossHexMultiplication()
	{
		Equal(0m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.Stats, 1), "Stats tier one bonus");
		Equal(0.05m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.Stats, 2), "Stats tier two bonus");
		Equal(0.10m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.Stats, 3), "Stats tier three bonus");
		Equal(0.05m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStats, 1), "Stats on Stats tier one bonus");
		Equal(0.10m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStats, 2), "Stats on Stats tier two bonus");
		Equal(0.15m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStats, 3), "Stats on Stats tier three bonus");
		Equal(0.10m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStatsOnStats, 1), "Stats on Stats on Stats tier one bonus");
		Equal(0.20m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStatsOnStats, 2), "Stats on Stats on Stats tier two bonus");
		Equal(0.30m, EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStatsOnStats, 3), "Stats on Stats on Stats tier three bonus");

		decimal combined = HextechEnemyCoefficientHelper.CombineBonusFractionsByHex(
		[
			(MonsterHexKind.Stats, 0.05m),
			(MonsterHexKind.Stats, 0.05m),
			(MonsterHexKind.StatsOnStats, 0.10m)
		]);
		Equal(1.21m, combined, "attribute bonuses should add within one hex and multiply across hexes");
	}

	[HextechTest]
	private static void EnemyMaxHpCoefficientThresholdsScaleWithPlayerCount()
	{
		Equal(1m, HeavyHitterEnemyHex.ResolveMultiplier(29m, 2), "two-player Heavy Hitter below 30-HP threshold");
		Equal(1.01m, HeavyHitterEnemyHex.ResolveMultiplier(30m, 2), "two-player Heavy Hitter first threshold");
		Equal(1.30m, HeavyHitterEnemyHex.ResolveMultiplier(900m, 2), "two-player Heavy Hitter cap");

		Equal(1m, VitalitySurgeEnemyHex.ResolveMultiplier(39m, 2), "two-player Vitality Surge below 40-HP threshold");
		Equal(1.01m, VitalitySurgeEnemyHex.ResolveMultiplier(40m, 2), "two-player Vitality Surge first threshold");
		Equal(1.30m, VitalitySurgeEnemyHex.ResolveMultiplier(1200m, 2), "two-player Vitality Surge cap");
	}

	[HextechTest]
	private static void EnemyBalanceUsesNewTierPercentagesAndUncappedSustain()
	{
		Equal(20, ExoskeletonEnemyHex.ResolveHardToKill(100, 1), "act one Hard to Kill threshold");
		Equal(15, ExoskeletonEnemyHex.ResolveHardToKill(100, 2), "act two Hard to Kill threshold");
		Equal(10, ExoskeletonEnemyHex.ResolveHardToKill(100, 3), "act three Hard to Kill threshold");
		Equal(6, ExoskeletonEnemyHex.ResolveHardToKill(10, 3), "Hard to Kill minimum remains six");
		for (int tier = 1; tier <= 3; tier++)
		{
			Equal(tier + 2, FinalFormEnemyHex.ResolvePlating(100, tier), "Final Form grants 3/4/5 percent Plating");
			Equal(tier, CourageOfColossusEnemyHex.ResolvePlating(100, tier), "Courage grants 1/2/3 percent Plating");
		}
		Equal(25, SoulEaterEnemyHex.ResolveMaxHpGain(100), "Soul Eater gains one quarter of the dead enemy's Max HP");
		Equal(24, SoulEaterEnemyHex.ResolveMaxHpGain(99), "Soul Eater rounds the Max HP gain down");
		Equal(30m, VantomEnemyHex.MaxHpPerStack, "Vantom requires thirty Max HP per Slippery");
	}
}
