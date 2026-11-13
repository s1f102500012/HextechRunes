using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using VanillaBurningBlood = MegaCrit.Sts2.Core.Models.Relics.BurningBlood;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void OrobasSecondUpgradePreservesNativeAndForeignMappings()
	{
		WithOrobasModels(() =>
		{
			(Type Starter, Type Ancient, Type Plus)[] upgrades =
			[
				(typeof(VanillaBurningBlood), typeof(BlackBlood), typeof(HextechBlackBloodPlus)),
				(typeof(RingOfTheSnake), typeof(RingOfTheDrake), typeof(HextechRingOfTheDrakePlus)),
				(typeof(DivineRight), typeof(DivineDestiny), typeof(HextechDivineDestinyPlus)),
				(typeof(BoundPhylactery), typeof(PhylacteryUnbound), typeof(HextechPhylacteryUnboundPlus)),
				(typeof(CrackedCore), typeof(InfusedCore), typeof(HextechInfusedCorePlus))
			];
			TouchOfOrobas touch = new();
			foreach ((Type Starter, Type Ancient, Type Plus) row in upgrades)
			{
				RelicModel starter = ModelDb.GetById<RelicModel>(ModelDb.GetId(row.Starter));
				RelicModel ancient = touch.GetUpgradedStarterRelic(starter);
				Equal(row.Ancient, ancient.GetType(), "first upgrade remains native");
				Expect(ReferenceEquals(ancient, OrobasPlusUpgrades.ResolveUpgrade(starter, ancient, true)), "existing native mapping is preserved");
				RelicModel fallback = touch.GetUpgradedStarterRelic(ancient);
				Expect(fallback is Circlet, "reproduces native second-upgrade fallback");
				Expect(ReferenceEquals(fallback, OrobasPlusUpgrades.ResolveUpgrade(ancient, fallback, false)), "disabled run leaves native behavior intact");
				RelicModel plus = OrobasPlusUpgrades.ResolveUpgrade(ancient, fallback, true);
				Equal(row.Plus, plus.GetType(), "second upgrade resolves to the matching plus relic");
				Expect(ReferenceEquals(plus, OrobasPlusUpgrades.ResolveUpgrade(plus, fallback, false)), "third touch cannot turn an existing plus relic into a circlet");
				Expect(ReferenceEquals(starter, OrobasPlusUpgrades.ResolveUpgrade(ancient, starter, true)), "another mod's non-circlet result wins");
			}
			RelicModel unknown = ModelDb.Relic<Circlet>();
			Expect(ReferenceEquals(unknown, OrobasPlusUpgrades.ResolveUpgrade(unknown, unknown, true)), "unknown relic types are untouched");

			// 在真正的原版入口挂上同一个补丁，验证图鉴无 Owner 调用及第三次升级，而不模拟 Godot 奖励 UI。
			Harmony harmony = new("HextechRunes.Tests.OrobasPlus");
			try
			{
				harmony.CreateClassProcessor(typeof(OrobasPlusUpgrades).GetNestedType("UpgradePatch", BindingFlags.NonPublic)!).Patch();
				Expect(touch.GetUpgradedStarterRelic(ModelDb.Relic<VanillaBurningBlood>()) is BlackBlood, "compendium first-upgrade query stays native");
				Expect(touch.GetUpgradedStarterRelic(ModelDb.Relic<HextechBlackBloodPlus>()) is HextechBlackBloodPlus, "native entry preserves an existing plus form");

				// 玩家复现是无 Mayhem modifier 的单机旧局，通过控制台获得欧洛巴斯之触。
				// 走实际补丁而非直接传 active=true，避免漏掉启用判断；配置只改测试进程内存。
				using (ModEnabledOverride modSwitch = new())
				{
					RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
					typeof(RunState).GetProperty(nameof(RunState.Modifiers))!.SetValue(run, Array.Empty<ModifierModel>());
					Player owner = CreateOrdinalTestPlayer(1);
					typeof(Player).GetField("_runState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, run);
					RelicModel ownedAncient = ModelDb.Relic<BlackBlood>().ToMutable();
					ownedAncient.Owner = owner;
					modSwitch.Enabled = true;
					Expect(touch.GetUpgradedStarterRelic(ownedAncient) is HextechBlackBloodPlus, "enabled single-player run without Mayhem modifier upgrades at native entry");
					modSwitch.Enabled = false;
					Expect(touch.GetUpgradedStarterRelic(ownedAncient) is Circlet, "disabled single-player run still keeps native behavior");
				}
			}
			finally
			{
				harmony.UnpatchAll(harmony.Id);
			}
		});
	}

	[HextechTest]
	private static void OrobasPlusDrawAndLightningStayOwnerScoped()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		Player foreign = CreateOrdinalTestPlayer(2);
		PlayerCombatState combat = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
		typeof(Player).GetProperty(nameof(Player.PlayerCombatState))!.SetValue(owner, combat);
		HextechRingOfTheDrakePlus ring = CreateMutableTestModel<HextechRingOfTheDrakePlus>();
		ring.Owner = owner;
		foreach (int turn in new[] { 1, 5, 6 })
		{
			typeof(PlayerCombatState).GetProperty(nameof(PlayerCombatState.TurnNumber))!.SetValue(combat, turn);
			Equal(turn <= 5 ? 7m : 5m, ring.ModifyHandDraw(owner, 5m), "extra draw expires after the fifth turn");
			Equal(5m, ring.ModifyHandDraw(foreign, 5m), "teammate draw is unchanged");
		}
		HextechInfusedCorePlus core = CreateMutableTestModel<HextechInfusedCorePlus>();
		core.Owner = owner;
		LightningOrb lightning = CreateMutableTestModel<LightningOrb>();
		lightning.Owner = owner;
		Equal(5m, core.ModifyOrbValue(lightning, 3m), "passive damage gains two");
		Equal(10m, core.ModifyOrbValue(lightning, 8m), "evoke damage gains two");
		LightningOrb foreignLightning = CreateMutableTestModel<LightningOrb>();
		foreignLightning.Owner = foreign;
		Equal(3m, core.ModifyOrbValue(foreignLightning, 3m), "teammate lightning is unchanged");
		FrostOrb frost = CreateMutableTestModel<FrostOrb>();
		frost.Owner = owner;
		Equal(2m, core.ModifyOrbValue(frost, 2m), "other orb types are unchanged");
	}

	[HextechTest]
	private static void OrobasPlusUsesNativeAssetsAndStaysOutOfShops()
	{
		WithOrobasModels(() =>
		{
			foreach (Type type in HextechCustomModelRegistry.EventRelicTypes.Where(type => typeof(OrobasPlusRelicBase).IsAssignableFrom(type)))
			{
				RelicModel plus = ModelDb.GetById<RelicModel>(ModelDb.GetId(type));
				RelicModel original = (RelicModel)type.GetProperty("OriginalRelic", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plus)!;
				Equal(original.PackedIconPath, plus.PackedIconPath, "native icon atlas path");
				Expect(!plus.IsAllowedInShops && !HextechContentRegistry.AllCustomRelicTypes.Contains(type), "plus forms are excluded from shops and shared random registration");
			}
		});
	}

	private static void WithOrobasModels(Action action)
	{
		Type[] types =
		[
			typeof(VanillaBurningBlood), typeof(BlackBlood), typeof(RingOfTheSnake), typeof(RingOfTheDrake),
			typeof(DivineRight), typeof(DivineDestiny), typeof(BoundPhylactery), typeof(PhylacteryUnbound),
			typeof(CrackedCore), typeof(InfusedCore), typeof(Circlet),
#if STS2_113_OR_NEWER
			// 0.113.0 起 TouchOfOrobas 的初始遗物映射含愚者的 SongBow → Storyteller,构造映射表要求两者已注册。
			typeof(SongBow), typeof(Storyteller),
#endif
			.. HextechCustomModelRegistry.EventRelicTypes.Where(type => typeof(OrobasPlusRelicBase).IsAssignableFrom(type))
		];
		using IDisposable models = InjectMissingModels(types);
		action();
	}
}
