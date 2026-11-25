using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static readonly List<(Player Player, int Gold)> UpgradeGoldRewards = [];

	private static T UpgradeTestPower<T>(Creature owner, int amount) where T : PowerModel
	{
		T power = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
		SetAutoProperty(power, nameof(AbstractModel.IsMutable), true);
		AccessTools.Property(typeof(PowerModel), nameof(PowerModel.Owner)).SetValue(power, owner);
		AccessTools.Field(typeof(PowerModel), "_amount").SetValue(power, amount);
		return power;
	}

	[HextechTest]
	private static void RoyaltiesUpgradePaysImmediately()
	{
		WithImmediateGoldFixture((first, second, _, listener) =>
		{
			RoyaltiesUpgradeRune rune = CreateMutableTestModel<RoyaltiesUpgradeRune>();
			rune.Owner = first;
			RoyaltiesPower power = UpgradeTestPower<RoyaltiesPower>(first.Creature, 19);
			AccessTools.Field(typeof(Creature), "_powers").SetValue(first.Creature, new List<PowerModel> { power });
			rune.BeforeCombatStart().GetAwaiter().GetResult();
			rune.AfterPlayerTurnStart(null!, second).GetAwaiter().GetResult();
			Equal(0, listener.Calls, "teammate's turn does not grant royalties");
			int gold = first.Gold;
			rune.AfterPlayerTurnStart(null!, first).GetAwaiter().GetResult();
			rune.AfterPlayerTurnStart(null!, first).GetAwaiter().GetResult();
			Equal(gold + 38, first.Gold, "full royalties amount each turn, paid immediately with no compounding");
			Equal(19, power.Amount, "original combat-end royalties remain intact");
			rune.AfterCombatEnd((CombatRoom)RuntimeHelpers.GetUninitializedObject(typeof(CombatRoom))).GetAwaiter().GetResult();
			Equal(0, UpgradeGoldRewards.Count, "no battle-end duplicate");
		});
	}

	private static bool CaptureUpgradeGoldReward(Player player, Reward reward)
	{
		UpgradeGoldRewards.Add((player, ((GoldReward)reward).Amount));
		return false;
	}

	[HextechTest]
	private static void PlayerUpgradeKeywordsAndNoDrawStayOwnerScoped()
	{
		(HextechEnemyHexContext _, Player first, Player second) = CreatePrismaticEnemyFixture();
		BulletTimeUpgradeRune bullet = CreateMutableTestModel<BulletTimeUpgradeRune>(); bullet.Owner = first;
		BulletTime card = CreateMutableTestModel<BulletTime>(); card.Owner = first;
		NoDrawPower noDraw = UpgradeTestPower<NoDrawPower>(first.Creature, 1);
		Equal(0m, bullet.ModifyPowerAmountGivenMultiplicative(noDraw, first.Creature, 1, first.Creature, card), "suppress only Bullet Time's No Draw");
		Equal(1m, bullet.ModifyPowerAmountGivenMultiplicative(noDraw, first.Creature, 1, first.Creature, null), "other No Draw remains");
		BulletTime foreignBullet = CreateMutableTestModel<BulletTime>(); foreignBullet.Owner = second;
		Equal(1m, bullet.ModifyPowerAmountGivenMultiplicative(noDraw, second.Creature, 1, second.Creature, foreignBullet), "teammate unaffected");
		RebootUpgradeRune reboot = CreateMutableTestModel<RebootUpgradeRune>(); reboot.Owner = first;
		Reboot rebootCard = CreateMutableTestModel<Reboot>(); rebootCard.Owner = first;
		HashSet<CardKeyword> keywords = [CardKeyword.Exhaust];
		Expect(!reboot.TryModifyKeywordsInCombat(rebootCard, keywords) && keywords.Contains(CardKeyword.Exhaust), "upgraded Reboot keeps its native exhaust");
		MethodInfo rebootPrefix = AccessTools.Method(AccessTools.Inner(typeof(RebootUpgradeRune), "RebootSelectDrawPatch"), "Prefix");
		AccessTools.Field(typeof(Player), "_relics").SetValue(second, new List<RelicModel>());
		Reboot foreignReboot = CreateMutableTestModel<Reboot>(); foreignReboot.Owner = second;
		Equal(true, (bool)rebootPrefix.Invoke(null, [foreignReboot, null, null])!, "teammate without the rune keeps native random draw");
		keywords.Clear();
		HangUpgradeRune hang = CreateMutableTestModel<HangUpgradeRune>(); hang.Owner = first;
		Hang hangCard = CreateMutableTestModel<Hang>(); hangCard.Owner = first;
		keywords.Clear();
		Expect(hang.TryModifyKeywordsInCombat(hangCard, keywords) && keywords.Contains(CardKeyword.Exhaust), "upgraded Hang exhausts");
		HextechHangPower power = UpgradeTestPower<HextechHangPower>(second.Creature, 4);
		Equal(4m, power.ModifyDamageMultiplicativeCompat(second.Creature, 3, ValueProp.Unpowered, null, null), "Hang amplifies non-card damage");
		Equal(4m, power.ModifyDamageMultiplicativeCompat(second.Creature, 3, ValueProp.Move, first.Creature, card), "Hang amplifies other attacks");
		Equal(1m, power.ModifyDamageMultiplicativeCompat(first.Creature, 3, ValueProp.Move, second.Creature, card), "Hang is target scoped");
		Equal(8, 4 + HangUpgradeRune.NextIncrease(4), "repeated Hang doubles the multiplier");
	}

	[HextechTest]
	private static void ClawUpgradeSeparatesPermanentGrowthFromNativeCombatGrowth()
	{
		(HextechEnemyHexContext _, Player first, Player second) = CreatePrismaticEnemyFixture();
		ClawUpgradeRune rune = CreateMutableTestModel<ClawUpgradeRune>(); rune.Owner = first;
		Claw deck = CreateMutableTestModel<Claw>(); deck.Owner = first;
		Claw combat = CreateMutableTestModel<Claw>(); combat.Owner = first; combat.DeckVersion = deck;
		Claw otherCopy = CreateMutableTestModel<Claw>(); otherCopy.Owner = first; otherCopy.DeckVersion = deck;
		Claw foreign = CreateMutableTestModel<Claw>(); foreign.Owner = second;
		AccessTools.Method(typeof(Claw), "BuffFromClawPlay").Invoke(combat, [2m]);
		AccessTools.Method(typeof(Claw), "BuffFromClawPlay").Invoke(otherCopy, [2m]);
		rune.GrowClaws([deck, combat, combat, otherCopy, foreign]);
		Equal(4m, deck.DynamicVars.Damage.BaseValue, "deck grows once despite multiple combat copies");
		Equal(6m, combat.DynamicVars.Damage.BaseValue, "native plus-two is kept only in combat");
		Equal(6m, otherCopy.DynamicVars.Damage.BaseValue, "every current combat copy grows once");
		Equal(3m, foreign.DynamicVars.Damage.BaseValue, "teammate's Claw is unchanged");
		SerializableCard saved = new();
		Type store = typeof(HextechSelfUpgradeCardStore);
		AccessTools.Method(store.GetNestedType("ToSerializablePatch", BindingFlags.NonPublic), "Postfix").Invoke(null, [deck, saved]);
		Equal(1, saved.Props!.ints!.Single(p => p.name == HextechSelfUpgradeCardStore.DamageBonusSavedPropertyName).value, "save contains permanent one, not native combat two");
		Claw loaded = CreateMutableTestModel<Claw>(); loaded.Owner = first;
		AccessTools.Method(store.GetNestedType("FromSerializablePatch", BindingFlags.NonPublic), "Postfix").Invoke(null, [saved, loaded]);
		Equal(4m, loaded.DynamicVars.Damage.BaseValue, "loading restores only permanent growth");
	}

	[HextechTest]
	private static void PersistentPowerUpgradesDoNotAffectOtherPlayers()
	{
		(HextechEnemyHexContext _, Player first, Player second) = CreatePrismaticEnemyFixture();
		foreach (Player player in new[] { first, second })
		{
			SetAutoProperty(player.Creature, nameof(Creature.Player), player);
			AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
		}
		RageUpgradeRune rage = CreateMutableTestModel<RageUpgradeRune>(); rage.Owner = first;
		ReflectUpgradeRune reflect = CreateMutableTestModel<ReflectUpgradeRune>(); reflect.Owner = first;
		((List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(first)!).AddRange([rage, reflect]);
		foreach ((Type runeType, PowerModel power, PowerModel foreignPower) in new (Type, PowerModel, PowerModel)[]
		{
			(typeof(RageUpgradeRune), UpgradeTestPower<RagePower>(first.Creature, 3), UpgradeTestPower<RagePower>(second.Creature, 3)),
			(typeof(ReflectUpgradeRune), UpgradeTestPower<ReflectPower>(first.Creature, 3), UpgradeTestPower<ReflectPower>(second.Creature, 3))
		})
		{
			MethodInfo prefix = AccessTools.Method(runeType.GetNestedTypes(BindingFlags.NonPublic).Single(), "Prefix");
			object?[] args = [power, null];
			Equal(false, (bool)prefix.Invoke(null, args)!, "owner's cleanup is skipped");
			((Task)args[1]!).GetAwaiter().GetResult();
			Equal(true, (bool)prefix.Invoke(null, [foreignPower, null])!, "teammate keeps native cleanup");
		}
	}

	[HextechTest]
	private static void RegisteredCustomCardsUseOwnerPoolTokenContract()
	{
		Expect(typeof(HextechOwnerPoolTokenCard).IsAbstract, "owner-pool token card base should stay abstract");
		Expect(
			!HextechCustomModelRegistry.CustomCardTypes.Contains(typeof(HextechOwnerPoolTokenCard)),
			"owner-pool token card base must not enter the concrete model registry");
		Equal(
			HextechCustomModelRegistry.CustomCardTypes.Count,
			HextechCustomModelRegistry.CustomCardTypes.Count(
				static type => typeof(HextechOwnerPoolTokenCard).IsAssignableFrom(type) && !type.IsAbstract),
			"all registered custom cards should use the owner-pool token card contract");
	}

	[HextechTest]
	private static void CardUpgradePickupAndAvailabilityRules()
	{
		BloodlettingUpgradeRune singleForm = new();
		Expect(singleForm.MeetsCardAvailabilityRequirement([]), "ordinary card upgrade runes should not require the target card");

		BashUpgradeRune bash = new();
		NeutralizeUpgradeRune neutralize = new();
		FallingStarUpgradeRune fallingStar = new();
		UnleashUpgradeRune unleash = new();
		DualcastUpgradeRune dualcast = new();

		Expect(!bash.MeetsCardAvailabilityRequirement([]), "Bash upgrade should require Bash or Break");
		Expect(bash.MeetsCardAvailabilityRequirement([new Bash()]), "Bash upgrade should accept Bash");
		Expect(bash.MeetsCardAvailabilityRequirement([new Break()]), "Bash upgrade should accept Break");
		Expect(!neutralize.MeetsCardAvailabilityRequirement([]), "Neutralize upgrade should require Neutralize or Suppress");
		Expect(neutralize.MeetsCardAvailabilityRequirement([new Neutralize()]), "Neutralize upgrade should accept Neutralize");
		Expect(neutralize.MeetsCardAvailabilityRequirement([new Suppress()]), "Neutralize upgrade should accept Suppress");
		Expect(!fallingStar.MeetsCardAvailabilityRequirement([]), "Falling Star upgrade should require Falling Star or Meteor Shower");
		Expect(fallingStar.MeetsCardAvailabilityRequirement([new FallingStar()]), "Falling Star upgrade should accept Falling Star");
		Expect(fallingStar.MeetsCardAvailabilityRequirement([new MeteorShower()]), "Falling Star upgrade should accept Meteor Shower");
		Expect(!unleash.MeetsCardAvailabilityRequirement([]), "Unleash upgrade should require Unleash or Protector");
		Expect(unleash.MeetsCardAvailabilityRequirement([new Unleash()]), "Unleash upgrade should accept Unleash");
		Expect(unleash.MeetsCardAvailabilityRequirement([new Protector()]), "Unleash upgrade should accept Protector");
		Expect(!dualcast.MeetsCardAvailabilityRequirement([]), "Dualcast upgrade should require Dualcast or Quadcast");
		Expect(dualcast.MeetsCardAvailabilityRequirement([new Dualcast()]), "Dualcast upgrade should accept Dualcast");
		Expect(dualcast.MeetsCardAvailabilityRequirement([new Quadcast()]), "Dualcast upgrade should accept Quadcast");

		Expect(!StrikeUpgradeRune.HasBasicStrike([]), "Strike upgrade should require a basic Strike");
		Expect(StrikeUpgradeRune.HasBasicStrike([new StrikeIronclad()]), "Strike upgrade should accept a basic Strike");
		Expect(!DefendUpgradeRune.HasBasicDefend([]), "Defend upgrade should require a basic Defend");
		Expect(DefendUpgradeRune.HasBasicDefend([new DefendIronclad()]), "Defend upgrade should accept a basic Defend");
	}

	[HextechTest]
	private static void BashUpgradeStrengthMatchesVulnerableApplied()
	{
		Bash bash = CreateMutableTestModel<Bash>();
		Equal(2m, BashUpgradeRune.CalculateStrengthGain(bash), "base Bash vulnerable and Strength");
		CardCmd.Upgrade(bash);
		Equal(3m, BashUpgradeRune.CalculateStrengthGain(bash), "upgraded Bash vulnerable and Strength");

		Break breakCard = CreateMutableTestModel<Break>();
		Equal(5m, BashUpgradeRune.CalculateStrengthGain(breakCard), "base Break vulnerable and Strength");
		CardCmd.Upgrade(breakCard);
		Equal(7m, BashUpgradeRune.CalculateStrengthGain(breakCard), "upgraded Break vulnerable and Strength");

		Equal(0m, BashUpgradeRune.CalculateStrengthGain(new StrikeIronclad()), "unrelated card Strength");
	}

	[HextechTest]
	private static void StarterUpgradeCapsTerminateExternalUpgradeToMaxLoops()
	{
		Equal(
			999,
			HextechStarterUpgradeHooks.ResolveOwnedMaxUpgradeLevel(0),
			"owned basic cards with the matching rune use the +999 cap");
		Equal(
			1001,
			HextechStarterUpgradeHooks.ResolveOwnedMaxUpgradeLevel(1001),
			"owned legacy over-cap cards remain loadable but cannot grow further");
		Equal(
			1,
			HextechStarterUpgradeHooks.ResolveUnownedMaxUpgradeLevel(0, isDeserializing: false),
			"new unowned cards keep the vanilla cap");
		Equal(
			1,
			HextechStarterUpgradeHooks.ResolveUnownedMaxUpgradeLevel(998, isDeserializing: false),
			"ordinary unowned cards do not inherit the rune cap");
		Equal(
			1001,
			HextechStarterUpgradeHooks.ResolveUnownedMaxUpgradeLevel(1000, isDeserializing: true),
			"legacy over-cap saves can replay the next upgrade level");
	}

	[HextechTest]
	private static void CreativeAiUpgradeRuneUpgradesGeneratedPowerCards()
	{
		CreativeAi card = CreateMutableTestModel<CreativeAi>();

		Expect(CreativeAiUpgradeRune.UpgradeGeneratedCard(card), "Creative AI should generate an upgraded Power card");
		Equal(1, card.CurrentUpgradeLevel, "Creative AI generated card upgrade level");
		Expect(!CreativeAiUpgradeRune.UpgradeGeneratedCard(card), "an already upgraded generated card should not be upgraded twice");

		ExpectCombatGenerationFilters(
			GetAsyncStateMachineMoveNext(typeof(BlankCheckRune).GetMethod(nameof(BlankCheckRune.AfterPlayerTurnStart))!),
			nameof(BlankCheckRune));
		ExpectCombatGenerationFilters(
			GetAsyncStateMachineMoveNext(typeof(MindOverMatterRune).GetMethod(nameof(MindOverMatterRune.BeforeHandDraw))!),
			nameof(MindOverMatterRune));
		ExpectCombatGenerationFilters(
			GetAsyncStateMachineMoveNext(typeof(SingularityAIRune).GetMethod(nameof(SingularityAIRune.BeforeHandDraw))!),
			nameof(SingularityAIRune));
		ExpectCombatGenerationFilters(
			typeof(CorruptedBranchRune).GetMethod("CreateRandomCombatCard", BindingFlags.Instance | BindingFlags.NonPublic)!,
			nameof(CorruptedBranchRune));
		ExpectCombatGenerationFilters(
			typeof(ColorDiscoveryRune).GetMethod("GetOtherCharacterCards", BindingFlags.NonPublic | BindingFlags.Static)!,
			nameof(ColorDiscoveryRune));
	}

	[HextechTest]
	private static void SubroutineUpgradeCombatMoveGateResetsAcrossCombats()
	{
		SubroutineUpgradeRune rune = new();

		Expect(rune.TryConsumeCombatStartMove(), "first combat-start move should be consumed");
		Expect(!rune.TryConsumeCombatStartMove(), "same combat should reject a second move");

		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Expect(rune.TryConsumeCombatStartMove(), "combat start should reset the move gate");

		rune.AfterCombatEnd(null!).GetAwaiter().GetResult();
		Expect(rune.TryConsumeCombatStartMove(), "combat end should clear the move gate");
	}

	[HextechTest]
	private static void PactsEndUpgradeDamageScalesWithExhaustPile()
	{
		Equal(0m, PactsEndUpgradeRune.CalculateBonusDamage(0, 6m), "empty exhaust pile bonus");
		Equal(30m, PactsEndUpgradeRune.CalculateBonusDamage(5, 6m), "five-card exhaust pile bonus");
	}

	[HextechTest]
	private static void BrandUpgradeDamageScalesWithPermanentPlayCount()
	{
		Equal(1m, BrandUpgradeRune.CalculateDamageMultiplier(0, BrandUpgradeRune.DamagePercentPerBrand), "zero brand plays");
		Equal(1.03m, BrandUpgradeRune.CalculateDamageMultiplier(1, BrandUpgradeRune.DamagePercentPerBrand), "one brand play");
		Equal(1.30m, BrandUpgradeRune.CalculateDamageMultiplier(10, BrandUpgradeRune.DamagePercentPerBrand), "ten brand plays");
	}

	[HextechTest]
	private static void NewCardUpgradeRunesUseExpectedTriggerRules()
	{
		Equal(0, ThornmailRune.CalculateThorns(19m), "Thornmail should floor partial Max HP steps");
		Equal(1, ThornmailRune.CalculateThorns(20m), "Thornmail should grant one Thorns per twenty Max HP");
		Equal(4, ThornmailRune.CalculateThorns(99m), "Thornmail should have no legacy bonus cap");
		Player waveOwner = CreateOrdinalTestPlayer(1);
		CorrosiveWave ownWave = CreateMutableTestModel<CorrosiveWave>();
		CorrosiveWave teammateWave = CreateMutableTestModel<CorrosiveWave>();
		StrikeIronclad ownStrike = CreateMutableTestModel<StrikeIronclad>();
		ownWave.Owner = ownStrike.Owner = waveOwner;
		teammateWave.Owner = CreateOrdinalTestPlayer(2);
		Expect(CorrosiveWaveUpgradeRune.GrantsExhaust(ownWave, waveOwner), "the owner's Corrosive Wave gains the Exhaust keyword");
		Expect(!CorrosiveWaveUpgradeRune.GrantsExhaust(teammateWave, waveOwner), "a teammate's Corrosive Wave is untouched");
		Expect(!CorrosiveWaveUpgradeRune.GrantsExhaust(ownStrike, waveOwner), "Corrosive Wave upgrade should not exhaust other cards");
		Expect(StormUpgradeRune.ShouldTrigger(CardType.Power, hasUpgradeRune: false), "vanilla Storm should still trigger for Power cards");
		Expect(!StormUpgradeRune.ShouldTrigger(CardType.Attack, hasUpgradeRune: false), "vanilla Storm should ignore Attacks");
		Expect(StormUpgradeRune.ShouldTrigger(CardType.Attack, hasUpgradeRune: true), "upgraded Storm should trigger for Attacks");
		Expect(StormUpgradeRune.ShouldTrigger(CardType.Skill, hasUpgradeRune: true), "upgraded Storm should trigger for Skills");
		Equal(3, DecisionsDecisionsUpgradeRune.AddRequestedPlayCount(1, 3), "Decisions should resolve all three plays inside one card-play wrapper");
		Equal(4, DecisionsDecisionsUpgradeRune.AddRequestedPlayCount(2, 3), "Decisions replay count should combine additively with another replay");
	}

	[HextechTest]
	private static void HiddenGemUpgradeMovesNewReplayTargetToHand()
	{
		StrikeIronclad target = CreateMutableTestModel<StrikeIronclad>();
		Expect(
			HiddenGemUpgradeRune.IsEligibleReplayTarget(target),
			"Hidden Gem should accept a playable card without Replay");

		target.BaseReplayCount = 1;
		Expect(
			!HiddenGemUpgradeRune.IsEligibleReplayTarget(target),
			"Hidden Gem should retain the vanilla restriction against cards that already have Replay");
		Equal(PileType.Hand, HiddenGemUpgradeRune.ReplayTargetPile, "Hidden Gem upgraded replay target pile");
	}

	// 升级类海克斯的可用性还会看牌组，角色测试玩家默认没有牌组。
	private static Player CreateUpgradeAvailabilityTestPlayer(Type characterType)
	{
		Player player = CreateCharacterTestPlayer(characterType);
		SetAutoProperty(player, nameof(Player.Deck), new CardPile(PileType.Deck));
		return player;
	}

	/// <summary>升级：慷慨捐助仅联机、仅储君；只把这次慷慨捐助为目标玩家加入手牌的那一张设为本回合免费。</summary>
	[HextechTest]
	private static void LargesseUpgradeIsMultiplayerRegentOnlyAndFreesTheGrantedCard()
	{
		LargesseUpgradeRune availability = CreateMutableTestModel<LargesseUpgradeRune>();
		Player regent = CreateUpgradeAvailabilityTestPlayer(typeof(Regent));
		Player ironclad = CreateUpgradeAvailabilityTestPlayer(typeof(Ironclad));
		WithFeedbackNetGameType(NetGameType.Singleplayer, () => Expect(!availability.IsAvailableForPlayer(regent), "single-player Regent cannot get Upgrade: Largesse"));
		WithFeedbackNetGameType(NetGameType.Host, () =>
		{
			Expect(availability.IsAvailableForPlayer(regent), "multiplayer Regent can get Upgrade: Largesse");
			Expect(!availability.IsAvailableForPlayer(ironclad), "multiplayer non-Regent cannot get Upgrade: Largesse");
		});

		(HextechEnemyHexContext _, Player owner, Player teammate) = CreatePrismaticEnemyFixture();
		LargesseUpgradeRune rune = CreateMutableTestModel<LargesseUpgradeRune>();
		rune.Owner = owner;
		foreach (Player player in new[] { owner, teammate })
		{
			SetAutoProperty(player.Creature, nameof(Creature.Player), player);
		}

		Largesse largesse = CreateMutableTestModel<Largesse>();
		largesse.Owner = owner;
		CardPlay play = CreateCardPlay(largesse, isAutoPlay: false);
		AccessTools.Property(typeof(CardPlay), nameof(CardPlay.Target)).SetValue(play, teammate.Creature);
		StrikeIronclad UpgradeGrantedCard(Player cardOwner, PileType pileType)
		{
			StrikeIronclad card = CreateMutableTestModel<StrikeIronclad>();
			card.Owner = cardOwner;
			pileType.GetPile(cardOwner).AddInternal(card);
			return card;
		}

		StrikeIronclad unrelated = UpgradeGrantedCard(teammate, PileType.Hand);
		rune.AfterCardGeneratedForCombat(unrelated, owner).GetAwaiter().GetResult();
		Equal(1, unrelated.EnergyCost.GetWithModifiers(CostModifiers.Local), "cards generated outside Largesse keep their cost");

		rune.BeforeCardPlayed(play).GetAwaiter().GetResult();
		StrikeIronclad foreignCreator = UpgradeGrantedCard(teammate, PileType.Hand);
		rune.AfterCardGeneratedForCombat(foreignCreator, teammate).GetAwaiter().GetResult();
		Equal(1, foreignCreator.EnergyCost.GetWithModifiers(CostModifiers.Local), "a card the recipient created themselves is not the gift");
		StrikeIronclad ownCard = UpgradeGrantedCard(owner, PileType.Hand);
		rune.AfterCardGeneratedForCombat(ownCard, owner).GetAwaiter().GetResult();
		Equal(1, ownCard.EnergyCost.GetWithModifiers(CostModifiers.Local), "a card for the holder is not the gift");
		StrikeIronclad gift = UpgradeGrantedCard(teammate, PileType.Hand);
		rune.AfterCardGeneratedForCombat(gift, owner).GetAwaiter().GetResult();
		Equal(0, gift.EnergyCost.GetWithModifiers(CostModifiers.Local), "the card Largesse adds to the teammate's hand is free this turn");
		StrikeIronclad second = UpgradeGrantedCard(teammate, PileType.Hand);
		rune.AfterCardGeneratedForCombat(second, owner).GetAwaiter().GetResult();
		Equal(1, second.EnergyCost.GetWithModifiers(CostModifiers.Local), "only the first matching card per Largesse play is freed");
		rune.AfterCardPlayed(null!, play).GetAwaiter().GetResult();

		Largesse foreignLargesse = CreateMutableTestModel<Largesse>();
		foreignLargesse.Owner = teammate;
		CardPlay foreignPlay = CreateCardPlay(foreignLargesse, isAutoPlay: false);
		AccessTools.Property(typeof(CardPlay), nameof(CardPlay.Target)).SetValue(foreignPlay, owner.Creature);
		rune.BeforeCardPlayed(foreignPlay).GetAwaiter().GetResult();
		StrikeIronclad teammateGift = UpgradeGrantedCard(owner, PileType.Hand);
		rune.AfterCardGeneratedForCombat(teammateGift, teammate).GetAwaiter().GetResult();
		Equal(1, teammateGift.EnergyCost.GetWithModifiers(CostModifiers.Local), "a teammate's Largesse is not upgraded by this holder");
	}

	/// <summary>升级：群情激愤仅联机、仅铁甲战士；加成 = 本场战斗任意玩家打出群情激愤的次数（含复制品与重放），只加在群情激愤的强化攻击上。</summary>
	[HextechTest]
	private static void OutrageUpgradeIsMultiplayerIroncladOnlyAndScalesWithEveryonesPlays()
	{
		OutrageUpgradeRune availability = CreateMutableTestModel<OutrageUpgradeRune>();
		Player ironclad = CreateUpgradeAvailabilityTestPlayer(typeof(Ironclad));
		Player silent = CreateUpgradeAvailabilityTestPlayer(typeof(Silent));
		WithFeedbackNetGameType(NetGameType.Singleplayer, () => Expect(!availability.IsAvailableForPlayer(ironclad), "single-player Ironclad cannot get Upgrade: Outrage"));
		WithFeedbackNetGameType(NetGameType.Host, () =>
		{
			Expect(availability.IsAvailableForPlayer(ironclad), "multiplayer Ironclad can get Upgrade: Outrage");
			Expect(!availability.IsAvailableForPlayer(silent), "multiplayer non-Ironclad cannot get Upgrade: Outrage");
		});

		(HextechEnemyHexContext _, Player owner, Player teammate) = CreatePrismaticEnemyFixture();
		OutrageUpgradeRune rune = CreateMutableTestModel<OutrageUpgradeRune>();
		rune.Owner = owner;
		Outrage own = CreateMutableTestModel<Outrage>();
		own.Owner = owner;
		Outrage copy = CreateMutableTestModel<Outrage>();
		copy.Owner = teammate;
		StrikeIronclad strike = CreateMutableTestModel<StrikeIronclad>();
		strike.Owner = owner;
		WithFeedbackCombatHistory((history, entries) =>
		{
			Equal(0m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, owner.Creature, own), "no bonus before any Outrage is played");
			RecordFeedbackPlayFinished(history, entries, own, playIndex: 0, playCount: 1, isAutoPlay: false);
			Equal(1m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, owner.Creature, own), "the holder's play adds one");
			Equal(1m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, teammate.Creature, copy), "a teammate's copy sees the same bonus");
			RecordFeedbackPlayFinished(history, entries, copy, playIndex: 0, playCount: 2, isAutoPlay: false);
			RecordFeedbackPlayFinished(history, entries, copy, playIndex: 1, playCount: 2, isAutoPlay: false);
			Equal(3m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, owner.Creature, own), "a teammate's play and its replay each add one");
			RecordFeedbackPlayFinished(history, entries, strike, playIndex: 0, playCount: 1, isAutoPlay: false);
			Equal(3m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, owner.Creature, own), "other cards do not count");
			Equal(0m, rune.ModifyDamageAdditiveCompat(null, 6m, ValueProp.Move, owner.Creature, strike), "other cards gain nothing");
			Equal(0m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Unpowered, owner.Creature, own), "only powered attacks are increased, as Momentum");
		});
		Equal(0m, rune.ModifyDamageAdditiveCompat(null, 9m, ValueProp.Move, owner.Creature, own), "the bonus ends with the combat history");
	}
}
