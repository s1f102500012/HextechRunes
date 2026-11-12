using System.Reflection;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 联机候选池不看本机补丁失败表，只看本局禁用列表；单机仍按本机表过滤。
	[HextechTest]
	private static void HookFailedRunesFilterPoolOnlyInSinglePlayer()
	{
		const HextechRarityTier rarity = HextechRarityTier.Gold;
		using ModEnabledOverride loadedConfig = new();
		using IDisposable models = InjectMissingModels([ typeof(Ironclad), .. HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(rarity) ]);
		(RunState run, Player player) = CreateGuaranteedPickFixture(ModelDb.GetById<CharacterModel>(ModelDb.GetId(typeof(Ironclad))));
		SetAutoProperty(player, nameof(Player.Deck), new CardPile(PileType.Deck));
		HextechMayhemModifier modifier = CreateMutableTestModel<HextechMayhemModifier>();
		AccessTools.Field(typeof(ModifierModel), "_runState").SetValue(modifier, run);
		modifier.SetPlayerRuneConfigDisabledIdsSnapshot([], "hook-failed pool test");
		SetAutoProperty(run, nameof(RunState.Modifiers), new List<ModifierModel> { modifier });

		List<ModelId> baseline = PoolIds(NetGameType.Host);
		Expect(baseline.Count >= 2, "baseline pool should have at least two runes");
		Type failedType = HextechCatalog.GetConfigurablePlayerRuneTypesForRarity(rarity).Single(type => ModelDb.GetId(type) == baseline[0]);
		ModelId disabledId = baseline[1];
		modifier.SetPlayerRuneConfigDisabledIdsSnapshot([ disabledId.Entry ], "hook-failed pool test");

		WithHookFailedPlayerRunes([ failedType ], () =>
		{
			List<ModelId> single = PoolIds(NetGameType.Singleplayer);
			Expect(!single.Contains(baseline[0]), "single player pool skips the locally hook-failed rune");
			Expect(!single.Contains(disabledId), "single player pool skips the run-disabled rune");
			foreach (NetGameType mode in new[] { NetGameType.Host, NetGameType.Client })
			{
				List<ModelId> multi = PoolIds(mode);
				Expect(multi.Contains(baseline[0]), $"{mode} pool ignores the local hook-failed table");
				SequenceEqual(baseline.Where(id => id != disabledId), multi, $"{mode} pool equals the baseline minus run-disabled ids");
			}

			// 稀有度可用性与候选池同口径：只剩本机失败的那一枚时，单机视为该稀有度全禁，联机不受本机表影响。
			HashSet<string> allButFailed = GetConfigurableRuneEntries(rarity);
			allButFailed.Remove(ModelDb.GetId(failedType).Entry);
			bool RarityEnabled(NetGameType mode)
			{
				bool enabled = false;
				WithFeedbackNetGameType(mode, () => enabled = HextechRunePoolBuilder.GetEnabledPlayerRuneRaritiesForDisabledIds(allButFailed).Contains(rarity));
				return enabled;
			}

			Expect(!RarityEnabled(NetGameType.Singleplayer), "single player treats a rarity with only hook-failed runes as disabled");
			Expect(RarityEnabled(NetGameType.Host) && RarityEnabled(NetGameType.Client), "multiplayer rarity availability ignores the local table");
		});

		Func<Type, bool> singleFilter = HextechRuntimeRuneCompatibility.GetPlayerRunePoolRuntimeFilter(false, new HashSet<Type> { failedType });
		Func<Type, bool> multiFilter = HextechRuntimeRuneCompatibility.GetPlayerRunePoolRuntimeFilter(true, new HashSet<Type> { failedType });
		Expect(!singleFilter(failedType) && singleFilter(typeof(StrengthForge)), "single filter drops only failed types");
		Expect(multiFilter(failedType), "multiplayer filter keeps failed types");

		List<ModelId> PoolIds(NetGameType mode)
		{
			List<ModelId> ids = [];
			WithFeedbackNetGameType(mode, () => ids = HextechRunePoolBuilder.BuildSelectableRunePool(player, rarity, run)
				.Select(static relic => relic.CanonicalId())
				.ToList());
			return ids;
		}
	}

	// 房主新开局冻结点并入本机失败项，下发的幕掷骰载荷里带着它；非房主与读档还原（SetRunConfigurationSnapshot）不并入。
	[HextechTest]
	private static void HostRunConfigFreezeMergesHookFailedRunesIntoSyncedPayload()
	{
		using ModEnabledOverride loadedConfig = new();
		ModelId[] configurable = HextechCatalog.GetConfigurablePlayerRuneIds()
			.OrderBy(static id => id.Entry, StringComparer.Ordinal)
			.ToArray();
		ModelId failedId = configurable[0];
		ModelId alreadyDisabledId = configurable[1];
		string nonConfigurableId = ModelDb.GetId(typeof(StrengthForge)).Entry;
		string registeredForgeId = ModelDb.GetId(typeof(StrengthForge)).Entry;
		const string staleForgeId = "HEXTECH_TEST_REMOVED_FORGE";
		HextechRunConfigurationSnapshot local = HextechRuneConfiguration.GetDefaultSnapshot() with
		{
			DisabledPlayerRuneIds = [ alreadyDisabledId.Entry ],
			DisabledForgeIds = [ registeredForgeId, staleForgeId ]
		};

		string[] hostHookFailedIds = [ failedId.Entry, nonConfigurableId ];
		HextechRunConfigurationSnapshot hostFrozen = HextechMayhemModifier.FreezeNewRunConfigurationSnapshot(
			local, isContentRegistered: true, hostHookFailedIds, out List<string> frozenAdded, out List<string> frozenSkipped);
		SetEqual([ alreadyDisabledId.Entry, failedId.Entry ], hostFrozen.DisabledPlayerRuneIds, "host freeze merges configurable hook-failed runes only");
		SequenceEqual(new[] { failedId.Entry }, frozenAdded, "host freeze reports merged ids");
		SequenceEqual(new[] { nonConfigurableId }, frozenSkipped, "host freeze reports non-configurable ids for the warning");
		SetEqual([ registeredForgeId ], hostFrozen.DisabledForgeIds, "run snapshot drops forge ids missing from the registry");
		SetEqual([ registeredForgeId, staleForgeId ], local.DisabledForgeIds, "local config snapshot keeps its forge ids");

		HextechRunConfigurationSnapshot singleFrozen = HextechMayhemModifier.FreezeNewRunConfigurationSnapshot(local, isContentRegistered: true, null, out _, out _);
		SetEqual([ alreadyDisabledId.Entry ], singleFrozen.DisabledPlayerRuneIds, "single player freeze does not merge the local table");
		HextechRunConfigurationSnapshot earlyFrozen = HextechMayhemModifier.FreezeNewRunConfigurationSnapshot(local, isContentRegistered: false, null, out _, out _);
		SetEqual([ registeredForgeId, staleForgeId ], earlyFrozen.DisabledForgeIds, "forge ids are kept while content is not registered");

		// 房主幕掷骰下发的就是 modifier 上的本局列表（SendHostActRoll 用 PlayerRuneConfigDisabledIds 与有效快照编码）。
		HextechMayhemModifier modifier = CreateMutableTestModel<HextechMayhemModifier>();
		modifier.SetRunConfigurationSnapshot(hostFrozen, "freeze test");
		HextechRunConfigurationSnapshot sent = modifier.GetEffectiveRunConfigurationSnapshot();
		PlayerChoiceResult payload = HextechChoiceCodec.CreateActRoll(0, HextechRarityTier.Gold, null, false,
			modifier.EnemyHexCountsByAct, modifier.PlayerRuneConfigDisabledIds, sent);
		Expect(HextechChoiceCodec.TryDecodeActRoll(payload, 0, out _, out _, out _, out _,
			out HashSet<string> decodedDisabled, out HextechRunConfigurationSnapshot decodedSnapshot), "act roll decodes");
		SetEqual([ alreadyDisabledId.Entry, failedId.Entry ], decodedDisabled, "synced player disabled ids include the host hook-failed rune");
		SetEqual([ registeredForgeId ], decodedSnapshot.DisabledForgeIds, "synced forge ids match the frozen run snapshot");

		HextechMayhemModifier restored = CreateMutableTestModel<HextechMayhemModifier>();
		restored.SetRunConfigurationSnapshot(local, "restore saved run config");
		SetEqual([ alreadyDisabledId.Entry ], restored.PlayerRuneConfigDisabledIds, "restoring a saved snapshot does not merge again");

		// 客户端告警判定：本机失败项减去房主禁用列表。
		IReadOnlyList<string> enabledByHost = HextechRuntimeRuneCompatibility.GetHookFailedRuneIdsNotDisabled(
			[ failedId.Entry, alreadyDisabledId.Entry, nonConfigurableId, failedId.Entry ],
			new HashSet<string>(StringComparer.Ordinal) { alreadyDisabledId.Entry });
		SequenceEqual(new[] { failedId.Entry, nonConfigurableId }.OrderBy(static id => id, StringComparer.Ordinal), enabledByHost, "client warning lists hook-failed ids the host left enabled");
		Equal(0, HextechRuntimeRuneCompatibility.GetHookFailedRuneIdsNotDisabled([ failedId.Entry ], decodedDisabled).Count, "nothing to warn once the host disabled it");

		HashSet<string> merged = HextechRuntimeRuneCompatibility.MergeHookFailedIntoDisabledIds(
			[ " " + alreadyDisabledId.Entry ],
			[ failedId.Entry, alreadyDisabledId.Entry, nonConfigurableId ],
			configurable.Select(static id => id.Entry).ToHashSet(StringComparer.Ordinal),
			out List<string> added,
			out List<string> skipped);
		SetEqual([ alreadyDisabledId.Entry, failedId.Entry ], merged, "pure merge result");
		SequenceEqual(new[] { failedId.Entry }, added, "pure merge added ids");
		SequenceEqual(new[] { nonConfigurableId }, skipped, "pure merge skipped ids");
	}

	// 敌方海克斯全禁、锻造器全禁（注册表实际数量与超过旧上限的人造数量）都能往返；超过新上限仍被拒绝。
	[HextechTest]
	private static void ActRollConfigSnapshotRoundTripsFullyDisabledLists()
	{
		HashSet<string> allMonsterHexIds = HextechRuneConfiguration.NormalizeDisabledMonsterHexIds(
			Enum.GetValues<MonsterHexKind>().Select(static kind => kind.ToString()));
		HashSet<string> allForgeIds = HextechCatalog.GetAllForgeTypes()
			.Select(static type => ModelDb.GetId(type).Entry)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> allPlayerRuneIds = HextechCatalog.GetConfigurablePlayerRuneIds()
			.Select(static id => id.Entry)
			.ToHashSet(StringComparer.Ordinal);
		Console.WriteLine($"registry sizes: enemyHexes={allMonsterHexIds.Count} forges={allForgeIds.Count} playerRunes={allPlayerRuneIds.Count}");
		Expect(allMonsterHexIds.Count > 128, $"configurable enemy hexes exceed the old limit: {allMonsterHexIds.Count}");
		Expect(allMonsterHexIds.Count <= HextechChoiceCodec.MaxDisabledMonsterHexes, "enemy hex limit covers the registry");
		Expect(allForgeIds.Count <= HextechChoiceCodec.MaxDisabledForgeIds, "forge limit covers the registry");

		RoundTrip(allForgeIds, "registry forges");
		// 人造 ID 取编码允许的最长长度，估算两份列表都满额时的最坏载荷。
		int padding = HextechStableModelIdListCodec.MaxSerializedLength - new ModelId(ModInfo.Id, "HEXTECH_TEST_FORGE_0000_").ToString().Length;
		HashSet<string> manyForgeIds = Enumerable.Range(0, HextechChoiceCodec.MaxDisabledForgeIds)
			.Select(i => $"HEXTECH_TEST_FORGE_{i:D4}_{new string('X', padding)}")
			.ToHashSet(StringComparer.Ordinal);
		int maxPayloadInts = RoundTrip(manyForgeIds, "max-count forges");
		Console.WriteLine($"act-roll payload at forge cap: ints={maxPayloadInts} bytes~={maxPayloadInts * 4}");
		Expect(maxPayloadInts * 4 < 512 * 1024, "payload at the caps stays below the Steam message size limit");

		HashSet<string> tooManyForgeIds = Enumerable.Range(0, HextechChoiceCodec.MaxDisabledForgeIds + 1)
			.Select(static i => $"HEXTECH_TEST_FORGE_{i:D4}")
			.ToHashSet(StringComparer.Ordinal);
		ExpectThrows<ArgumentOutOfRangeException>(
			() => HextechChoiceCodec.CreateActRoll(0, HextechRarityTier.Gold, null, false, [ 1, 1, 1 ], allPlayerRuneIds,
				HextechRuneConfiguration.GetDefaultSnapshot() with { DisabledForgeIds = tooManyForgeIds }),
			"forge list above the new limit is rejected by the encoder");

		List<int> forgeList = [];
		HextechStableModelIdListCodec.Append(forgeList, tooManyForgeIds.Select(static entry => new ModelId(ModInfo.Id, entry)), int.MaxValue);
		Expect(!HextechStableModelIdListCodec.TryDecode(forgeList, 0, HextechChoiceCodec.MaxDisabledForgeIds, out _, out _), "forge list above the new limit is rejected by the decoder");
		Expect(HextechStableModelIdListCodec.TryDecode(forgeList, 0, HextechChoiceCodec.MaxDisabledForgeIds + 1, out List<ModelId> decodedForges, out _)
			&& decodedForges.Count == tooManyForgeIds.Count, "same list decodes with a larger explicit limit (wire format unchanged)");

		MethodInfo readMonsterHexes = AccessTools.Method(typeof(HextechChoiceCodec), "TryReadDisabledMonsterHexIds")
			?? throw new MissingMethodException(nameof(HextechChoiceCodec), "TryReadDisabledMonsterHexIds");
		foreach ((int count, bool accepted) in new[] { (HextechChoiceCodec.MaxDisabledMonsterHexes, true), (HextechChoiceCodec.MaxDisabledMonsterHexes + 1, false) })
		{
			List<int> monsterPayload = [ count, .. Enumerable.Repeat(0, count) ];
			object?[] args = [ monsterPayload, 0, null ];
			Equal(accepted, (bool)readMonsterHexes.Invoke(null, args)!, $"monster hex list count {count}");
		}

		int RoundTrip(HashSet<string> forgeIds, string label)
		{
			HextechRunConfigurationSnapshot snapshot = HextechRuneConfiguration.GetDefaultSnapshot() with
			{
				DisabledPlayerRuneIds = allPlayerRuneIds,
				DisabledMonsterHexIds = allMonsterHexIds,
				DisabledForgeIds = forgeIds
			};
			PlayerChoiceResult result = HextechChoiceCodec.CreateActRoll(0, HextechRarityTier.Gold, null, false, [ 1, 1, 1 ], allPlayerRuneIds, snapshot);
			Expect(HextechChoiceCodec.TryDecodeActRoll(result, 0, out _, out _, out _, out _,
				out HashSet<string> decodedPlayer, out HextechRunConfigurationSnapshot decoded), $"{label}: act roll decodes");
			SetEqual(allPlayerRuneIds, decodedPlayer, $"{label}: player disabled ids");
			SetEqual(allMonsterHexIds, decoded.DisabledMonsterHexIds, $"{label}: monster hex disabled ids");
			SetEqual(forgeIds, decoded.DisabledForgeIds, $"{label}: forge disabled ids");
			Expect(HextechChoiceCodec.TryGetIndexPayload(result, out List<int> ints), $"{label}: index payload");
			return ints.Count;
		}
	}

	// 往本机补丁失败表里临时加入类型（不走 MarkPlayerRuneHookFailed：测试进程里原版日志不可用），结束时只移除本次加入的。
	private static void WithHookFailedPlayerRunes(IEnumerable<Type> types, Action body)
	{
		HashSet<Type> failed = (HashSet<Type>)(AccessTools.Field(typeof(HextechRuntimeRuneCompatibility), "HookFailedPlayerRuneTypes")
			?? throw new MissingFieldException(nameof(HextechRuntimeRuneCompatibility), "HookFailedPlayerRuneTypes")).GetValue(null)!;
		List<Type> added = [];
		try
		{
			foreach (Type type in types)
			{
				if (failed.Add(type))
				{
					added.Add(type);
				}
			}

			body();
		}
		finally
		{
			foreach (Type type in added)
			{
				failed.Remove(type);
			}
		}
	}
}
