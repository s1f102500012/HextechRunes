using System.Reflection;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using VanillaBurningBlood = MegaCrit.Sts2.Core.Models.Relics.BurningBlood;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static readonly List<(RelicModel Original, RelicModel Replacement)> RecordedOrobasReplacements = [];
	private static readonly List<RelicModel> RecordedOrobasObtains = [];

	[HextechTest]
	private static void OrobasStarterSelectionSkipsHextechRelics()
	{
		WithOrobasStarterModels(() =>
		{
			MethodInfo getStarter = AccessTools.Method(typeof(TouchOfOrobas), "GetStarterRelic", [typeof(Player)]);
			RelicModel hex = CreateMutableTestModel<NeurosurgeUpgradeRune>();
			RelicModel forge = CreateMutableTestModel<GlamForge>();
			RelicModel burningBlood = MutableOrobasModel<VanillaBurningBlood>();
			RelicModel blackBlood = MutableOrobasModel<BlackBlood>();
			RelicModel plus = MutableOrobasModel<HextechBlackBloodPlus>();
			RelicModel circlet = MutableOrobasModel<Circlet>();
			Expect(HextechCatalog.IsHextechCustomRelic(hex) && HextechCatalog.IsHextechCustomRelic(forge), "runes and forges are hextech-managed");
			Expect(!HextechCatalog.IsHextechCustomRelic(plus), "orobas plus relics stay character starter relics");

			RelicModel? ChooseStarter(RelicModel[] layout)
			{
				TouchOfOrobas touch = MutableOrobasModel<TouchOfOrobas>();
				return (RelicModel?)getStarter.Invoke(touch, [CreateOrobasTestPlayer(layout)]);
			}

			RelicModel[][] withoutHextech =
			[
				[burningBlood],
				[circlet, burningBlood],
				[blackBlood, plus],
				[plus],
				[]
			];
			RelicModel?[] vanillaWithoutHextech = withoutHextech.Select(ChooseStarter).ToArray();
			Expect(ChooseStarter([hex, burningBlood]) == hex, "native selection picks a leading hextech relic");
			Expect(ChooseStarter([hex]) == hex, "native selection picks the only hextech relic");

			Harmony harmony = new("HextechRunes.Tests.OrobasStarterFilter");
			try
			{
				harmony.CreateClassProcessor(typeof(TouchOfOrobasStarterHooks).GetNestedType("StarterRelicPatch", BindingFlags.NonPublic)!).Patch();
				PatchOrobasSettersWithoutHoverTips(harmony);
				Expect(ChooseStarter([hex, forge, burningBlood]) == burningBlood, "real starter after hextech relics is chosen");
				Expect(ChooseStarter([burningBlood, hex]) == burningBlood, "real starter before hextech relics is chosen");
				Expect(ChooseStarter([hex, plus]) == plus, "plus relic remains eligible for a further touch");
				Expect(ChooseStarter([hex, forge]) == null, "only hextech relics means no starter relic");
				RelicModel?[] patchedWithoutHextech = withoutHextech.Select(ChooseStarter).ToArray();
				for (int i = 0; i < withoutHextech.Length; i++)
				{
					Expect(ReferenceEquals(vanillaWithoutHextech[i], patchedWithoutHextech[i]), $"layout {i} without hextech relics matches native selection");
				}

				Player onlyHex = CreateOrobasTestPlayer([hex]);
				TouchOfOrobas refused = MutableOrobasModel<TouchOfOrobas>();
				Expect(!refused.SetupForPlayer(onlyHex), "event and bundle setup report no starter relic");
				Expect(refused.StarterRelic == null && refused.UpgradedRelic == null, "refused setup leaves no partial state");
				Expect(onlyHex.Relics.SequenceEqual([hex]), "hextech relic stays owned");

				Player mixed = CreateOrobasTestPlayer([hex, burningBlood]);
				TouchOfOrobas accepted = MutableOrobasModel<TouchOfOrobas>();
				Expect(accepted.SetupForPlayer(mixed), "setup succeeds with a real starter relic");
				Equal(burningBlood.Id, accepted.StarterRelic, "setup records the real starter relic");
				Equal(blackBlood.Id, accepted.UpgradedRelic, "setup records the native upgrade");
			}
			finally
			{
				harmony.UnpatchAll(harmony.Id);
			}
		});
	}

	[HextechTest]
	private static void OrobasObtainFallbackSkipsHextechRelics()
	{
		WithOrobasStarterModels(() =>
		{
			RelicModel hex = CreateMutableTestModel<NeurosurgeUpgradeRune>();
			RelicModel burningBlood = MutableOrobasModel<VanillaBurningBlood>();

			Harmony harmony = new("HextechRunes.Tests.OrobasObtainFallback");
			try
			{
				// 只替代 RelicCmd.Replace 的遗物栏与 UI 部分，保留欧洛巴斯之触自己的挑选与升级查询。
				harmony.Patch(AccessTools.Method(typeof(RelicCmd), nameof(RelicCmd.Replace), [typeof(RelicModel), typeof(RelicModel)]),
					prefix: new HarmonyMethod(typeof(Program), nameof(RecordOrobasReplacePrefix)));
				PatchOrobasSettersWithoutHoverTips(harmony);

				(RelicModel Original, RelicModel Replacement)? Obtain(RelicModel[] layout)
				{
					RecordedOrobasReplacements.Clear();
					TouchOfOrobas touch = MutableOrobasModel<TouchOfOrobas>();
					touch.Owner = CreateOrobasTestPlayer(layout);
					touch.AfterObtained().GetAwaiter().GetResult();
					return RecordedOrobasReplacements.Count == 0 ? null : RecordedOrobasReplacements.Single();
				}

				Expect(Obtain([hex]) is { } nativeOnlyHex && nativeOnlyHex.Original == hex && nativeOnlyHex.Replacement is Circlet,
					"native fallback turns the only hextech relic into a circlet");
				Expect(Obtain([hex, burningBlood]) is { } nativeMixed && nativeMixed.Original == hex,
					"native fallback replaces a leading hextech relic");

				harmony.CreateClassProcessor(typeof(TouchOfOrobasStarterHooks).GetNestedType("ObtainFallbackPatch", BindingFlags.NonPublic)!).Patch();
				Expect(Obtain([hex]) == null, "only hextech relics: replacement is skipped");
				Expect(Obtain([hex, burningBlood]) is { } mixed && mixed.Original == burningBlood && mixed.Replacement is BlackBlood,
					"real starter after a hextech relic is upgraded");
				Expect(Obtain([burningBlood, hex]) is { } leading && leading.Original == burningBlood && leading.Replacement is BlackBlood,
					"real starter before a hextech relic keeps native behavior");
				ExpectThrows<InvalidOperationException>(() => Obtain([]), "no starter relic at all keeps the native exception");
			}
			finally
			{
				harmony.UnpatchAll(harmony.Id);
			}
		});
	}

	[HextechTest]
	private static void OrobasBundleGrantSkipsTouchWithOnlyHextechRelics()
	{
		WithOrobasStarterModels(() =>
		{
			RelicModel hex = CreateMutableTestModel<NeurosurgeUpgradeRune>();
			Player owner = CreateOrobasTestPlayer([hex]);
			Harmony harmony = new("HextechRunes.Tests.OrobasBundleGrant");
			try
			{
				harmony.CreateClassProcessor(typeof(TouchOfOrobasStarterHooks).GetNestedType("StarterRelicPatch", BindingFlags.NonPublic)!).Patch();
				harmony.Patch(AccessTools.Method(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)]),
					prefix: new HarmonyMethod(typeof(Program), nameof(RecordOrobasObtainPrefix)));
				RecordedOrobasObtains.Clear();
				Task grant = RelicBundleGrantHelper.GrantRelics(owner, [typeof(TouchOfOrobas)]);
				Expect(grant.IsCompletedSuccessfully, "bundle grant finishes without awaiting an obtain");
				Expect(RecordedOrobasObtains.Count == 0, "touch of orobas is not obtained");
				Expect(owner.Relics.SequenceEqual([hex]), "hextech relic stays owned and nothing else is added");
			}
			finally
			{
				harmony.UnpatchAll(harmony.Id);
			}
		});
	}

	// 原版 StarterRelic / UpgradedRelic 的 setter 会读取悬浮提示与本地化，测试进程没有角色与文本表；
	// 只替代这部分，只写入字段。
	private static void PatchOrobasSettersWithoutHoverTips(Harmony harmony)
	{
		harmony.Patch(AccessTools.PropertySetter(typeof(TouchOfOrobas), nameof(TouchOfOrobas.StarterRelic)),
			prefix: new HarmonyMethod(typeof(Program), nameof(StoreOrobasStarterRelicPrefix)));
		harmony.Patch(AccessTools.PropertySetter(typeof(TouchOfOrobas), nameof(TouchOfOrobas.UpgradedRelic)),
			prefix: new HarmonyMethod(typeof(Program), nameof(StoreOrobasUpgradedRelicPrefix)));
	}

	private static bool StoreOrobasStarterRelicPrefix(TouchOfOrobas __instance, ModelId? value)
	{
		AccessTools.Field(typeof(TouchOfOrobas), "_starterRelic").SetValue(__instance, value);
		return false;
	}

	private static bool StoreOrobasUpgradedRelicPrefix(TouchOfOrobas __instance, ModelId? value)
	{
		AccessTools.Field(typeof(TouchOfOrobas), "_upgradedRelic").SetValue(__instance, value);
		return false;
	}

	private static bool RecordOrobasReplacePrefix(RelicModel original, RelicModel replace, ref Task<RelicModel> __result)
	{
		RecordedOrobasReplacements.Add((original, replace));
		__result = Task.FromResult(replace);
		return false;
	}

	private static bool RecordOrobasObtainPrefix(RelicModel relic, ref Task<RelicModel> __result)
	{
		RecordedOrobasObtains.Add(relic);
		__result = Task.FromResult(relic);
		return false;
	}

	private static Player CreateOrobasTestPlayer(RelicModel[] relics)
	{
		Player player = CreateOrdinalTestPlayer(1);
		AccessTools.Field(typeof(Player), "_relics").SetValue(player, relics.ToList());
		return player;
	}

	// 已注入 ModelDb 的模型按游戏路径从规范模型复制；直接构造会与注入登记冲突。
	private static T MutableOrobasModel<T>()
		where T : RelicModel
	{
		return (T)ModelDb.Relic<T>().ToMutable();
	}

	private static void WithOrobasStarterModels(Action action)
	{
		WithOrobasModels(() =>
		{
			using IDisposable models = InjectMissingModels(typeof(TouchOfOrobas));
			action();
		});
	}
}
