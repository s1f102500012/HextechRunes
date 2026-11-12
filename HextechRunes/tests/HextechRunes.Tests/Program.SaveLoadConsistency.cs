using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 界面刷新用的只读阶段解析：不创建额外阶段条目、不改激活阶段、不递增 ActState.Version；
	// 已有条目时与 ActivateExtraStage 给出同一序号，查不到时退回当前幕的选择序号。
	[HextechTest]
	private static void PeekStageIndexDoesNotWriteActState()
	{
		(HextechEnemyHexContext context, _, _) = CreatePrismaticEnemyFixture();
		HextechMayhemModifier modifier = context.Modifier;
		RunState run = context.RunState;
		AccessTools.Property(typeof(RunState), "Acts").SetValue(run, new ActModel[3]);
		AccessTools.Field(typeof(RunState), "_currentActIndex").SetValue(run, 1);
		HextechMayhemRunContext runContext = (HextechMayhemRunContext)AccessTools.Field(typeof(HextechMayhemModifier), "_runContext").GetValue(modifier)!;
		HextechMayhemActState actState = runContext.ActState;
		int actStage = modifier.GetCurrentActSelectionIndex();

		void ExpectUnchanged(int version, int actCount, string savedIndexes, int? activeStage, string label)
		{
			Equal(version, actState.Version, $"{label}: version");
			Equal(actCount, actState.ActCount, $"{label}: stage count");
			Equal(savedIndexes, modifier.SavedExtraStageIndexesJson, $"{label}: extra stage entries");
			Equal(activeStage, runContext.ActiveExtraStageIndex, $"{label}: active extra stage");
		}

		int version = actState.Version;
		int actCount = actState.ActCount;
		string savedIndexes = modifier.SavedExtraStageIndexesJson;
		Equal(actStage, modifier.PeekStageIndex("peek-test"), "missing extra stage falls back to the act stage");
		Equal(actStage, modifier.PeekStageIndex(null), "no extra stage id reads the act stage");
		Equal(actStage, modifier.PeekStageIndex(" "), "blank extra stage id reads the act stage");
		ExpectUnchanged(version, actCount, savedIndexes, null, "missing entry");

		int extraStage = modifier.ActivateExtraStage("peek-test");
		modifier.ClearActiveExtraStage();
		version = actState.Version;
		actCount = actState.ActCount;
		savedIndexes = modifier.SavedExtraStageIndexesJson;
		Equal(extraStage, modifier.PeekStageIndex("peek-test"), "existing extra stage resolves to its index");
		ExpectUnchanged(version, actCount, savedIndexes, null, "existing entry while inactive");

		Equal(extraStage, modifier.ActivateExtraStage("peek-test"), "activation reuses the entry");
		version = actState.Version;
		Equal(actStage, modifier.PeekStageIndex(null), "act stage lookup while an extra stage is active");
		Equal(extraStage, modifier.PeekStageIndex("peek-test"), "extra stage lookup while active");
		ExpectUnchanged(version, actCount, savedIndexes, extraStage, "active entry");

		// 条目按无尽轮次限定：换轮后旧条目不再匹配，退回当前幕。
		modifier.SavedActSelectionIndexOffset = actState.ActCount;
		version = actState.Version;
		Equal(modifier.GetCurrentActSelectionIndex(), modifier.PeekStageIndex("peek-test"), "entries from an earlier endless loop do not match");
		ExpectUnchanged(version, actCount, savedIndexes, extraStage, "earlier loop entry");
	}

	// TopBar.Initialize 时点的同步阶段状态还原：有额外阶段 ID 时激活、没有时清除、本阶段未结算时从玩家遗物恢复，
	// 找不到本局 Modifier 时什么都不做。
	[HextechTest]
	private static void TopBarStageStateRestoreActivatesClearsAndRecovers()
	{
		(HextechEnemyHexContext context, Player first, Player second) = CreatePrismaticEnemyFixture();
		HextechMayhemModifier modifier = context.Modifier;
		RunState run = context.RunState;
		AccessTools.Property(typeof(RunState), "Acts").SetValue(run, new ActModel[3]);
		AccessTools.Field(typeof(RunState), "_currentActIndex").SetValue(run, 1);
		FieldInfo relicsField = AccessTools.Field(typeof(Player), "_relics");
		relicsField.SetValue(first, new List<RelicModel>());
		relicsField.SetValue(second, new List<RelicModel>());
		PropertyInfo modifiersProperty = AccessTools.Property(typeof(RunState), nameof(RunState.Modifiers));
		HextechMayhemRunContext runContext = (HextechMayhemRunContext)AccessTools.Field(typeof(HextechMayhemModifier), "_runContext").GetValue(modifier)!;
		int actStage = modifier.GetCurrentActSelectionIndex();

		modifiersProperty.SetValue(run, new List<ModifierModel>());
		Expect(!HextechRunLifecycleHooks.RestoreStageState(run, "topbar test"), "no modifier in the run: nothing restored");
		Equal(null, runContext.ActiveExtraStageIndex, "no modifier in the run: no extra stage activated");

		modifiersProperty.SetValue(run, new List<ModifierModel> { modifier });
		Expect(!HextechRunLifecycleHooks.RestoreStageState(run, modifier, "topbar-test", "topbar test"), "extra stage without relics: nothing recovered");
		int? extraStage = runContext.ActiveExtraStageIndex;
		Expect(extraStage is >= 3, "extra stage id activates an extra stage");
		Equal(extraStage!.Value, modifier.PeekStageIndex("topbar-test"), "activated extra stage has a saved entry");
		Equal(extraStage.Value, modifier.GetCurrentStageIndex(), "current stage is the extra stage");

		HextechRunLifecycleHooks.RestoreStageState(run, modifier, null, "topbar test");
		Equal(null, runContext.ActiveExtraStageIndex, "no extra stage id clears the active extra stage");
		Equal(actStage, modifier.GetCurrentStageIndex(), "current stage falls back to the act stage");

		modifier.ActivateExtraStage("topbar-test");
		HextechRunLifecycleHooks.RestoreStageState(run, "topbar test");
		Equal(null, runContext.ActiveExtraStageIndex, "run-state entry without an extra act provider clears the extra stage");

		NeurosurgeUpgradeRune firstRune = CreateMutableTestModel<NeurosurgeUpgradeRune>();
		firstRune.Owner = first;
		NeurosurgeUpgradeRune secondRune = CreateMutableTestModel<NeurosurgeUpgradeRune>();
		secondRune.Owner = second;
		relicsField.SetValue(first, new List<RelicModel> { firstRune });
		relicsField.SetValue(second, new List<RelicModel> { secondRune });
		Expect(!modifier.IsStageResolved(0) && !modifier.IsStageResolved(actStage), "stages start unresolved");
		Expect(HextechRunLifecycleHooks.RestoreStageState(run, modifier, null, "topbar test"), "unresolved stage recovers from player relics");
		Expect(modifier.IsStageResolved(0), "earlier act recovered as resolved");
		Expect(!modifier.IsStageResolved(actStage), "current act stays unresolved");
		Expect(!HextechRunLifecycleHooks.RestoreStageState(run, modifier, null, "topbar test"), "repeating the restore changes nothing");
	}

	// 没有本局 Modifier 时锻造器配置的来源：单机读本机菜单配置；联机（房主/客机）读默认配置。
	[HextechTest]
	private static void ForgeConfigurationWithoutRunUsesDefaultsInMultiplayer()
	{
		Expect(RunManager.Instance.DebugOnlyGetState() == null, "no current run in tests");
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		HextechRunConfigurationSnapshot local = defaults with
		{
			ForgeRarityWeights = new HextechForgeRarityWeights(10, 20, 70),
			RandomForgeShopPrice = defaults.RandomForgeShopPrice + 77,
			DisabledForgeIds = new HashSet<string>(StringComparer.Ordinal) { "LOCAL_ONLY_FORGE" }
		};
		WithLocalRuneConfiguration(local, () =>
		{
			foreach (NetGameType mode in new[] { NetGameType.Singleplayer, NetGameType.Host, NetGameType.Client })
			{
				WithFeedbackNetGameType(mode, () =>
				{
					bool multiplayer = mode is NetGameType.Host or NetGameType.Client;
					HextechRunConfigurationSnapshot expected = multiplayer ? defaults : local;
					HextechRunConfigurationSnapshot actual = HextechForgeGrantHelper.GetForgeConfigurationWithoutRun();
					Equal(expected.ForgeRarityWeights, actual.ForgeRarityWeights, $"{mode}: forge rarity weights");
					Expect(expected.DisabledForgeIds.SetEquals(actual.DisabledForgeIds), $"{mode}: disabled forge ids");
					Equal(expected.RandomForgeShopPrice, actual.RandomForgeShopPrice, $"{mode}: forge price");
					Equal(expected.RandomForgeShopPrice, HextechForgeShopPriceHelper.GetCurrentRandomForgeShopPrice(), $"{mode}: shop price call site");
				});
			}
		});
	}

	// 局内改过每幕数量与玩家符文禁用表后，四个存档属性按两种顺序还原得到同一有效快照，且等于保存前。
	[HextechTest]
	private static void RunConfigurationSavedPropertiesRestoreInEitherOrder()
	{
		// 有效配置与日志会读本机玩家符文禁用表：让配置读取走内存，不碰 Godot 文件接口。
		using ModEnabledOverride loadedConfig = new();
		HextechMayhemModifier source = CreateMutableTestModel<HextechMayhemModifier>();
		source.SetRunConfigurationSnapshot(HextechRuneConfiguration.GetDefaultSnapshot(), "restore order test");
		source.SetEnemyHexCountsByActSnapshot([ 3, 4, 5 ], "restore order test");
		source.SavedPlayerHexCountsByAct = [ 2, 0, 3 ];
		source.SetPlayerRuneConfigDisabledIdsSnapshot([ "RESTORE_ORDER_RUNE" ], "restore order test");
		string expected = DescribeRunConfiguration(source.GetEffectiveRunConfigurationSnapshot());

		int[] playerCounts = source.SavedPlayerHexCountsByAct;
		int[] enemyCounts = source.SavedEnemyHexCountsByAct;
		string disabledJson = source.SavedPlayerRuneConfigDisabledIdsJson;
		string snapshotJson = source.SavedHextechRunConfigurationSnapshotJson;

		HextechMayhemModifier separateFirst = CreateMutableTestModel<HextechMayhemModifier>();
		separateFirst.SavedPlayerHexCountsByAct = playerCounts;
		separateFirst.SavedEnemyHexCountsByAct = enemyCounts;
		separateFirst.SavedPlayerRuneConfigDisabledIdsJson = disabledJson;
		separateFirst.SavedHextechRunConfigurationSnapshotJson = snapshotJson;

		HextechMayhemModifier snapshotFirst = CreateMutableTestModel<HextechMayhemModifier>();
		snapshotFirst.SavedHextechRunConfigurationSnapshotJson = snapshotJson;
		snapshotFirst.SavedPlayerHexCountsByAct = playerCounts;
		snapshotFirst.SavedEnemyHexCountsByAct = enemyCounts;
		snapshotFirst.SavedPlayerRuneConfigDisabledIdsJson = disabledJson;

		Equal(expected, DescribeRunConfiguration(separateFirst.GetEffectiveRunConfigurationSnapshot()), "separate properties restored before the snapshot");
		Equal(expected, DescribeRunConfiguration(snapshotFirst.GetEffectiveRunConfigurationSnapshot()), "snapshot restored before the separate properties");
		Equal(snapshotJson, separateFirst.SavedHextechRunConfigurationSnapshotJson, "snapshot JSON is stable across a reload");
	}

	// 没有局内改动的普通局：新写出的快照 JSON 与"直接序列化开局冻结快照"的写法逐字节相同。
	[HextechTest]
	private static void RunConfigurationSnapshotJsonUnchangedWithoutRunEdits()
	{
		using ModEnabledOverride loadedConfig = new();
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		HextechRunConfigurationSnapshot custom = defaults with
		{
			PlayerHexCountsByAct = [ 0, 2, 4 ],
			EnemyHexCountsByAct = [ 1, 3, 6 ],
			DisabledPlayerRuneIds = new HashSet<string>(StringComparer.Ordinal) { "ZETA_RUNE", "ALPHA_RUNE" },
			DisabledForgeIds = new HashSet<string>(StringComparer.Ordinal) { "SOME_FORGE" },
			RandomForgeShopPrice = 321
		};
		Type jsonType = AccessTools.Inner(typeof(HextechMayhemModifier), "RunConfigurationSnapshotJson");
		FieldInfo runContextField = AccessTools.Field(typeof(HextechMayhemModifier), "_runContext");
		foreach ((string label, HextechRunConfigurationSnapshot snapshot) in new[]
		{
			("default", defaults),
			("custom", custom),
			("godslayer trial", HextechGodslayerTrial.CreateTrialSnapshot())
		})
		{
			HextechMayhemModifier modifier = CreateMutableTestModel<HextechMayhemModifier>();
			modifier.SetRunConfigurationSnapshot(snapshot, "byte stability test");
			HextechMayhemRunContext runContext = (HextechMayhemRunContext)runContextField.GetValue(modifier)!;
			object legacyProjection = Activator.CreateInstance(
				jsonType,
				HextechRuneConfiguration.NormalizeSnapshot(runContext.RunConfigurationSnapshot!))!;
			string legacyJson = JsonSerializer.Serialize(legacyProjection, jsonType, HextechTelemetry.JsonOptions);
			Equal(legacyJson, modifier.SavedHextechRunConfigurationSnapshotJson, $"{label}: snapshot JSON bytes");
		}

		Equal("", CreateMutableTestModel<HextechMayhemModifier>().SavedHextechRunConfigurationSnapshotJson, "no snapshot still writes an empty string");
	}

	private static string DescribeRunConfiguration(HextechRunConfigurationSnapshot snapshot)
	{
		return JsonSerializer.Serialize(new
		{
			snapshot.PlayerHexCountsByAct,
			snapshot.EnemyHexCountsByAct,
			DisabledPlayerRuneIds = snapshot.DisabledPlayerRuneIds.Order(StringComparer.Ordinal).ToArray(),
			DisabledMonsterHexIds = snapshot.DisabledMonsterHexIds.Order(StringComparer.Ordinal).ToArray(),
			DisabledForgeIds = snapshot.DisabledForgeIds.Order(StringComparer.Ordinal).ToArray(),
			Rest = snapshot with
			{
				PlayerHexCountsByAct = [],
				EnemyHexCountsByAct = [],
				DisabledPlayerRuneIds = new HashSet<string>(),
				DisabledMonsterHexIds = new HashSet<string>(),
				DisabledForgeIds = new HashSet<string>()
			}
		});
	}

	// 把本机菜单配置临时换成给定快照（只在内存里），结束后还原；不碰 Godot 文件接口。
	private static void WithLocalRuneConfiguration(HextechRunConfigurationSnapshot snapshot, Action body)
	{
		FieldInfo loadedField = AccessTools.Field(typeof(HextechRuneConfiguration), "_loaded");
		FieldInfo configField = AccessTools.Field(typeof(HextechRuneConfiguration), "_config");
		object? wasLoaded = loadedField.GetValue(null);
		object? previous = configField.GetValue(null);
		object config = Activator.CreateInstance(configField.FieldType, nonPublic: true)!;
		AccessTools.Method(typeof(HextechRuneConfiguration), "StoreNormalizedSnapshot")
			.Invoke(null, [ config, HextechRuneConfiguration.NormalizeSnapshot(snapshot) ]);
		try
		{
			configField.SetValue(null, config);
			loadedField.SetValue(null, true);
			body();
		}
		finally
		{
			configField.SetValue(null, previous);
			loadedField.SetValue(null, wasLoaded);
		}
	}
}
