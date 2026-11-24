using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void GuaranteedFirstPickPlacementAndRerollRarity()
	{
		Action[] restore =
		[
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneRegistrations"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "AssetModIdsByType"),
			CaptureExternalRegistryVersionRestore()
		];
		try
		{
			Equal(PlayerRuneFlags.GuaranteedFirstPick, HextechRunesInterop.ParsePlayerRuneFlags(" guaranteedfirstpick "), "flag is parsed by name");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestSilverRuneA), HextechRarityTier.Silver),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestSilverRuneB), HextechRarityTier.Silver),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestSilverRuneC), HextechRarityTier.Silver),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestPrismaticRune), HextechRarityTier.Prismatic, PlayerRuneFlags.GuaranteedFirstPick),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestGoldRune), HextechRarityTier.Gold, PlayerRuneFlags.GuaranteedFirstPick),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestGoldRegularRune), HextechRarityTier.Gold),
				"Compatibility.Guaranteed");

			RelicModel a = new GuaranteedTestSilverRuneA();
			RelicModel b = new GuaranteedTestSilverRuneB();
			RelicModel c = new GuaranteedTestSilverRuneC();
			RelicModel prismatic = new GuaranteedTestPrismaticRune();
			RelicModel gold = new GuaranteedTestGoldRune();
			Expect(HextechRunePoolBuilder.IsGuaranteedFirstPickRune(prismatic), "flagged rune is recognised");
			Expect(!HextechRunePoolBuilder.IsGuaranteedFirstPickRune(a), "unflagged rune is not guaranteed");

			SequenceEqual(new[] { a, b, prismatic }, HextechRunePoolBuilder.PlaceGuaranteedOption([a, b, c], prismatic), "full selection replaces the last slot");
			SequenceEqual(new[] { a, prismatic }, HextechRunePoolBuilder.PlaceGuaranteedOption([a], prismatic), "short selection appends");
			Equal(HextechRarityTier.Silver, HextechRunePoolBuilder.GetRarityForOptions(HextechRunePoolBuilder.PlaceGuaranteedOption([a, b, c], prismatic)),
				"selection rarity still comes from the act's first option");

			List<RelicModel> withGuaranteed = [a, b, prismatic];
			Expect(ReferenceEquals(withGuaranteed, HextechRunePoolBuilder.ApplyGuaranteedFirstPick(null!, null!, 0, null, withGuaranteed)),
				"a selection that already holds a guaranteed rune is left untouched");

			RelicModel upgradedRegular = new GuaranteedTestGoldRegularRune();
			Equal(HextechRarityTier.Silver, HextechRunePoolBuilder.GetRerollRarity([a, b, prismatic], 2, HextechRarityTier.Silver), "rerolling the guaranteed slot returns to the act rarity");
			Equal(HextechRarityTier.Silver, HextechRunePoolBuilder.GetRerollRarity([upgradedRegular, b, prismatic], 2, HextechRarityTier.Silver),
				"a golden-upgraded first slot does not change the guaranteed slot's reroll rarity");
			Equal(HextechRarityTier.Silver, HextechRunePoolBuilder.GetRerollRarity([a, upgradedRegular, prismatic], 2, HextechRarityTier.Silver),
				"a golden-upgraded second slot does not change it either");
			Equal(HextechRarityTier.Gold, HextechRunePoolBuilder.GetRerollRarity([upgradedRegular, b, prismatic], 0, HextechRarityTier.Silver), "regular slots keep their own rarity");
			Equal(HextechRarityTier.Silver, HextechRunePoolBuilder.GetRerollRarity([prismatic], 0, HextechRarityTier.Silver), "a lone guaranteed option still rerolls from the act pool");
			Equal(HextechRarityTier.Prismatic, HextechRunePoolBuilder.GetRerollRarity([a, prismatic], 1, null), "without a recorded act rarity the slot keeps its own rarity");

			HashSet<ModelId> seen = [prismatic.CanonicalId()];
			SequenceEqual(new[] { gold }, HextechRunePoolBuilder.SelectGuaranteedCandidates([prismatic, gold], seen, upgradeAlreadySelected: false), "unseen guaranteed runes are preferred");
			seen.Add(gold.CanonicalId());
			SequenceEqual(new[] { prismatic, gold }, HextechRunePoolBuilder.SelectGuaranteedCandidates([prismatic, gold], seen, upgradeAlreadySelected: false), "all seen falls back to the full set");
			Equal(0, HextechRunePoolBuilder.SelectGuaranteedCandidates([], seen, upgradeAlreadySelected: false).Count, "no available guaranteed rune skips the guarantee");
			RelicModel upgrade = new GuaranteedTestUpgradeRune();
			SequenceEqual(new[] { gold }, HextechRunePoolBuilder.SelectGuaranteedCandidates([upgrade, gold], null, upgradeAlreadySelected: true),
				"the one-upgrade-per-selection rule still applies");
		}
		finally
		{
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	[HextechTest]
	private static void ExternalRuneCharacterGetsCharacterWeight()
	{
		Action[] restore =
		[
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneRegistrations"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "AssetModIdsByType"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneCharacterTypesByType"),
			CaptureExternalRegistryVersionRestore(),
			SuppressCompatibilityWarnings("external-content.rune-character-conflict")
		];
		try
		{
			Expect(HextechRunesInterop.ApiVersion >= 2, "guaranteed pick and rune character raise the contract version");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestSilverRuneA), HextechRarityTier.Silver),
				"Compatibility.Guaranteed");
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestSilverRuneB), HextechRarityTier.Silver),
				"Compatibility.Guaranteed");
			ModelId silent = ModelDb.GetId<Silent>();
			ModelId ironclad = ModelDb.GetId<Ironclad>();
			Expect(!HextechExternalContentRegistry.HasPlayerRunesForCharacter(silent), "no external character runes before registration");

			int version = HextechExternalContentRegistry.Version;
			HextechRunesInterop.SetPlayerRuneCharacter(typeof(GuaranteedTestSilverRuneA), typeof(Silent));
			Expect(HextechExternalContentRegistry.Version > version, "character ownership changes weights, so it bumps the registry version");
			HextechRunesInterop.SetPlayerRuneCharacter(typeof(GuaranteedTestSilverRuneA), typeof(Ironclad));
			Expect(HextechExternalContentRegistry.HasPlayerRunesForCharacter(silent), "registered character has character runes");
			Expect(!HextechExternalContentRegistry.HasPlayerRunesForCharacter(ironclad), "first character registration wins");

			RelicModel owned = new GuaranteedTestSilverRuneA();
			RelicModel generic = new GuaranteedTestSilverRuneB();
			Expect(HextechRunePoolBuilder.IsRuneForCharacter(owned, silent), "external rune counts as the registered character's rune");
			Expect(!HextechRunePoolBuilder.IsRuneForCharacter(owned, ironclad), "other characters do not match");
			Expect(!HextechRunePoolBuilder.IsRuneForCharacter(generic, silent), "unmapped external rune stays generic");
			Expect(HextechRunePoolBuilder.IsRuneForCharacter(new BerserkRune(), ironclad), "vanilla character pools still match by character id");
			List<int> weights = HextechRunePoolBuilder.BuildSelectionWeights([owned, generic], new Dictionary<string, int>(), false, silent,
				HextechWeightedRuneOptions.InitialCharacterWeightPercent, out _);
			SequenceEqual(new[] { 15000, 10000 }, weights, "mapped external rune receives the 150% character weight");

			ExpectThrows<ArgumentException>(
				() => HextechRunesInterop.SetPlayerRuneCharacter(typeof(GuaranteedTestSilverRuneB), typeof(string)),
				"character type must be a CharacterModel");
			ExpectThrows<ArgumentException>(
				() => HextechRunesInterop.SetPlayerRuneCharacter(typeof(string), typeof(Silent)),
				"rune type must be a RelicModel");
			Equal(null, HextechExternalContentRegistry.GetPlayerRuneCharacterId(generic.CanonicalId()), "invalid registrations leave no state");
		}
		finally
		{
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	// 走完整入口：真实的过滤（角色可用、已拥有、配置禁用）、首次/后续选择判定、稳定挑选，以及模组角色的加权识别。
	[HextechTest]
	private static void GuaranteedFirstPickFullEntryAndModCharacterWeight()
	{
		Action[] restore =
		[
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneRegistrations"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "AssetModIdsByType"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneAvailabilityByType"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "PlayerRuneCharacterTypesByType"),
			CaptureExternalRegistryVersionRestore()
		];
		using IDisposable models = InjectMissingModels(
			typeof(GuaranteedTestSilverRuneA),
			typeof(GuaranteedTestSilverRuneB),
			typeof(GuaranteedTestSilverRuneC),
			typeof(GuaranteedTestPrismaticRune),
			typeof(GuaranteedTestGoldRune),
			typeof(GuaranteedTestModCharacter),
			typeof(GuaranteedTestOtherCharacter));
		try
		{
			static bool ForModCharacter(Player player) => player.Character is GuaranteedTestModCharacter;
			foreach (Type type in new[] { typeof(GuaranteedTestSilverRuneA), typeof(GuaranteedTestSilverRuneB), typeof(GuaranteedTestSilverRuneC) })
			{
				HextechExternalContentRegistry.RegisterPlayerRune(new PlayerRuneRegistration(type, HextechRarityTier.Silver), "Compatibility.Guaranteed");
			}
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestPrismaticRune), HextechRarityTier.Prismatic, PlayerRuneFlags.GuaranteedFirstPick),
				"Compatibility.Guaranteed",
				ForModCharacter);
			HextechExternalContentRegistry.RegisterPlayerRune(
				new PlayerRuneRegistration(typeof(GuaranteedTestGoldRune), HextechRarityTier.Gold, PlayerRuneFlags.GuaranteedFirstPick),
				"Compatibility.Guaranteed",
				ForModCharacter);

			CharacterModel modCharacter = ModelDb.GetById<CharacterModel>(ModelDb.GetId(typeof(GuaranteedTestModCharacter)));
			(RunState run, Player player) = CreateGuaranteedPickFixture(modCharacter);
			ModelId prismaticId = ModelDb.GetId(typeof(GuaranteedTestPrismaticRune));
			ModelId goldId = ModelDb.GetId(typeof(GuaranteedTestGoldRune));
			List<RelicModel> options = new HextechWeightedRuneOptions(
				new[] { typeof(GuaranteedTestSilverRuneA), typeof(GuaranteedTestSilverRuneB), typeof(GuaranteedTestSilverRuneC) }
					.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)).ToMutable()),
				170);
			MethodInfo stagePick = AccessTools.Method(typeof(HextechRuneSelectionCoordinator), "ApplyStageGuaranteedPick")
				?? throw new InvalidOperationException("stage guarantee entry should exist");
			List<RelicModel> StagePick(int choiceOrdinal, IReadOnlySet<ModelId> seen) =>
				(List<RelicModel>)stagePick.Invoke(null, [ run, player, 0, choiceOrdinal, seen, options ])!;

			List<RelicModel> first = StagePick(0, new HashSet<ModelId>());
			Equal(3, first.Count, "guarantee keeps three options");
			SequenceEqual(options.Take(2).Select(static relic => relic.CanonicalId()), first.Take(2).Select(static relic => relic.CanonicalId()), "first two act options are kept");
			ModelId pickedId = first[2].CanonicalId();
			Expect(pickedId == prismaticId || pickedId == goldId, "last slot holds an available guaranteed rune regardless of act rarity");
			Equal(170, HextechWeightedRuneOptions.GetWeight(first), "character weight progress is carried through");
			Equal(pickedId, StagePick(0, new HashSet<ModelId>()) [2].CanonicalId(), "the pick is stable for the same stage and player");
			Expect(ReferenceEquals(options, StagePick(1, new HashSet<ModelId>())), "later choices of the act get no guarantee");

			ModelId otherId = pickedId == prismaticId ? goldId : prismaticId;
			Equal(otherId, StagePick(0, new HashSet<ModelId> { pickedId })[2].CanonicalId(), "unseen guaranteed runes are preferred");

			List<RelicModel> relics = (List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(player)!;
			relics.Add(ModelDb.GetById<RelicModel>(prismaticId).ToMutable());
			Equal(goldId, StagePick(0, new HashSet<ModelId>())[2].CanonicalId(), "owned guaranteed runes are skipped");

			// 让配置读取走内存，不碰 Godot 文件接口；禁用集合来自本局快照。
			using ModEnabledOverride loadedConfig = new();
			HextechMayhemModifier modifier = CreateMutableTestModel<HextechMayhemModifier>();
			AccessTools.Field(typeof(ModifierModel), "_runState").SetValue(modifier, run);
			modifier.SetPlayerRuneConfigDisabledIdsSnapshot([ goldId.Entry ], "guaranteed pick test");
			SetAutoProperty(run, nameof(RunState.Modifiers), new List<ModifierModel> { modifier });
			Expect(ReferenceEquals(options, StagePick(0, new HashSet<ModelId>())), "config-disabled and owned runes leave nothing to guarantee");
			SetAutoProperty(run, nameof(RunState.Modifiers), new List<ModifierModel>());
			relics.Clear();

			(RunState vanillaRun, Player vanillaPlayer) = CreateGuaranteedPickFixture(ModelDb.GetById<CharacterModel>(ModelDb.GetId(typeof(GuaranteedTestOtherCharacter))));
			Expect(ReferenceEquals(options, HextechRunePoolBuilder.ApplyGuaranteedFirstPick(vanillaPlayer, vanillaRun, 0, null, options)),
				"availability predicates still decide which characters get the guarantee");

			Equal(null, HextechRunePoolBuilder.GetRuneCharacterId(player), "mod characters without registered runes stay unweighted");
			Equal(150, HextechRunePoolBuilder.AdvanceCharacterWeight(player, 150, options[0]), "unweighted characters do not advance weight progress");
			HextechRunesInterop.SetPlayerRuneCharacter(typeof(GuaranteedTestSilverRuneA), typeof(GuaranteedTestModCharacter));
			Equal(modCharacter.Id, HextechRunePoolBuilder.GetRuneCharacterId(player), "registered mod character takes part in character weighting");
			Equal(140, HextechRunePoolBuilder.AdvanceCharacterWeight(player, 150, options[0]), "drawing the mod character's rune lowers the weight");
			Equal(160, HextechRunePoolBuilder.AdvanceCharacterWeight(player, 150, options[1]), "drawing a generic rune raises the weight");
			List<int> weights = HextechRunePoolBuilder.BuildSelectionWeights(options.Take(2).ToList(), new Dictionary<string, int>(), false,
				HextechRunePoolBuilder.GetRuneCharacterId(player), HextechWeightedRuneOptions.InitialCharacterWeightPercent, out _);
			SequenceEqual(new[] { 15000, 10000 }, weights, "the mod character's rune gets the 150% weight");
		}
		finally
		{
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	private static (RunState Run, Player Player) CreateGuaranteedPickFixture(CharacterModel character)
	{
		RunState run = (RunState)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		Player player = CreateOrdinalTestPlayer(1);
		AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { player });
		FieldInfo history = AccessTools.Field(typeof(RunState), "_mapPointHistory");
		history.SetValue(run, Activator.CreateInstance(history.FieldType));
		AccessTools.Property(typeof(RunState), "Rng").SetValue(run, new RunRngSet("GUARANTEED-FIRST-PICK"));
		SetAutoProperty(run, nameof(RunState.Modifiers), new List<ModifierModel>());
		AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
		AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
		SetAutoProperty(player, nameof(Player.Character), character);
		return (run, player);
	}

	private sealed class GuaranteedTestModCharacter : GuaranteedTestCharacterBase;

	private sealed class GuaranteedTestOtherCharacter : GuaranteedTestCharacterBase;

	private abstract class GuaranteedTestCharacterBase : CharacterModel
	{
		public override Color NameColor => Colors.White;
		public override CharacterGender Gender => default;
		protected override CharacterModel UnlocksAfterRunAs => this;
		public override int StartingHp => 1;
		public override int StartingGold => 0;
		public override CardPoolModel CardPool => throw new NotSupportedException();
		public override RelicPoolModel RelicPool => throw new NotSupportedException();
		public override PotionPoolModel PotionPool => throw new NotSupportedException();
		public override IEnumerable<CardModel> StartingDeck => [];
		public override IReadOnlyList<RelicModel> StartingRelics => [];
		public override float AttackAnimDelay => 0;
		public override float CastAnimDelay => 0;
		public override List<string> GetArchitectAttackVfx() => [];
	}

	private sealed class GuaranteedTestSilverRuneA : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestSilverRuneB : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestSilverRuneC : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestPrismaticRune : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestGoldRune : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestGoldRegularRune : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class GuaranteedTestUpgradeRune : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}
}
