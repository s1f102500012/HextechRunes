using System.Reflection;
using Godot;
using HarmonyLib;
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
	private static void PlayerRuneRarityConfigExcludesFullyDisabledTier()
	{
		HashSet<string> disabledIds = GetConfigurableRuneEntries(HextechRarityTier.Silver);

		IReadOnlyList<HextechRarityTier> enabled = HextechRunePoolBuilder.GetEnabledPlayerRuneRaritiesForDisabledIds(disabledIds);

		Expect(!enabled.Contains(HextechRarityTier.Silver), "fully disabled silver tier should be excluded");
		Expect(enabled.Contains(HextechRarityTier.Gold), "gold tier should remain enabled");
		Expect(enabled.Contains(HextechRarityTier.Prismatic), "prismatic tier should remain enabled");
	}

	[HextechTest]
	private static void PlayerRuneRarityConfigFallsBackWhenAllTiersDisabled()
	{
		HashSet<string> disabledIds = GetConfigurableRuneEntries(
			HextechRarityTier.Silver,
			HextechRarityTier.Gold,
			HextechRarityTier.Prismatic);

		IReadOnlyList<HextechRarityTier> enabled = HextechRunePoolBuilder.GetEnabledPlayerRuneRaritiesForDisabledIds(disabledIds);

		SequenceEqual(Enum.GetValues<HextechRarityTier>(), enabled, "all disabled fallback rarities");
	}

	[HextechTest]
	private static void FlyingKickDisableSurvivesNormalizationAndStrictPoolFiltering()
	{
		string flyingKickId = ModelDb.GetId<FlyingKickRune>().Entry;
		HashSet<string> disabledIds = HextechRuneConfiguration.NormalizeDisabledPlayerRuneIds([ flyingKickId ]);
		Expect(disabledIds.Contains(flyingKickId), "Flying Kick disable should survive config import normalization");

		RelicModel flyingKick = CreateMutableTestModel<FlyingKickRune>();
		RelicModel doubleVision = CreateMutableTestModel<DoubleVisionRune>();
		List<RelicModel> filtered = HextechRunePoolBuilder.FilterDisabledPlayerRunes(
			[ flyingKick, doubleVision ],
			disabledIds);
		SequenceEqual(
			new[] { ModelDb.GetId<DoubleVisionRune>() },
			filtered.Select(static relic => relic.CanonicalInstance?.Id ?? relic.Id),
			"disabled Flying Kick should never re-enter a partially filtered pool");
		Expect(
			HextechRunePoolBuilder.FilterDisabledPlayerRunes([ flyingKick ], disabledIds).Count == 0,
			"an exhausted pool must remain empty instead of restoring disabled Flying Kick");
	}

	[HextechTest]
	private static void RarityRollResolverFiltersWeightedRarities()
	{
		HextechRarityWeights weights = HextechRarityRollResolver.ApplyEnabledRarities(
			new HextechRarityWeights(20, 50, 30),
			[ HextechRarityTier.Gold, HextechRarityTier.Prismatic ]);

		Equal(0, weights.Silver, "silver weight");
		Equal(50, weights.Gold, "gold weight");
		Equal(30, weights.Prismatic, "prismatic weight");
		Equal(80, weights.Total, "total weight");
		Equal(HextechRarityTier.Gold, HextechRarityRollResolver.ResolveWeighted(weights, 0), "first gold roll");
		Equal(HextechRarityTier.Gold, HextechRarityRollResolver.ResolveWeighted(weights, 49), "last gold roll");
		Equal(HextechRarityTier.Prismatic, HextechRarityRollResolver.ResolveWeighted(weights, 50), "first prismatic roll");
		Equal(HextechRarityTier.Prismatic, HextechRarityRollResolver.ResolveWeighted(weights, 79), "last prismatic roll");
	}

	[HextechTest]
	private static void RarityRollResolverUsesOrderedUniformFallback()
	{
		HextechRarityTier[] order = HextechRarityRollResolver.GetUniformRarityOrder(
			[ HextechRarityTier.Prismatic, HextechRarityTier.Silver ]);

		SequenceEqual(new[] { HextechRarityTier.Silver, HextechRarityTier.Prismatic }, order, "uniform rarity order");
		SequenceEqual(Enum.GetValues<HextechRarityTier>(), HextechRarityRollResolver.GetUniformRarityOrder([]), "empty enabled fallback order");
		Expect(HextechRarityRollResolver.HasAllRarities(Enum.GetValues<HextechRarityTier>()), "all-rarity detection");
		Expect(!HextechRarityRollResolver.HasAllRarities(order), "partial-rarity detection");
	}

	[HextechTest]
	private static void ConsecutiveSilverRuleExcludesSilverFromEveryLaterAct()
	{
		HextechRarityWeights configured = new(2, 5, 3);
		HextechRarityTier[] all = Enum.GetValues<HextechRarityTier>();
		Equal(
			configured,
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(configured, all, true, 0, null).Weights,
			"first act weights");
		Equal(
			configured,
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(configured, all, true, 1, HextechRarityTier.Gold).Weights,
			"weights after non-Silver act");
		(HextechRarityWeights disabledWeights, IReadOnlyList<HextechRarityTier> disabledCandidates) =
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(configured, all, false, 1, HextechRarityTier.Silver);
		Equal(configured, disabledWeights, "disabled consecutive-Silver rule");
		SequenceEqual(all, disabledCandidates, "disabled rule keeps candidates");
		Equal(
			new HextechRarityWeights(0, 5, 3),
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(configured, all, true, 1, HextechRarityTier.Silver).Weights,
			"second act weights after Silver");
		Equal(
			new HextechRarityWeights(0, 5, 3),
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(configured, all, true, 2, HextechRarityTier.Silver).Weights,
			"third act weights after Silver");
		Equal(
			new HextechRarityWeights(0, 1, 1),
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(new HextechRarityWeights(9, 0, 0), all, true, 2, HextechRarityTier.Silver).Weights,
			"non-Silver zero-weight fallback");

		SequenceEqual(
			new[] { HextechRarityTier.Gold },
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(
				configured,
				[ HextechRarityTier.Silver, HextechRarityTier.Gold ],
				true,
				1,
				HextechRarityTier.Silver).Candidates,
			"enabled non-Silver candidates");
		SequenceEqual(
			new[] { HextechRarityTier.Gold, HextechRarityTier.Prismatic },
			HextechRuneSelectionCoordinator.ApplyConsecutiveSilverRule(
				configured,
				[ HextechRarityTier.Silver ],
				true,
				1,
				HextechRarityTier.Silver).Candidates,
			"strict non-Silver fallback candidates");
	}

	[HextechTest]
	private static void GoldenRerollOnlyUpgradesSilverAndGold()
	{
		Expect(
			HextechGoldenRerollRules.TryGetUpgradedRarity(
				HextechRarityTier.Silver,
				out HextechRarityTier upgradedSilver),
			"silver should be eligible for a golden reroll");
		Equal(HextechRarityTier.Gold, upgradedSilver, "silver golden reroll target");

		Expect(
			HextechGoldenRerollRules.TryGetUpgradedRarity(
				HextechRarityTier.Gold,
				out HextechRarityTier upgradedGold),
			"gold should be eligible for a golden reroll");
		Equal(HextechRarityTier.Prismatic, upgradedGold, "gold golden reroll target");

		Expect(
			!HextechGoldenRerollRules.TryGetUpgradedRarity(
				HextechRarityTier.Prismatic,
				out HextechRarityTier unchangedPrismatic),
			"prismatic should not be eligible for a golden reroll");
		Equal(HextechRarityTier.Prismatic, unchangedPrismatic, "prismatic fallback target");
	}

	[HextechTest]
	private static void GoldenRerollDebugForceIsOneShot()
	{
		HextechGoldenRerollDebug.ResetForTests();
		HextechGoldenRerollDebug.ForceCurrentOrNext(out bool activatedCurrent);
		Expect(!activatedCurrent, "force without an open selection should target the next eligible selection");
		Expect(HextechGoldenRerollDebug.IsNextEligibleForced, "next eligible selection should be forced");
		Expect(HextechGoldenRerollDebug.ConsumeNextEligibleForce(), "first eligible selection should consume the force");
		Expect(!HextechGoldenRerollDebug.ConsumeNextEligibleForce(), "force should not leak to another player or selection");
		Expect(!HextechGoldenRerollDebug.IsNextEligibleForced, "consumed force should clear");
	}

	[HextechTest]
	private static void GoldenRerollCardThemeFollowsRerolledRuneRarity()
	{
		foreach (HextechRarityTier rarity in Enum.GetValues<HextechRarityTier>())
		{
			Type runeType = HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(rarity).First();
			RelicModel rune = (RelicModel)Activator.CreateInstance(runeType)!;
			string expected = rarity switch
			{
				HextechRarityTier.Silver => "SILVER",
				HextechRarityTier.Prismatic => "PRISMATIC",
				_ => "GOLD"
			};
			Equal(
				expected,
				HextechRuneSelectionScreen.DetermineCardRarityKey(
					rune,
					HextechSelectionMetadataMode.PlayerRune),
				$"{rarity} rerolled card theme");
		}
	}

	[HextechTest]
	private static void WeightedIndexBoundarySelection()
	{
		int[] weights = [ 100, 150, 100 ];

		Equal(0, HextechRunePoolBuilder.SelectWeightedIndex(weights, 0), "first slot start");
		Equal(0, HextechRunePoolBuilder.SelectWeightedIndex(weights, 99), "first slot end");
		Equal(1, HextechRunePoolBuilder.SelectWeightedIndex(weights, 100), "second slot start");
		Equal(1, HextechRunePoolBuilder.SelectWeightedIndex(weights, 249), "second slot end");
		Equal(2, HextechRunePoolBuilder.SelectWeightedIndex(weights, 250), "third slot start");
		Equal(2, HextechRunePoolBuilder.SelectWeightedIndex(weights, 999), "overflow clamps to last slot");
	}

	[HextechTest]
	private static void RuneSelectionCandidateConstraintsMixCharactersAndLimitUpgrades()
	{
		RelicModel[] candidates = [new BerserkRune(), new BloodlettingUpgradeRune(), new JudicatorRune(), new AutomationUpgradeRune()];
		SequenceEqual(candidates, HextechRunePoolBuilder.ConstrainCandidates(candidates, false), "every slot mixes generic and character candidates");
		RelicModel[] noUpgrades = [candidates[0], candidates[2]];
		SequenceEqual(noUpgrades, HextechRunePoolBuilder.ConstrainCandidates(candidates, true), "one upgrade per offer remains enforced");
	}

	[HextechTest]
	private static void UnconfirmedRuneSelectionCancelsInsteadOfDefaultingToFirstOption()
	{
		RelicModel confirmed = new JudicatorRune();
		Equal(
			confirmed,
			HextechRuneSelectionCoordinator.RequireCompletedSelection(confirmed, "test"),
			"confirmed selection");

		try
		{
			HextechRuneSelectionCoordinator.RequireCompletedSelection<RelicModel>(null, "test");
			throw new InvalidOperationException("missing selection should cancel");
		}
		catch (OperationCanceledException ex)
		{
			Expect(ex.Message.Contains("test", StringComparison.Ordinal), "cancellation should retain diagnostic context");
		}
	}

	// 手柄焦点只跟随游戏自己的输入模式(NControllerManager 的方向导航):鼠标玩家打开界面不出焦点框;
	// 原版确认键 A 是 ui_select,本模组 Godot 按钮只认 ui_accept,选择界面必须做转换。
	[HextechTest]
	private static void SelectionUiFocusesOnlyInDirectionalNavigation()
	{
		MethodInfo defaultFocusGetter = typeof(HextechRuneSelectionScreen)
			.GetProperty(nameof(HextechRuneSelectionScreen.DefaultFocusedControl))!
			.GetMethod!;
		Expect(
			PatchProcessor.GetOriginalInstructions(defaultFocusGetter)
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.DeclaringType == typeof(HextechControllerInput) && method.Name == "get_" + nameof(HextechControllerInput.IsDirectionalNavigation)),
			"selection overlay should expose an initial focus target only in the game's directional navigation mode");

		MethodInfo selectionInput = typeof(HextechRuneSelectionScreen).GetMethod(
			nameof(HextechRuneSelectionScreen._Input),
			BindingFlags.Instance | BindingFlags.Public)
			?? throw new MissingMethodException(nameof(HextechRuneSelectionScreen), nameof(HextechRuneSelectionScreen._Input));
		Expect(
			PatchProcessor.GetOriginalInstructions(selectionInput)
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.DeclaringType == typeof(HextechControllerInput) && method.Name == nameof(HextechControllerInput.TryTranslateSelectToAccept)),
			"selection overlay should translate the controller confirm (ui_select) for its Godot buttons");

		MethodInfo openConfig = typeof(HextechRuneConfigMenuHooks).GetMethod(
			"OpenOverlay",
			BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(HextechRuneConfigMenuHooks), "OpenOverlay");
		MethodInfo[] configCalls = PatchProcessor.GetOriginalInstructions(openConfig)
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Expect(
			configCalls.Any(static method => method.DeclaringType == typeof(HextechControllerOverlay) && method.Name == "set_InitialFocus"),
			"config overlay should register a deferred controller focus target");
		Expect(
			configCalls.All(static method => method.Name != nameof(Control.GrabFocus)),
			"opening config with mouse should not explicitly focus an option");
	}

	[HextechTest]
	private static void PlayerRuneSelectionUsesPendingSlotUntilConfirmation()
	{
		Expect(
			HextechRuneSelectionScreen.ShouldUsePlayerRuneConfirmation(HextechSelectionMetadataMode.PlayerRune, enemyOnly: false, selfPickMode: false, preferenceEnabled: true),
			"normal player rune selection uses confirmation when the preference is on");
		Expect(
			!HextechRuneSelectionScreen.ShouldUsePlayerRuneConfirmation(HextechSelectionMetadataMode.PlayerRune, enemyOnly: false, selfPickMode: false, preferenceEnabled: false),
			"preference off keeps one-click selection");
		Expect(
			!HextechRuneSelectionScreen.ShouldUsePlayerRuneConfirmation(HextechSelectionMetadataMode.PlayerRune, enemyOnly: false, selfPickMode: true, preferenceEnabled: true),
			"self-pick already confirms; its confirm must not be swallowed as a slotless pending pick");
		Expect(
			!HextechRuneSelectionScreen.ShouldUsePlayerRuneConfirmation(HextechSelectionMetadataMode.Forge, enemyOnly: false, selfPickMode: false, preferenceEnabled: true),
			"forge selection should remain immediate");
		Expect(
			!HextechRuneSelectionScreen.ShouldUsePlayerRuneConfirmation(HextechSelectionMetadataMode.PlayerRune, enemyOnly: true, selfPickMode: false, preferenceEnabled: true),
			"enemy-only selection should keep its existing confirmation");
		Equal(
			1,
			HextechRuneSelectionScreen.ResolvePendingPlayerRuneSlot(slotIndex: 1, slotCount: 3),
			"card click should record its slot without completing the selection");
		Equal(
			0,
			HextechRuneSelectionScreen.ResolvePendingPlayerRuneSlot(slotIndex: 0, slotCount: 3),
			"a second card click should replace the pending slot");
		Equal<int?>(null, HextechRuneSelectionScreen.ResolvePendingPlayerRuneSlot(slotIndex: 3, slotCount: 3), "out-of-range slot is not pending");
	}

	[HextechTest]
	private static void PlayerRuneRerollClearsOnlyCurrentPendingSlot()
	{
		Equal<int?>(null, HextechRuneSelectionScreen.ResolvePendingSlotAfterReroll(1, 1), "rerolling the pending slot should clear it");
		Equal<int?>(1, HextechRuneSelectionScreen.ResolvePendingSlotAfterReroll(1, 0), "rerolling another slot should preserve the pending slot");
		Equal<int?>(null, HextechRuneSelectionScreen.ResolvePendingSlotAfterReroll(null, 0), "rerolling without a pending slot should stay empty");
	}

	[HextechTest]
	private static void EnemyHexRemovalCanBeUndoneWithoutConsumingTheSlot()
	{
		List<MonsterHexKind?> current = [ MonsterHexKind.EightPennyGate ];
		List<MonsterHexKind?> beforeRemoval = [ null ];
		Expect(
			HextechRuneSelectionScreen.ToggleEnemyHexRemoval(current, beforeRemoval, 0),
			"an active enemy hex should be removable");
		Equal<MonsterHexKind?>(null, current[0], "removed enemy hex slot");
		Equal<MonsterHexKind?>(MonsterHexKind.EightPennyGate, beforeRemoval[0], "removed enemy hex undo snapshot");

		Expect(
			HextechRuneSelectionScreen.ToggleEnemyHexRemoval(current, beforeRemoval, 0),
			"a locally removed enemy hex should be restorable");
		Equal<MonsterHexKind?>(MonsterHexKind.EightPennyGate, current[0], "restored enemy hex slot");
		Equal<MonsterHexKind?>(null, beforeRemoval[0], "consumed enemy hex undo snapshot");
		Expect(
			!HextechRuneSelectionScreen.ToggleEnemyHexRemoval([ null ], [ null ], 0),
			"a remotely removed slot without an undo snapshot should stay disabled");
	}

	[HextechTest]
	private static void EnemyHexActionButtonsSwapToUndoAndResolveStateTextures()
	{
		Expect(
			!HextechRuneSelectionScreen.ShouldShowEnemyHexUndoButton(MonsterHexKind.EightPennyGate),
			"active enemy hexes should show reroll and remove actions");
		Expect(
			HextechRuneSelectionScreen.ShouldShowEnemyHexUndoButton(null),
			"removed enemy hexes should replace both actions with undo");
		Equal(
			"res://HextechRunes/images/ui/hextechUndoButtonDisabled.png",
			HextechRuneSelectionScreen.ResolveEnemyHexRemovalButtonTexture(undo: true, disabled: true, pressed: false, highlighted: false),
			"disabled undo texture");
		Equal(
			"res://HextechRunes/images/ui/hextechUndoButtonPressed.png",
			HextechRuneSelectionScreen.ResolveEnemyHexRemovalButtonTexture(undo: true, disabled: false, pressed: true, highlighted: true),
			"pressed undo texture");
		Equal(
			"res://HextechRunes/images/ui/hextechRemoveButtonHover.png",
			HextechRuneSelectionScreen.ResolveEnemyHexRemovalButtonTexture(undo: false, disabled: false, pressed: false, highlighted: true),
			"hovered remove texture");
	}

	[HextechTest]
	private static void CollapsedEnemyHexPanelFollowsTopBarButtonLifecycle()
	{
		MethodInfo ensureButton = typeof(HextechEnemyHexCollapseView).GetMethod(
			"EnsureButton",
			BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(HextechEnemyHexCollapseView), "EnsureButton");
		Expect(
			PatchProcessor.GetOriginalInstructions(ensureButton)
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.Name == "add_TreeExiting"),
			"collapsed enemy hex button should own a tree-exit cleanup hook");

		MethodInfo cleanup = typeof(HextechEnemyHexCollapseView).GetMethod(
			"OnButtonTreeExiting",
			BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(HextechEnemyHexCollapseView), "OnButtonTreeExiting");
		Expect(
			PatchProcessor.GetOriginalInstructions(cleanup)
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Any(static method => method.Name == "QueueFreeIfValid"),
			"top bar exit should release the globally hosted collapsed enemy hex panel");
	}

	[HextechTest]
	private static void DestructivePickupRunesAreExcludedFromRandomRewards()
	{
		Type[] destructiveTypes =
		[
			typeof(TransmuteChaosRune),
			typeof(TransmutePrismaticRune),
			typeof(TransmuteGoldRune),
			typeof(PandorasBoxRune)
		];
		foreach (Type runeType in destructiveTypes)
		{
			Expect(
				HextechRuneGrantHelper.IsDestructiveRandomRewardRuneType(runeType),
				$"{runeType.Name} must not be generated as a random reward");
		}

		Expect(
			!HextechRuneGrantHelper.IsDestructiveRandomRewardRuneType(typeof(JudicatorRune)),
			"ordinary runes should remain eligible for random rewards");
	}

	[HextechTest]
	private static void RuneSelectionJournalRoundTripsInStableOrder()
	{
		HextechRuneSelectionJournalState state = new();
		ModelId later = new("HEXTECH_TEST", "LATER");
		ModelId earlier = new("HEXTECH_TEST", "EARLIER");
		state.RecordSelected(2, 1, 99, later);
		state.RecordSelected(0, 0, 7, earlier);
		state.MarkApplied(2, 1, 99, later);
		Expect(state.HasEntriesForAct(0), "journal should report a pending operation for act zero");
		Expect(state.HasEntriesForAct(2), "journal should retain completed operations until the run resets");
		Expect(!state.HasEntriesForAct(1), "journal should not report an unrelated act");

		string json = state.Serialize();
		Expect(
			json.IndexOf("EARLIER", StringComparison.Ordinal)
				< json.IndexOf("LATER", StringComparison.Ordinal),
			"journal JSON should sort operations by act, ordinal and player id");

		HextechRuneSelectionJournalState restored = new();
		restored.Restore(json);
		Expect(
			restored.TryGet(0, 0, 7, out HextechRuneSelectionJournalEntry earlierEntry),
			"earlier journal entry should restore");
		Equal(earlier, earlierEntry.SelectedId, "restored earlier selected ModelId");
		Equal(false, earlierEntry.Applied, "restored earlier applied state");
		Expect(
			restored.TryGet(2, 1, 99, out HextechRuneSelectionJournalEntry laterEntry),
			"later journal entry should restore");
		Equal(later, laterEntry.SelectedId, "restored later selected ModelId");
		Equal(true, laterEntry.Applied, "restored later applied state");
	}

	[HextechTest]
	private static void RuneSelectionJournalRejectsConflictingSelections()
	{
		HextechRuneSelectionJournalState state = new();
		ModelId selected = new("HEXTECH_TEST", "SELECTED");
		ModelId conflicting = new("HEXTECH_TEST", "CONFLICTING");

		Expect(state.RecordSelected(1, 2, 33, selected), "first journal selection should be recorded");
		Expect(!state.RecordSelected(1, 2, 33, selected), "same journal selection should be idempotent");
		ExpectThrows<InvalidOperationException>(
			() => state.RecordSelected(1, 2, 33, conflicting),
			"same operation must reject a different selected ModelId");
		Expect(state.MarkApplied(1, 2, 33, selected), "first applied transition should be recorded");
		Expect(!state.MarkApplied(1, 2, 33, selected), "applied transition should be idempotent");
		ExpectThrows<InvalidOperationException>(
			() => state.MarkApplied(1, 2, 33, conflicting),
			"applied transition must reject a different ModelId");
	}

	[HextechTest]
	private static void MonsterHexRollerBuildActPoolExcludesKnownAndFallsBack()
	{
		(HextechRarityTier rarity, IReadOnlyList<MonsterHexKind> rarityPool) = GetMonsterHexPoolWithMinimum(2);

		IReadOnlyList<MonsterHexKind> filteredPool = HextechMonsterHexRoller.BuildActPool(
			rarity,
			rarityPool.Take(rarityPool.Count - 1));
		SequenceEqual(new[] { rarityPool[^1] }, filteredPool, "act monster hex pool should exclude known hexes");

		IReadOnlyList<MonsterHexKind> fallbackPool = HextechMonsterHexRoller.BuildActPool(rarity, rarityPool);
		SequenceEqual(rarityPool, fallbackPool, "act monster hex pool should fall back to full rarity pool when exhausted");
	}

	[HextechTest]
	private static void MonsterHexPoolDropsSinglePlayerOnlyHexesInMultiplayer()
	{
		HextechRarityTier rarity = MonsterHexCatalog.GetMonsterHexRarity(MonsterHexKind.TestSubject);
		Expect(MonsterHexCatalog.IsSinglePlayerOnly(MonsterHexKind.TestSubject), "test subject should be single-player only");
		Expect(
			HextechMonsterHexRoller.GetConfiguredRarityPool(rarity, null, networkMultiplayer: false).Contains(MonsterHexKind.TestSubject),
			"single-player pool should keep test subject");
		Expect(
			!HextechMonsterHexRoller.GetConfiguredRarityPool(rarity, null, networkMultiplayer: true).Contains(MonsterHexKind.TestSubject),
			"multiplayer pool should drop test subject");
	}

	[HextechTest]
	private static void MonsterHexRollerResolveNewHexesPreservesPrimaryAndAvoidsDuplicates()
	{
		MonsterHexKind[] kinds = Enum.GetValues<MonsterHexKind>()
			.Take(4)
			.ToArray();
		Expect(kinds.Length >= 4, "monster hex enum should have at least four values for resolution test");

		IReadOnlyList<MonsterHexKind> resolved = HextechMonsterHexRoller.ResolveNewMonsterHexes(
			newEnemyHexCount: 3,
			previousHexes: [ kinds[0] ],
			primaryMonsterHex: kinds[1],
			chooseExtraHex: (excludedHexes, _) =>
			{
				foreach (MonsterHexKind kind in kinds)
				{
					if (!excludedHexes.Contains(kind))
					{
						return kind;
					}
				}

				return null;
			});

		SequenceEqual(new[] { kinds[1], kinds[2], kinds[3] }, resolved, "resolved new monster hexes");
		Expect(HextechMonsterHexRoller.ResolveNewMonsterHexes(0, [ kinds[0] ], kinds[1], (_, _) => kinds[2]).Count == 0, "zero enemy hex count should resolve none");
	}

	[HextechTest]
	private static void MonsterHexRollerBuildRerollPoolHonorsIconExclusionsThenFallbacks()
	{
		(HextechRarityTier rarity, IReadOnlyList<MonsterHexKind> rarityPool) = GetMonsterHexPoolWithMinimum(4);
		MonsterHexKind currentHex = rarityPool[0];
		MonsterHexKind knownHex = rarityPool[1];
		MonsterHexKind iconBlockedHex = rarityPool[2];
		MonsterHexKind allowedHex = rarityPool[3];

		IReadOnlyList<MonsterHexKind> rerollPool = HextechMonsterHexRoller.BuildRerollPool(
			rarity,
			[ knownHex ],
			currentHex,
			new HashSet<ModelId> { TestMonsterHexIconId(iconBlockedHex) },
			TestMonsterHexIconId);
		Expect(!rerollPool.Contains(currentHex), "reroll pool should exclude current hex");
		Expect(!rerollPool.Contains(knownHex), "reroll pool should exclude known hexes");
		Expect(!rerollPool.Contains(iconBlockedHex), "reroll pool should exclude icon-blocked hexes while alternatives remain");
		Expect(rerollPool.Contains(allowedHex), "reroll pool should keep unblocked alternatives");

		IReadOnlyList<MonsterHexKind> fallbackPool = HextechMonsterHexRoller.BuildRerollPool(
			rarity,
			rarityPool.Skip(1),
			currentHex,
			new HashSet<ModelId>(),
			TestMonsterHexIconId);
		SequenceEqual(rarityPool.Where(hex => hex != currentHex), fallbackPool, "reroll pool should fall back to non-current rarity pool when known exclusions exhaust it");
	}
}
