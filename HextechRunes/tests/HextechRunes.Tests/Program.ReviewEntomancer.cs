using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes.Tests;

// 敌方科学狂人重做为"升级：蜂群术士"（原版人体蜂房）；愈战愈勇/大法师的免费牌挑选改为原版木乃伊之手分级。
internal static partial class Program
{
	[HextechTest]
	private static void EnemyEntomancerUsesItsOwnIconCarrier()
	{
		MonsterHexRegistration row = HextechMonsterHexRegistry.Registrations.Single(registration => registration.Kind == MonsterHexKind.MadScientist);
		Equal(typeof(EntomancerHex), row.IconRelicType, "enemy Entomancer shows its own icon carrier");
		Expect(!HextechPlayerRuneRegistry.Registrations.Any(registration => registration.Type == typeof(EntomancerHex)),
			"the enemy carrier never enters the player pool");

		EntomancerHex carrier = new();
		Expect(HextechCatalog.IsHextechEnemyHexIconRelic(carrier), "the carrier is recognised as an enemy hex icon relic");
		Equal("res://HextechRunes/images/relics/madScientistRune.png", HextechAssets.TryGetCustomRelicIconPath(carrier),
			"the carrier borrows the Mad Scientist icon for now");
		Equal("res://HextechRunes/images/relics/madScientistRune.png", HextechAssets.TryGetCustomRelicIconPath(new MadScientistRune()),
			"player icon path is unchanged");
	}

	[HextechTest]
	private static void EnemyEntomancerGrantsVanillaPersonalHiveInsteadOfCustomDazed()
	{
		IReadOnlyList<HextechEnemyHexEffect> effects = (IReadOnlyList<HextechEnemyHexEffect>)AccessTools.Field(typeof(HextechEnemyHexEffects), "OrderedEffects").GetValue(null)!;
		HextechEnemyHexEffect effect = effects.Single(candidate => candidate.Kind == MonsterHexKind.MadScientist);
		Equal(typeof(MadScientistEnemyHex), effect.GetType(), "the MadScientist kind is handled by the Entomancer effect");
		Expect(effect is IHextechEnemyMaxHpCoefficientProvider, "the max HP reduction is kept");
		Expect(!new PersonalHivePower().ShouldScaleInMultiplayer,
			"vanilla does not scale Personal Hive by player count, so the enemy keeps exactly 1 stack");

		SetEqual([typeof(PersonalHivePower)], MonsterHexCatalog.GetEnemyHexPowerHoverTipTypes(MonsterHexKind.MadScientist),
			"the enemy hex hover shows Personal Hive");
		Expect(!TwilightVeilRune.IsMirrorablePower(new PersonalHivePower()),
			"Personal Hive stays out of Twilight Veil mirroring");
	}

	[HextechTest]
	private static void FreeCardPickerSkipsCardsAlreadyFreedByModifiers()
	{
		CardModel alreadyFree = CreateMutableTestModel<StrikeIronclad>();
		alreadyFree.EnergyCost.SetThisTurn(0);
		CardModel normal = CreateMutableTestModel<StrikeIronclad>();
		Expect(HextechFreeCardPicker.HasBaseCost(alreadyFree) && !alreadyFree.CostsEnergyOrStars(includeGlobalModifiers: true),
			"fixture: the free card still has a base cost but no longer costs anything");

		List<string> tiers = [];
		CardModel? picked = HextechFreeCardPicker.Pick(
			[alreadyFree, normal],
			(candidates, tier) =>
			{
				tiers.Add(tier);
				return candidates.Contains(alreadyFree) ? alreadyFree : candidates[0];
			});
		Equal(normal, picked, "the real card picker never wastes the free effect on an already free card");
		SetEqual([HextechFreeCardPicker.BaseCostTier], tiers, "the first tier already has a candidate");

		// 全局修正（三头犬、奇巧许可等）只在战斗 Hook 里生效，这里用谓词模拟"基础费 > 0 但最终费用为 0"。
		string globallyFree = "globally-free", ordinary = "ordinary", zeroBase = "zero-base";
		Func<string, bool> hasBaseCost = card => card != zeroBase;
		Func<string, bool> costsAfterGlobal = card => card == ordinary;
		List<IReadOnlyList<string>> offered = [];
		string? pickedName = HextechFreeCardPicker.Pick(
			[globallyFree, zeroBase, ordinary],
			hasBaseCost,
			costsAfterGlobal,
			(candidates, tier) =>
			{
				offered.Add(candidates);
				return candidates[0];
			});
		Equal(ordinary, pickedName, "a card made free by a global modifier is skipped while a costed card exists");
		Equal(1, offered.Count, "only the first non-empty tier is offered");
		SetEqual([ordinary], offered[0], "the first tier only holds cards that still cost something");
	}

	[HextechTest]
	private static void FreeCardPickerFallsBackLikeMummifiedHandWhenEverythingIsFree()
	{
		string globallyFree = "globally-free", zeroBase = "zero-base";
		Func<string, bool> hasBaseCost = card => card != zeroBase;
		Func<string, bool> costsNothing = static _ => false;
		List<string> tiers = [];
		string? picked = HextechFreeCardPicker.Pick(
			[zeroBase, globallyFree],
			hasBaseCost,
			costsNothing,
			(candidates, tier) =>
			{
				tiers.Add(tier);
				return candidates[0];
			});
		Equal(globallyFree, picked, "with nothing left to pay for, a card with a base cost is still chosen (vanilla order)");
		SetEqual([HextechFreeCardPicker.BaseAnyTier], tiers, "empty tiers are skipped without consuming a pick");

		tiers.Clear();
		picked = HextechFreeCardPicker.Pick(
			[zeroBase],
			hasBaseCost,
			costsNothing,
			(candidates, tier) =>
			{
				tiers.Add(tier);
				return candidates[0];
			});
		Equal(zeroBase, picked, "the last tier is any hand card");
		SetEqual([HextechFreeCardPicker.AnyTier], tiers, "only the any tier was offered");

		Equal<string?>(null, HextechFreeCardPicker.Pick(Array.Empty<string>(), hasBaseCost, costsNothing, static (candidates, _) => candidates[0]),
			"an empty hand picks nothing");

		CardModel whirlwind = CreateMutableTestModel<Whirlwind>();
		Expect(!whirlwind.CostsEnergyOrStars(includeGlobalModifiers: true), "X-cost cards never enter the first two tiers (vanilla CostsEnergyOrStars)");
		CardModel? xPicked = HextechFreeCardPicker.Pick(
			[whirlwind],
			(candidates, tier) =>
			{
				tiers.Add(tier);
				return candidates[0];
			});
		Equal(whirlwind, xPicked, "an X-cost card alone is still picked by a fallback tier");
	}
}
