using System.Reflection;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void EnemyPowerScalingDoesNotPatchOfficialModifierPipeline()
	{
		BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
		MethodInfo? scaledPrefix = typeof(HextechEnemyPowerScalingHooks).GetMethod(
			"GetScaledAmountForMultiplayerPrefix",
			flags);
		MethodInfo? scaledTargets = typeof(HextechEnemyPowerScalingHooks).GetMethod(
			"ResolveGetScaledAmountForMultiplayerTargets",
			flags);
		Expect(scaledPrefix != null, "exact multiplayer amount prefix should remain installed");
		Expect(scaledTargets != null, "exact multiplayer amount targets should remain resolvable");

		IEnumerable<MethodInfo> targets = (IEnumerable<MethodInfo>)(scaledTargets!.Invoke(null, null)
			?? Array.Empty<MethodInfo>());
		MethodInfo[] resolvedTargets = targets.ToArray();
		Expect(resolvedTargets.Length > 0, "supported target should expose multiplayer amount scaling methods");
		Expect(
			resolvedTargets.All(static method => method.Name == nameof(PowerModel.GetScaledAmountForMultiplayer)),
			"retained enemy scaling targets must be limited to GetScaledAmountForMultiplayer");
	}

	[HextechTest]
	private static void EndlessMonsterPowerNormalizationUsesCapturedBaseAmounts()
	{
		Equal(9, HextechEndlessModeCompatibilityHooks.CalculateEndlessScaledAmount(9m, 1m), "unscaled Exoskeleton base amount");
		Equal(23, HextechEndlessModeCompatibilityHooks.CalculateEndlessScaledAmount(9m, 2.5m), "scaled Exoskeleton base amount");
		Equal(50, HextechEndlessModeCompatibilityHooks.CalculateEndlessScaledAmount(20m, 2.5m), "scaled Hardened Shell base amount");

		Harmony harmony = new("Natsuki.HextechRunes.Tests.EndlessPowerOrder");
		try
		{
			ApplyNestedPatches(harmony, typeof(HextechEndlessModeCompatibilityHooks));
			MethodInfo applyPower = typeof(MegaCrit.Sts2.Core.Commands.PowerCmd).GetMethod(
				nameof(MegaCrit.Sts2.Core.Commands.PowerCmd.Apply),
				BindingFlags.Public | BindingFlags.Static,
				binder: null,
				types:
				[
					typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
					typeof(PowerModel),
					typeof(Creature),
					typeof(decimal),
					typeof(Creature),
					typeof(CardModel),
					typeof(bool)
				],
				modifiers: null)
				?? throw new MissingMethodException(nameof(MegaCrit.Sts2.Core.Commands.PowerCmd), nameof(MegaCrit.Sts2.Core.Commands.PowerCmd.Apply));
			Patch capture = (Harmony.GetPatchInfo(applyPower)?.Prefixes.AsEnumerable() ?? Enumerable.Empty<Patch>())
				.Single(patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType?.Name == "ApplyPowerCapturePatch");
			Equal(Priority.First, capture.priority, "raw power capture priority");
			Expect(
				capture.before.Contains(HextechCombatHooks.EndlessModeHarmonyId),
				"raw power amount must be captured before EndlessMode changes it");

			MethodInfo exoskeletonAfterAdded = typeof(Exoskeleton).GetMethod(nameof(Exoskeleton.AfterAddedToRoom))
				?? throw new MissingMethodException(nameof(Exoskeleton), nameof(Exoskeleton.AfterAddedToRoom));
			Patch normalize = (Harmony.GetPatchInfo(exoskeletonAfterAdded)?.Postfixes.AsEnumerable() ?? Enumerable.Empty<Patch>())
				.Single(patch => patch.owner == harmony.Id && patch.PatchMethod.DeclaringType?.Name == "ExoskeletonPatch");
			Equal(Priority.Last, normalize.priority, "monster power normalization priority");
			Expect(
				normalize.after.Contains(HextechCombatHooks.EndlessModeHarmonyId),
				"monster power normalization must run after EndlessMode's entry hook");
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}
	}

	[HextechTest]
	private static void GlassCannonHealCapRunsAfterHealingMultipliers()
	{
		decimal capBeforeMultiplier = HextechCombatHooks.ClampHealAmountToCap(
			3885,
			5552,
			1m,
			GlassCannonEnemyHex.HealCapPercent);
		Equal(
			1m,
			HextechCombatHooks.ClampHealAmountToCap(
				3885,
				5552,
				capBeforeMultiplier * 2.5m,
				GlassCannonEnemyHex.HealCapPercent),
			"Hextech-first healing should be recapped after an external multiplier");
		Equal(
			1m,
			HextechCombatHooks.ClampHealAmountToCap(
				3885,
				5552,
				1m * 2.5m,
				GlassCannonEnemyHex.HealCapPercent),
			"external-first healing should stop at the same floored 70% cap");
		Equal(
			0m,
			HextechCombatHooks.ClampHealAmountToCap(3886, 5552, 100m, GlassCannonEnemyHex.HealCapPercent),
			"Glass Cannon should reject healing at its floored cap");
		MethodInfo mikaelsThresholdHook = typeof(MikaelsBlessingEnemyHex).GetMethod(
			nameof(MikaelsBlessingEnemyHex.AfterEnemyHealthThreshold),
			BindingFlags.Instance | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(MikaelsBlessingEnemyHex), nameof(MikaelsBlessingEnemyHex.AfterEnemyHealthThreshold));
		Expect(
			PatchProcessor.GetOriginalInstructions(GetAsyncStateMachineMoveNext(mikaelsThresholdHook))
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.DeclaringType == typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd)
					&& method.Name == nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd.Heal)),
			"Mikael's Blessing healing should pass through the globally capped heal command");

		Harmony harmony = new("Natsuki.HextechRunes.Tests.GlassCannonHealOrder");
		MethodInfo heal = typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd).GetMethod(
			nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd.Heal),
			BindingFlags.Static | BindingFlags.Public,
			binder: null,
			types: [typeof(Creature), typeof(decimal), typeof(bool)],
			modifiers: null)
			?? throw new MissingMethodException(nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd), nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd.Heal));

		try
		{
			ApplyNestedPatches(harmony, typeof(HextechCombatHooks), "HealPatch");
			IEnumerable<Patch> prefixes = Harmony.GetPatchInfo(heal)?.Prefixes.AsEnumerable()
				?? Enumerable.Empty<Patch>();
			Patch finalCap = prefixes
				.Single(patch => patch.owner == harmony.Id && patch.PatchMethod.Name == "FinalCapPrefix");
			Equal(Priority.Last, finalCap.priority, "Glass Cannon final heal cap priority");
			Expect(
				finalCap.after.Contains(HextechCombatHooks.EndlessModeHarmonyId),
				"Glass Cannon final heal cap must run after EndlessMode's healing multiplier");
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}
	}
}
