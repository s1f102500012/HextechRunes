using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void ExternalRegistrationValidationPrecedesAllSideEffects()
	{
		int registryVersion = HextechExternalContentRegistry.Version;
		int playerRuneCount = HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count;
		int forgeCount = HextechExternalContentRegistry.GetForgeRegistrations().Count;
		int eventRelicCount = HextechExternalContentRegistry.GetEventRelicTypes().Count;
		IReadOnlyList<PropertyInfo>? invalidRarityCacheBefore = GetSavedPropertiesForCompatibilityTest(
			typeof(CompatibilityInvalidRarityRune));

		ExpectThrows<ArgumentNullException>(
			() => HextechRunesApi.RegisterPlayerRune(null!, HextechRarityTier.Silver),
			"null player rune type");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterPlayerRune(typeof(string), HextechRarityTier.Silver),
			"wrong player rune base type");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterPlayerRune(typeof(CompatibilityOpenGenericRune<>), HextechRarityTier.Silver),
			"open generic player rune type");
		ExpectThrows<ArgumentOutOfRangeException>(
			() => HextechRunesApi.RegisterPlayerRune<CompatibilityInvalidRarityRune>((HextechRarityTier)999),
			"undefined player rune rarity");
		ExpectThrows<ArgumentOutOfRangeException>(
			() => HextechRunesApi.RegisterPlayerRune<CompatibilityInvalidFlagsRune>(
				HextechRarityTier.Silver,
				(PlayerRuneFlags)(1 << 29)),
			"unknown player rune flag bits");
		ExpectThrows<ArgumentOutOfRangeException>(
			() => HextechRunesApi.RegisterPlayerRune<CompatibilityInvalidCharacterPoolRune>(
				HextechRarityTier.Silver,
				characterPool: (PlayerRuneCharacterPool)999),
			"undefined player rune character pool");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterPlayerRune<CompatibilityBlankTagRune>(
				HextechRarityTier.Silver,
				tagKey: " \t"),
			"blank player rune tag key");

		ExpectThrows<ArgumentNullException>(
			() => HextechRunesApi.RegisterEventRelic(null!),
			"null event relic type");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterEventRelic(typeof(string)),
			"wrong event relic base type");
		ExpectThrows<ArgumentNullException>(
			() => HextechRunesApi.RegisterForge(null!, HextechRarityTier.Silver),
			"null forge type");
		ExpectThrows<ArgumentOutOfRangeException>(
			() => HextechRunesApi.RegisterForge<CompatibilityInvalidRarityForge>((HextechRarityTier)(-1)),
			"undefined forge rarity");
		ExpectThrows<ArgumentNullException>(
			() => HextechRunesApi.RegisterSavedPropertyCarrier(null!),
			"null SavedProperty carrier type");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterSavedPropertyCarrier(typeof(CompatibilityOpenGenericCarrier<>)),
			"open generic SavedProperty carrier type");
		ExpectThrows<ArgumentNullException>(
			() => HextechRunesApi.RegisterEnchantmentIcon(null!, "res://Compatibility/icon.png"),
			"null enchantment icon type");
		ExpectThrows<ArgumentException>(
			() => HextechRunesApi.RegisterEnchantmentIcon(
				typeof(CompatibilityIconValidationEnchantment),
				" \t"),
			"blank enchantment icon path");

		Equal(registryVersion, HextechExternalContentRegistry.Version, "registry version after invalid registrations");
		Equal(playerRuneCount, HextechExternalContentRegistry.GetPlayerRuneRegistrations().Count, "player rune count after invalid registrations");
		Equal(forgeCount, HextechExternalContentRegistry.GetForgeRegistrations().Count, "forge count after invalid registrations");
		Equal(eventRelicCount, HextechExternalContentRegistry.GetEventRelicTypes().Count, "event relic count after invalid registrations");
		Expect(
			ReferenceEquals(
				invalidRarityCacheBefore,
				GetSavedPropertiesForCompatibilityTest(typeof(CompatibilityInvalidRarityRune))),
			"invalid metadata must not mutate the SavedProperty per-type cache");

		Type[] unqueuedSharedRelics =
		[
			typeof(CompatibilityInvalidRarityRune),
			typeof(CompatibilityInvalidFlagsRune),
			typeof(CompatibilityInvalidCharacterPoolRune),
			typeof(CompatibilityBlankTagRune),
			typeof(CompatibilityInvalidRarityForge)
		];
		foreach (Type modelType in unqueuedSharedRelics)
		{
			Expect(
				!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(
					typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool),
					modelType),
				$"invalid registration must not queue {modelType.Name}");
		}
	}

	[HextechTest]
	private static void ExternalResourceOwnershipIsFirstWriterWinsAndIdempotent()
	{
		Action restoreLogBudget = SuppressCompatibilityWarnings(
			"external-content.asset-owner-conflict",
			"external-content.enchantment-icon-conflict");
		try
		{
		PlayerRuneRegistration playerRegistration = new(
			typeof(CompatibilityResourceRune),
			HextechRarityTier.Silver);
		HextechExternalContentRegistry.RegisterPlayerRune(playerRegistration, "Compatibility.OwnerA");
		int playerVersion = HextechExternalContentRegistry.Version;
		HextechExternalContentRegistry.RegisterPlayerRune(playerRegistration, "Compatibility.OwnerA");
		Equal(playerVersion, HextechExternalContentRegistry.Version, "same player asset owner should be idempotent");
		HextechExternalContentRegistry.RegisterPlayerRune(playerRegistration, "Compatibility.OwnerB");
		Equal(playerVersion, HextechExternalContentRegistry.Version, "conflicting player asset owner should not change version");
		Equal(
			"Compatibility.OwnerA",
			HextechExternalContentRegistry.GetAssetModId(ModelDb.GetId(typeof(CompatibilityResourceRune))),
			"first player asset owner");

		ForgeRegistration forgeRegistration = new(
			typeof(CompatibilityResourceForge),
			HextechRarityTier.Gold);
		HextechExternalContentRegistry.RegisterForge(forgeRegistration, "Compatibility.ForgeA");
		int forgeVersion = HextechExternalContentRegistry.Version;
		HextechExternalContentRegistry.RegisterForge(forgeRegistration, "Compatibility.ForgeB");
		Equal(forgeVersion, HextechExternalContentRegistry.Version, "conflicting forge asset owner should not change version");
		Equal(
			"Compatibility.ForgeA",
			HextechExternalContentRegistry.GetAssetModId(ModelDb.GetId(typeof(CompatibilityResourceForge))),
			"first forge asset owner");

		HextechExternalContentRegistry.RegisterEventRelic(typeof(CompatibilityResourceEventRelic), "Compatibility.EventA");
		int eventVersion = HextechExternalContentRegistry.Version;
		HextechExternalContentRegistry.RegisterEventRelic(typeof(CompatibilityResourceEventRelic), "Compatibility.EventB");
		Equal(eventVersion, HextechExternalContentRegistry.Version, "conflicting event asset owner should not change version");
		Equal(
			"Compatibility.EventA",
			HextechExternalContentRegistry.GetAssetModId(ModelDb.GetId(typeof(CompatibilityResourceEventRelic))),
			"first event asset owner");

		PlayerRuneRegistration fillRegistration = new(
			typeof(CompatibilityResourceFillRune),
			HextechRarityTier.Silver);
		HextechExternalContentRegistry.RegisterPlayerRune(fillRegistration, null);
		int beforeFillVersion = HextechExternalContentRegistry.Version;
		HextechExternalContentRegistry.RegisterPlayerRune(fillRegistration, "Compatibility.FilledOwner");
		Equal(beforeFillVersion + 1, HextechExternalContentRegistry.Version, "first non-empty asset owner should update version");
		Equal(
			"Compatibility.FilledOwner",
			HextechExternalContentRegistry.GetAssetModId(ModelDb.GetId(typeof(CompatibilityResourceFillRune))),
			"filled player asset owner");

		ModelId enchantmentId = ModelDb.GetId(typeof(CompatibilityResourceEnchantment));
		const string firstIconPath = "res://Compatibility.OwnerA/images/enchantments/icon.png";
		HextechExternalContentRegistry.RegisterEnchantmentIcon(typeof(CompatibilityResourceEnchantment), firstIconPath);
		int iconVersion = HextechExternalContentRegistry.Version;
		HextechExternalContentRegistry.RegisterEnchantmentIcon(typeof(CompatibilityResourceEnchantment), firstIconPath);
		Equal(iconVersion, HextechExternalContentRegistry.Version, "same enchantment icon path should be idempotent");
		HextechExternalContentRegistry.RegisterEnchantmentIcon(
			typeof(CompatibilityResourceEnchantment),
			"res://Compatibility.OwnerB/images/enchantments/icon.png");
		Equal(iconVersion, HextechExternalContentRegistry.Version, "conflicting enchantment icon path should not change version");
		Equal(firstIconPath, HextechExternalContentRegistry.GetEnchantmentIconPath(enchantmentId), "first enchantment icon path");
		}
		finally
		{
			restoreLogBudget();
		}
	}

	[HextechTest]
	private static void SavedForgeRewardRestoreFiltersUnavailableExternalContent()
	{
		Action restoreLogBudget = SuppressCompatibilityWarnings("rewards.forge-choice-restore-skip");
		try
		{
		RegisterCompatibilityForgeModel(typeof(CompatibilityRestorableExternalForgeA), HextechRarityTier.Silver);
		RegisterCompatibilityForgeModel(typeof(CompatibilityRestorableExternalForgeB), HextechRarityTier.Gold);
		InjectCompatibilityModel(typeof(CompatibilityWrongSavedRewardModel));
		InjectCompatibilityModel(typeof(CompatibilityRemovedForgeRelic));

		ModelId validA = ModelDb.GetId(typeof(CompatibilityRestorableExternalForgeA));
		ModelId validB = ModelDb.GetId(typeof(CompatibilityRestorableExternalForgeB));
		ModelId missing = new("RELIC", "COMPATIBILITY_MISSING_EXTERNAL_FORGE");
		ModelId wrongType = ModelDb.GetId(typeof(CompatibilityWrongSavedRewardModel));
		ModelId removedForge = ModelDb.GetId(typeof(CompatibilityRemovedForgeRelic));
		SerializableReward save = CreateSavedForgeReward(
			[ missing, validA, wrongType, removedForge, validB ],
			optionCount: 2);
		Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));

		Expect(
			HextechForgeChoiceReward.TryFromSavedReward(save, player, out HextechForgeChoiceReward? restored),
			"mixed saved forge options should retain valid external forges");
		Expect(restored != null, "mixed saved forge options should produce a reward");
		SequenceEqual(
			[ validA, validB ],
			restored!.ToSerializable().CardPoolIds,
			"restored external forge option order");

		Reward result = new GoldReward(0, player, false);
		Expect(
			HextechRewardSafetyHooks.TryRestoreForgeChoiceReward(save, player, ref result),
			"valid saved external forge options should replace the GoldReward marker");
		Expect(result is HextechForgeChoiceReward, "valid saved external forge options should restore the custom reward");
		}
		finally
		{
			restoreLogBudget();
		}
	}

	[HextechTest]
	private static void SavedForgeRewardRestoreKeepsGoldFallbackWhenAllOptionsInvalid()
	{
		Action restoreLogBudget = SuppressCompatibilityWarnings("rewards.forge-choice-restore-skip");
		try
		{
		InjectCompatibilityModel(typeof(CompatibilityAllInvalidWrongModel));
		InjectCompatibilityModel(typeof(CompatibilityAllInvalidRemovedRelic));
		SerializableReward save = CreateSavedForgeReward(
			[
				new ModelId("RELIC", "COMPATIBILITY_MISSING_ONLY_FORGE"),
				ModelDb.GetId(typeof(CompatibilityAllInvalidWrongModel)),
				ModelDb.GetId(typeof(CompatibilityAllInvalidRemovedRelic))
			],
			optionCount: 3);
		Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));

		Expect(
			!HextechForgeChoiceReward.TryFromSavedReward(save, player, out HextechForgeChoiceReward? restored),
			"all-invalid saved forge options should not create a custom reward");
		Equal<HextechForgeChoiceReward?>(null, restored, "all-invalid saved forge reward result");

		Reward original = new GoldReward(0, player, false);
		Reward result = original;
		Expect(
			!HextechRewardSafetyHooks.TryRestoreForgeChoiceReward(save, player, ref result),
			"all-invalid saved forge options should not replace the GoldReward marker");
		Expect(ReferenceEquals(original, result), "all-invalid saved forge options should preserve the original GoldReward instance");
		}
		finally
		{
			restoreLogBudget();
		}
	}

	private static IReadOnlyList<PropertyInfo>? GetSavedPropertiesForCompatibilityTest(Type type)
	{
		return MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.GetJsonPropertiesForType(type);
	}

	private static void RegisterCompatibilityForgeModel(Type forgeType, HextechRarityTier rarity)
	{
		InjectCompatibilityModel(forgeType);
		HextechExternalContentRegistry.RegisterForge(new ForgeRegistration(forgeType, rarity), null);
	}

	private static void InjectCompatibilityModel(Type modelType)
	{
		if (!ModelDb.Contains(modelType))
		{
			ModelDb.Inject(modelType);
		}
	}

	private static SerializableReward CreateSavedForgeReward(IEnumerable<ModelId> ids, int optionCount)
	{
		return new SerializableReward
		{
			RewardType = RewardType.Gold,
			GoldAmount = 0,
			CardPoolIds = ids.ToList(),
			OptionCount = optionCount,
			CustomDescriptionEncounterSourceId = ModelDb.GetId<RandomForgeShopRelic>()
		};
	}

	private static Action SuppressCompatibilityWarnings(params string[] keys)
	{
		Action restore = CaptureStaticCollectionRestore(
			typeof(HextechRunLogBudget),
			"ConsumedByKey");
		foreach (string key in keys)
		{
			for (int attempt = 0; attempt < 12; attempt++)
			{
				HextechRunLogBudget.TryConsume(key, 12);
			}
		}

		return restore;
	}

	private sealed class CompatibilityInvalidRarityRune : HextechRelicBase
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int InvalidRegistrationCounter { get; set; } = 1;
	}

	private sealed class CompatibilityInvalidFlagsRune : HextechRelicBase { }
	private sealed class CompatibilityInvalidCharacterPoolRune : HextechRelicBase { }
	private sealed class CompatibilityBlankTagRune : HextechRelicBase { }
	private sealed class CompatibilityInvalidRarityForge : HextechForgeBase { }
	private sealed class CompatibilityOpenGenericRune<T> : HextechRelicBase { }
	private sealed class CompatibilityOpenGenericCarrier<T> : EnchantmentModel { }
	private sealed class CompatibilityIconValidationEnchantment : EnchantmentModel { }
	private sealed class CompatibilityResourceRune : HextechRelicBase { }
	private sealed class CompatibilityResourceFillRune : HextechRelicBase { }
	private sealed class CompatibilityResourceForge : HextechForgeBase { }
	private sealed class CompatibilityResourceEnchantment : EnchantmentModel { }
	private sealed class CompatibilityRestorableExternalForgeA : HextechForgeBase { }
	private sealed class CompatibilityRestorableExternalForgeB : HextechForgeBase { }
	private sealed class CompatibilityWrongSavedRewardModel : EnchantmentModel { }
	private sealed class CompatibilityAllInvalidWrongModel : EnchantmentModel { }

	private sealed class CompatibilityResourceEventRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private sealed class CompatibilityRemovedForgeRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Common;
	}

	private sealed class CompatibilityAllInvalidRemovedRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Uncommon;
	}
}
