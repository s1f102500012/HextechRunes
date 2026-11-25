using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 吸血习性：持有者只能靠本符文回血；濒死复活类回血（当前生命为 0）与队友、宠物不受影响。
	[HextechTest]
	private static void VampirismBlocksOtherHealingForItsOwnerOnly()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		Player teammate = CreateOrdinalTestPlayer(2);
		Creature ownerCreature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		Creature teammateCreature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		Creature pet = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(owner, nameof(Player.Creature), ownerCreature);
		SetAutoProperty(teammate, nameof(Player.Creature), teammateCreature);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(ownerCreature, 30);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(teammateCreature, 30);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(pet, 10);
		AccessTools.Field(typeof(Player), "_relics").SetValue(owner, new List<RelicModel> { CreateMutableTestModel<DeathHarvestRune>() });
		AccessTools.Field(typeof(Player), "_relics").SetValue(teammate, new List<RelicModel>());

		Expect(DeathHarvestRune.BlocksHeal(owner, ownerCreature), "other healing on the owner is blocked");
		Expect(!DeathHarvestRune.BlocksHeal(owner, pet), "the owner's pet still heals");
		Expect(!DeathHarvestRune.BlocksHeal(teammate, teammateCreature), "teammates without the rune still heal");
		Expect(!DeathHarvestRune.BlocksHeal(null, pet), "creatures without a player are unaffected");
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(ownerCreature, 0);
		Expect(!DeathHarvestRune.BlocksHeal(owner, ownerCreature), "revive heals at 0 HP are not swallowed");
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(ownerCreature, 30);

		var ownHeal = AccessTools.Field(typeof(DeathHarvestRune), "_isOwnHeal");
		ownHeal.SetValue(null, true);
		try
		{
			Expect(!DeathHarvestRune.BlocksHeal(owner, ownerCreature), "the rune's own heal passes");
		}
		finally
		{
			ownHeal.SetValue(null, false);
		}

		Equal(25m, CreateMutableTestModel<DeathHarvestRune>().DynamicVars["HealPercent"].BaseValue, "player heal is 25% of unblocked damage");
	}

	// 科学狂人：球数超过原版上限 10 时，每个球触发后的等待按 10/球数 压缩，整串总时长与 10 个球时相同。
	[HextechTest]
	private static void MadScientistOrbWaitKeepsTenOrbTotalDuration()
	{
		Equal(1f, MadScientistRune.ComputeOrbWaitScale(0), "no orbs keep vanilla timing");
		Equal(1f, MadScientistRune.ComputeOrbWaitScale(10), "ten orbs keep vanilla timing");
		Equal(0.5f, MadScientistRune.ComputeOrbWaitScale(20), "twenty orbs halve each wait");
		foreach (int count in new[] { 11, 17, 40, 123 })
		{
			Expect(Math.Abs(MadScientistRune.ComputeOrbWaitScale(count) * count - 10f) < 0.001f, $"{count} orbs take as long as ten");
		}
	}

	// 大地苏醒：每抽 8 张牌新增一块初始 5 层的滚石。
	[HextechTest]
	private static void EarthAwakensAddsFiveLayerBoulderEveryEightDraws()
	{
		EarthAwakensRune rune = CreateMutableTestModel<EarthAwakensRune>();
		Equal(8, rune.DynamicVars["CardsNeeded"].IntValue, "draw threshold");
		Equal(5m, rune.DynamicVars["RollingBoulderPower"].BaseValue, "initial boulder layers");
		Expect(rune is DrawThresholdRuneBase, "uses the shared per-combat draw counter");
	}

	// 升级打击/防御：0.113.x 及更早，镜中倒影与敌方“神化”的降级作用域里只降一级，作用域外（魔法骑士等）照原版清零；
	// 0.114.0 起降级幅度完全跟随原版（每次降一级，清零走 CardCmd.RemoveAllUpgrades），模组不再改写。
	[HextechTest]
	private static void UnlimitedStarterCardsStepDownOneLevelInScope()
	{
#if !STS2_114_OR_NEWER
		Equal(2, HextechStarterUpgradeHooks.ResolveSteppedDowngradeLevel(3, inStepDownScope: true, hasUnlockedLevels: true), "steps down one level");
		Equal(0, HextechStarterUpgradeHooks.ResolveSteppedDowngradeLevel(1, inStepDownScope: true, hasUnlockedLevels: true), "level one clears as usual");
		Equal(0, HextechStarterUpgradeHooks.ResolveSteppedDowngradeLevel(3, inStepDownScope: false, hasUnlockedLevels: true), "outside the scope clears");
		Equal(0, HextechStarterUpgradeHooks.ResolveSteppedDowngradeLevel(3, inStepDownScope: true, hasUnlockedLevels: false), "cards without the rune clear");
#endif

		using IDisposable models = InjectMissingModels(typeof(StrikeIronclad), typeof(DefendIronclad));
		Harmony harmony = new("HextechRunes.Tests.StarterStepDown");
		try
		{
			harmony.CreateClassProcessor(AccessTools.Inner(typeof(HextechStarterUpgradeHooks), "MaxUpgradeLevelPatch")).Patch();
#if !STS2_114_OR_NEWER
			harmony.CreateClassProcessor(AccessTools.Inner(typeof(HextechStarterUpgradeHooks), "SteppedDowngradePatch")).Patch();
#endif
			RunStarterStepDownScenario();
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}
	}

	private static void RunStarterStepDownScenario()
	{
		Player owner = CreateOrdinalTestPlayer(1);
		AccessTools.Field(typeof(Player), "_relics").SetValue(owner, new List<RelicModel> { CreateMutableTestModel<StrikeUpgradeRune>() });
		// 卡牌不在任何牌堆里：原版 CardCmd.Downgrade 查 Pile 时遍历持有者的牌堆。
		AccessTools.Field(typeof(Player), "_runPiles").SetValue(owner, Array.Empty<CardPile>());
		CardModel strike = ModelDb.GetById<CardModel>(ModelDb.GetId<StrikeIronclad>()).ToMutable();
		CardModel defend = ModelDb.GetById<CardModel>(ModelDb.GetId<DefendIronclad>()).ToMutable();
		strike.Owner = defend.Owner = owner;
		CardModel reference = ModelDb.GetById<CardModel>(ModelDb.GetId<StrikeIronclad>()).ToMutable();
		reference.Owner = owner;
		for (int i = 0; i < 3; i++)
		{
			strike.UpgradeInternal();
			strike.FinalizeUpgradeInternal();
		}
		for (int i = 0; i < 2; i++)
		{
			reference.UpgradeInternal();
			reference.FinalizeUpgradeInternal();
		}
		defend.UpgradeInternal();
		defend.FinalizeUpgradeInternal();

		Expect(HextechStarterUpgradeHooks.HasUnlockedUpgradeLevels(strike), "strike with Strike upgrade has unlocked levels");
		Expect(!HextechStarterUpgradeHooks.HasUnlockedUpgradeLevels(defend), "defend without Defend upgrade keeps vanilla levels");
		HextechStarterUpgradeHooks.DowngradeOneLevel(strike);
		Equal(2, strike.CurrentUpgradeLevel, "Strike+3 becomes Strike+2");
		Equal(reference.DynamicVars.Damage.BaseValue, strike.DynamicVars.Damage.BaseValue, "damage matches a card upgraded twice");
		HextechStarterUpgradeHooks.DowngradeOneLevel(defend);
		Equal(0, defend.CurrentUpgradeLevel, "ordinary upgraded cards still clear");
#if STS2_114_OR_NEWER
		MegaCrit.Sts2.Core.Commands.CardCmd.Downgrade(strike);
		Equal(1, strike.CurrentUpgradeLevel, "vanilla downgrades step down one level too");
		MegaCrit.Sts2.Core.Commands.CardCmd.RemoveAllUpgrades(strike);
		Equal(0, strike.CurrentUpgradeLevel, "Dampen clears through RemoveAllUpgrades");
#else
		MegaCrit.Sts2.Core.Commands.CardCmd.Downgrade(strike);
		Equal(0, strike.CurrentUpgradeLevel, "downgrades outside the scope (Dampen) still clear");
#endif
	}
}
