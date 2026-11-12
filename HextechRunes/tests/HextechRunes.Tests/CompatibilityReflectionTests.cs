using System.Reflection;
using System.Runtime.CompilerServices;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void ColorDiscoveryRewardUsesPublicCardsAndMissingSpecialFieldKeepsOriginal()
	{
		PropertyInfo? cardsProperty = typeof(CardReward).GetProperty(
			nameof(CardReward.Cards),
			BindingFlags.Instance | BindingFlags.Public);
		Expect(cardsProperty?.GetMethod?.IsPublic == true, "CardReward.Cards must remain a public reward contract.");
		Expect(
			typeof(ColorDiscoveryCardReward).GetField("CardRewardCardsField", BindingFlags.Static | BindingFlags.NonPublic) == null,
			"Color Discovery must not cache CardReward._cards.");

		Equal<CardModel?>(
			null,
			ColorDiscoveryCardReward.TryGetRestoredSpecialCard(restoredReward: null, cardField: null),
			"missing SpecialCardReward field fallback");

		Reward original = (Reward)RuntimeHelpers.GetUninitializedObject(typeof(SpecialCardReward));
		Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
		bool replaced = ColorDiscoveryCardReward.TryFromSavedSpecialCardReward(
			new SerializableReward(),
			original,
			player,
			out ColorDiscoveryCardReward? replacement,
			logFailure: false);
		Expect(!replaced, "a missing restored card must keep the already materialized SpecialCardReward");
		Equal<ColorDiscoveryCardReward?>(null, replacement, "missing restored card replacement");
		Reward finalReward = replaced ? replacement! : original;
		Expect(ReferenceEquals(original, finalReward), "failed conversion must preserve the original reward instance");
	}

	[HextechTest]
	private static void ColorDiscoveryIncludesThirdPartyCharacterPools()
	{
		MethodInfo productionMethod = typeof(ColorDiscoveryRune).GetMethod(
			"GetOtherCharacterCards",
			BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new MissingMethodException(nameof(ColorDiscoveryRune), "GetOtherCharacterCards");
		List<MethodInfo> referencedMethods = [];
		CollectReferencedMethods(productionMethod, referencedMethods, []);
		Expect(
			referencedMethods.Any(static method => method.DeclaringType == typeof(ModelDb)
				&& method.Name == "get_AllCharacters"),
			"production Color Discovery should enumerate every registered character, including locked and external characters");
	}

	[HextechTest]
	private static void InspectOpenScopesToHextechAndPreservesExternalPrefixChanges()
	{
		RelicModel requested = new ColorDiscoveryRune();
		RelicModel externalFirst = new CompatibilityExternalInspectRelicA();
		RelicModel externalSecond = new CompatibilityExternalInspectRelicB();

		Expect(HextechInspectHooks.ShouldHandleInspectRequest(requested), "Hextech inspect request should be handled");
		Expect(
			!HextechInspectHooks.ShouldHandleInspectRequest(externalFirst),
			"third-party inspect request must pass through untouched");

		RelicModel[] externallyModifiedRelics = [externalFirst, externalSecond];
		IReadOnlyList<RelicModel> merged = HextechInspectHooks.MergeRequestedInspectRelic(
			externallyModifiedRelics,
			requested,
			out int requestedIndex);
		SequenceEqual(
			[externalFirst, externalSecond, requested],
			merged,
			"external prefix relic entries must survive Hextech merge");
		Equal(2, requestedIndex, "merged Hextech inspect index");

		IReadOnlyList<RelicModel> unchanged = HextechInspectHooks.MergeRequestedInspectRelic(
			merged,
			requested,
			out int existingIndex);
		Expect(ReferenceEquals(merged, unchanged), "an existing request must not replace another mod's final list object");
		Equal(requestedIndex, existingIndex, "existing Hextech inspect index");
	}

	private sealed class CompatibilityExternalInspectRelicA : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private sealed class CompatibilityExternalInspectRelicB : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}
}
