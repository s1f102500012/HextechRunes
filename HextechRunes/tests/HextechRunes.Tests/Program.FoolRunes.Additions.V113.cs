#if STS2_113_OR_NEWER
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

// 第三批愚者海克斯（渐入佳境、引而不发、悲歌、升级：快速射击 / 歌之道 / 暴力和弦、弦歌不辍）与悲喜交加改为加入手牌。
// 箭矢、瞄准走 FoolArrowCombat 的真实 PowerCmd 数值路径；旋律、抽牌、格挡、伤害、拉伸用 FoolMelodyTestScope 只记录调用。
internal static partial class Program
{
	/// <summary>
	/// 渐入佳境：己方阵营回合开始时获得等同于战斗轮次的瞄准（第 1 轮 1 层、第 3 轮 3 层）；持有者自己的额外回合
	/// 不推进轮次，再给同样层数；敌方回合、只有队友参与的额外回合、非愚者持有者都不给。
	/// </summary>
	[HextechTest]
	private static void FoolHittingStrideGainsAimEqualToRoundNumber()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 1);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		HittingStrideRune rune = CreateArrowRune<HittingStrideRune>(fool);
		int Aim(Player player) => player.Creature.GetPower<AimPower>()?.Amount ?? 0;
		void TurnStart(HittingStrideRune holder, CombatSide side, params Creature[] participants) =>
			holder.AfterSideTurnStart(side, participants, combat.Combat).GetAwaiter().GetResult();

		combat.Combat.RoundNumber = 1;
		TurnStart(rune, CombatSide.Player, fool.Creature, teammate.Creature);
		Equal(1, Aim(fool), "round 1 grants 1 Aim");

		combat.Combat.RoundNumber = 3;
		TurnStart(rune, CombatSide.Player, fool.Creature, teammate.Creature);
		Equal(1 + 3, Aim(fool), "round 3 grants 3 more");
		Equal(0, Aim(teammate), "the teammate gains nothing");

		TurnStart(rune, CombatSide.Enemy);
		TurnStart(rune, CombatSide.Player, teammate.Creature);
		Equal(4, Aim(fool), "enemy turns and a teammate's extra turn do not trigger");
		TurnStart(rune, CombatSide.Player, fool.Creature);
		Equal(4 + 3, Aim(fool), "the holder's own extra turn keeps the round number");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 0);
		TurnStart(CreateArrowRune<HittingStrideRune>(ironclad), CombatSide.Player, ironclad.Creature);
		Equal(0, Aim(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 引而不发：己方阵营回合结束前，每支可用（已收回）的箭给 {Block} 点无力格挡；扎在敌人身上的、被摧毁的不算；
	/// 没有可用箭、敌方回合、只有队友参与的回合、非愚者持有者都不给。
	/// </summary>
	[HextechTest]
	private static void FoolHeldShotGainsBlockPerAvailableArrowAtTurnEnd()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		using FoolMelodyTestScope scope = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 3);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		HeldShotRune rune = CreateArrowRune<HeldShotRune>(fool);
		decimal block = rune.DynamicVars.Block.BaseValue;
		Equal(6m, block, "6 Block per available arrow");
		void TurnEnd(HeldShotRune holder, CombatSide side, params Creature[] participants) =>
			holder.BeforeSideTurnEnd(ArrowTestContext(), side, participants).GetAwaiter().GetResult();

		combat.Impale(enemy, fool);
		TurnEnd(rune, CombatSide.Player, fool.Creature, teammate.Creature);
		SequenceEqual(new[] { (fool.Creature, block * 2, ValueProp.Unpowered) }, FoolMelodyTestScope.BlockGains, "one arrow is impaled: two of three count");

		fool.PlayerCombatState!.Arrows.First(static slot => slot.IsAvailable).Status = ArrowStatus.Destroyed;
		TurnEnd(rune, CombatSide.Player, fool.Creature);
		Equal((fool.Creature, block, ValueProp.Unpowered), FoolMelodyTestScope.BlockGains[^1], "a destroyed arrow does not count");

		TurnEnd(rune, CombatSide.Enemy);
		TurnEnd(rune, CombatSide.Player, teammate.Creature);
		Equal(2, FoolMelodyTestScope.BlockGains.Count, "enemy turns and a teammate's extra turn do not trigger");

		combat.Impale(enemy, fool);
		TurnEnd(rune, CombatSide.Player, fool.Creature);
		Equal(2, FoolMelodyTestScope.BlockGains.Count, "no available arrow, no Block");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		TurnEnd(CreateArrowRune<HeldShotRune>(ironclad), CombatSide.Player, ironclad.Creature);
		Equal(2, FoolMelodyTestScope.BlockGains.Count, "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 悲歌："生成悲伤"同苦中作乐，每张给 {AimPower} 点瞄准；持有者自己的攻击（可嵌套）结算期间生成的先记账，
	/// 最外层攻击结束、原版瞄准扣完这次攻击登记的层数之后才给，给出的瞄准落在新实例上、能加成下一支箭；
	/// 队友的攻击不推迟；战斗重置清掉进行中的攻击；队友 / 非悲伤 / 非愚者 / 死亡的持有者不给。
	/// </summary>
	[HextechTest]
	private static void FoolElegyGainsAimOnOwnSadnessAndDefersDuringOwnAttacks()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 1);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		ElegyRune rune = CreateArrowRune<ElegyRune>(fool);
		int aim = rune.DynamicVars["AimPower"].IntValue;
		Equal(3, aim, "3 Aim per Sadness");
		int Aim(Player player) => player.Creature.GetPower<AimPower>()?.Amount ?? 0;
		void Generate(CardModel card, Player? creator) => rune.AfterCardGeneratedForCombat(card, creator).GetAwaiter().GetResult();

		Sadness own = CreateFoolTestCard<Sadness>(fool);
		Generate(own, fool);
		Equal(aim, Aim(fool), "own Sadness grants Aim");
		Generate(own, teammate);
		Generate(own, null);
		Generate(CreateFoolTestCard<Sadness>(teammate), fool);
		Generate(CreateFoolTestCard<Joy>(fool), fool);
		Equal(aim, Aim(fool), "teammates' Sadness, Sadness given to teammates and other cards do not trigger");

		// 原版瞄准登记这次箭矢攻击（记下 3 层），持有者攻击中途生成悲伤：先记账。
		AimPower registered = fool.Creature.GetPower<AimPower>()!;
		AttackCommand outer = CreateArrowTestAttack(fool.Creature);
		AttackCommand inner = CreateArrowTestAttack(fool.Creature);
		registered.BeforeAttack(outer).GetAwaiter().GetResult();
		rune.BeforeAttack(outer).GetAwaiter().GetResult();
		rune.BeforeAttack(inner).GetAwaiter().GetResult();
		Generate(own, fool);
		Equal(aim, Aim(fool), "Aim is held back during the holder's attack");
		rune.AfterAttack(ArrowTestContext(), inner).GetAwaiter().GetResult();
		Equal(aim, Aim(fool), "still held until the outermost attack ends");
		registered.AfterAttack(ArrowTestContext(), outer).GetAwaiter().GetResult();
		Equal(0, Aim(fool), "vanilla consumes the Aim registered for the attack");
		rune.AfterAttack(ArrowTestContext(), outer).GetAwaiter().GetResult();
		Equal(aim, Aim(fool), "the held Aim arrives after the attack");
		AimPower fresh = fool.Creature.GetPower<AimPower>()!;
		Expect(!ReferenceEquals(fresh, registered), "the held Aim lands on a fresh instance");
		Equal((decimal)aim, fresh.ModifyDamageAdditive(enemy, 10m, ArrowAttackProps, fool.Creature, CreateFoolTestCard<QuickShot>(fool), null), "and boosts the next arrow");

		rune.BeforeAttack(CreateArrowTestAttack(teammate.Creature)).GetAwaiter().GetResult();
		Generate(own, fool);
		Equal(aim * 2, Aim(fool), "a teammate's attack does not hold the Aim back");

		rune.BeforeAttack(CreateArrowTestAttack(fool.Creature)).GetAwaiter().GetResult();
		rune.AfterCombatEnd(null!).GetAwaiter().GetResult();
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Generate(own, fool);
		Equal(aim * 3, Aim(fool), "combat reset forgets attacks in flight");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 0);
		ElegyRune foreign = CreateArrowRune<ElegyRune>(ironclad);
		foreign.AfterCardGeneratedForCombat(CreateFoolTestCard<Sadness>(ironclad), ironclad).GetAwaiter().GetResult();
		Equal(0, Aim(ironclad), "non-Fool holders do not trigger");

		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(fool.Creature, 0);
		Expect(!rune.IsOwnSadnessGeneration(own, fool), "dead holders do not trigger");
	}

	/// <summary>
	/// 升级：快速射击：获得时补 1 张快速射击（无须牌组已有）；持有者的快速射击每次打出（含重放）后抽 {Cards} 张；
	/// 队友的快速射击、其他牌、非愚者持有者都不抽。
	/// </summary>
	[HextechTest]
	private static void FoolQuickShotUpgradeDrawsAfterOwnQuickShot()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState _, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		QuickShotUpgradeRune rune = AttachFoolTestRune<QuickShotUpgradeRune>(fool);
		decimal cards = rune.DynamicVars.Cards.BaseValue;
		Equal(1m, cards, "draws one card");
		Expect(rune.GrantsCardOnPickup && rune.HasUponPickupEffect, "adds a Quick Shot on pickup");
		Expect(rune.MeetsCardAvailabilityRequirement([]), "does not require Quick Shot in the deck");
		void Played(CardPlay play) => rune.AfterCardPlayed(null!, play).GetAwaiter().GetResult();

		QuickShot quickShot = CreateFoolTestCard<QuickShot>(fool);
		Played(CreateCardPlay(quickShot, isAutoPlay: false));
		SequenceEqual(new[] { (fool, cards) }, FoolMelodyTestScope.Draws, "own Quick Shot draws");
		Played(CreateCardPlay(quickShot, playIndex: 1, playCount: 2, isAutoPlay: false));
		Equal(2, FoolMelodyTestScope.Draws.Count, "a replay draws again");

		Played(CreateCardPlay(CreateFoolTestCard<QuickShot>(teammate), isAutoPlay: false));
		Played(CreateCardPlay(CreateFoolTestCard<HeavyShot>(fool), isAutoPlay: false));
		Equal(2, FoolMelodyTestScope.Draws.Count, "a teammate's Quick Shot and other cards do not draw");

		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		Played(CreateCardPlay(quickShot, isAutoPlay: false));
		Equal(2, FoolMelodyTestScope.Draws.Count, "non-Fool holders do not draw");
	}

	/// <summary>
	/// 升级：歌之道：用原版"本回合第一张带该旋律的牌"判定，哇、叽各补一次歌之道（数值读能力层数，无力格挡）；
	/// 啾由原版自己结算不重复；一张牌带来几种就结算几次；同种再打、队友的牌、没有歌之道能力、非愚者都不结算；下一回合重置。
	/// </summary>
	[HextechTest]
	private static void FoolSongPathUpgradeTriggersOnEveryNewMelody()
	{
		using FoolMelodyTestScope scope = new();
		using IDisposable models = InjectMissingModels(typeof(SongPathPower));
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		SongPathUpgradeRune rune = AttachFoolTestRune<SongPathUpgradeRune>(fool);
		Expect(rune.GrantsCardOnPickup, "adds a Song Path on pickup");
		void Play(CardModel card) => rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, card)).GetAwaiter().GetResult();

		Play(CreateFoolTestCard<WahTouch>(fool));
		Equal(0, FoolMelodyTestScope.BlockGains.Count, "without the Song Path power nothing happens");

		AddFoolTestPower<SongPathPower>(fool, 5);
		Play(CreateFoolTestCard<KiVerse>(fool));
		SequenceEqual(new[] { (fool.Creature, 5m, ValueProp.Unpowered) }, FoolMelodyTestScope.BlockGains, "the first Ki of the turn triggers Song Path");
		Play(CreateFoolTestCard<KiVerse>(fool));
		Play(CreateFoolTestCard<WahTouch>(fool));
		Play(CreateFoolTestCard<ChoChorus>(fool));
		Play(CreateFoolTestCard<WahChoKi>(teammate));
		Equal(1, FoolMelodyTestScope.BlockGains.Count, "repeat melodies, Cho (vanilla's own) and a teammate's card do not trigger");

		AdvanceFoolTestRound(combat, fool, teammate);
		Play(CreateFoolTestCard<WahChoKi>(fool));
		Equal(3, FoolMelodyTestScope.BlockGains.Count, "next turn: Wah Cho Ki brings Wah and Ki, two extra triggers");

		AdvanceFoolTestRound(combat, fool, teammate);
		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		Play(CreateFoolTestCard<KiVerse>(fool));
		Equal(3, FoolMelodyTestScope.BlockGains.Count, "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 升级：暴力和弦：同一判定下啾、叽各补一次暴力和弦（对此刻可被攻击的敌人造成能力层数的无力伤害，来源是持有者）；
	/// 哇由原版自己结算不重复；没有暴力和弦能力时什么都不做。
	/// </summary>
	[HextechTest]
	private static void FoolViolentChordUpgradeTriggersOnEveryNewMelody()
	{
		using FoolMelodyTestScope scope = new();
		using IDisposable models = InjectMissingModels(typeof(ViolentChordPower));
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		ViolentChordUpgradeRune rune = AttachFoolTestRune<ViolentChordUpgradeRune>(fool);
		Expect(rune.GrantsCardOnPickup, "adds a Violent Chord on pickup");
		void Play(CardModel card) => rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, card)).GetAwaiter().GetResult();

		Play(CreateFoolTestCard<ChoChorus>(fool));
		Equal(0, FoolMelodyTestScope.Damages.Count, "without the Violent Chord power nothing happens");

		AddFoolTestPower<ViolentChordPower>(fool, 9);
		Play(CreateFoolTestCard<WahTouch>(fool));
		Equal(0, FoolMelodyTestScope.Damages.Count, "Wah is vanilla's own trigger");
		Play(CreateFoolTestCard<KiVerse>(fool));
		Equal(1, FoolMelodyTestScope.Damages.Count, "the first Ki of the turn triggers Violent Chord");
		(Creature[] _, decimal amount, ValueProp props, Creature? dealer) = FoolMelodyTestScope.Damages[0];
		Expect(amount == 9m && props == ValueProp.Unpowered && dealer == fool.Creature, "power amount, unpowered, dealt by the holder");
		Play(CreateFoolTestCard<ChoChorus>(fool));
		Equal(1, FoolMelodyTestScope.Damages.Count, "Cho was already played this turn before the power existed");

		AdvanceFoolTestRound(combat, fool, teammate);
		Play(CreateFoolTestCard<WahChoKi>(fool));
		Equal(3, FoolMelodyTestScope.Damages.Count, "next turn: Wah Cho Ki brings Cho and Ki, two extra triggers");
		Play(CreateFoolTestCard<ChoChorus>(fool));
		Play(CreateFoolTestCard<KiVerse>(teammate));
		Equal(3, FoolMelodyTestScope.Damages.Count, "repeat melodies and a teammate's card do not trigger");
	}

	/// <summary>
	/// 引吭高歌给予旋律时按能力列表结算"获得旋律时"：持有升级：歌之道 / 升级：暴力和弦时，歌之道 / 暴力和弦对任意旋律都结算。
	/// </summary>
	[HextechTest]
	private static void FoolSingOutResolvesUpgradedMelodyPowersForAnyMelody()
	{
		using FoolMelodyTestScope scope = new();
		using IDisposable models = InjectMissingModels(typeof(SongPathPower), typeof(ViolentChordPower));
		(CombatState _, Player fool, Player _) = CreateFoolMelodyCombatFixture();
		SingOutRune singOut = AttachFoolTestRune<SingOutRune>(fool);
		PowerModel songPath = AddFoolTestPower<SongPathPower>(fool, 5);
		PowerModel violentChord = AddFoolTestPower<ViolentChordPower>(fool, 9);

		Equal(0, singOut.GetMelodyGainedPowers(CardTag.Ki).Count, "without upgrades Ki triggers neither");
		AttachFoolTestRune<SongPathUpgradeRune>(fool);
		SequenceEqual(new[] { songPath }, singOut.GetMelodyGainedPowers(CardTag.Ki), "upgraded Song Path resolves for Ki");
		SequenceEqual(new[] { songPath, violentChord }, singOut.GetMelodyGainedPowers(CardTag.Wah), "Wah: upgraded Song Path and vanilla Violent Chord, in power-list order");
		AttachFoolTestRune<ViolentChordUpgradeRune>(fool);
		SequenceEqual(new[] { songPath, violentChord }, singOut.GetMelodyGainedPowers(CardTag.Ki), "both upgraded: Ki resolves both");
		SequenceEqual(new[] { songPath, violentChord }, singOut.GetMelodyGainedPowers(CardTag.Cho), "both upgraded: Cho resolves both");
	}

	/// <summary>
	/// 弦歌不辍：口径同弦外之音，本回合每种旋律首次获得时各给 {StretchPower} 点拉伸（施加者是持有者、没有来源牌）；
	/// 一张牌带来多种新旋律一次给多份；同种再打、非旋律牌、队友出牌、敌方回合不给；下一回合重置；
	/// 开嗓第一回合开始检查时一次给三份，引吭高歌给予的旋律在回合开始检查时算一种。
	/// </summary>
	[HextechTest]
	private static void FoolUnbrokenSongGainsStretchForEachNewMelody()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		UnbrokenSongRune rune = AttachFoolTestRune<UnbrokenSongRune>(fool);
		decimal stretch = rune.DynamicVars["StretchPower"].BaseValue;
		Equal(2m, stretch, "2 Stretch per melody");
		decimal[] Gains() => FoolMelodyTestScope.StretchApplications.Select(static entry => entry.Amount).ToArray();
		void Play(CardModel card) => rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, card)).GetAwaiter().GetResult();

		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Play(CreateFoolTestCard<StrikeIronclad>(fool));
		Play(CreateFoolTestCard<WahTouch>(teammate));
		Equal(0, Gains().Length, "no melody of the holder's yet");

		Play(CreateFoolTestCard<WahTouch>(fool));
		Play(CreateFoolTestCard<WahTouch>(fool));
		Play(CreateFoolTestCard<ChoChorus>(fool));
		SequenceEqual(new[] { stretch, stretch }, Gains(), "first Wah and first Cho of the turn");
		Expect(FoolMelodyTestScope.StretchApplications.All(entry => entry.Target == fool.Creature && entry.Applier == fool.Creature), "Stretch goes to the holder, applied by the holder");

		combat.CurrentSide = CombatSide.Enemy;
		Play(CreateFoolTestCard<KiVerse>(fool));
		Equal(2, Gains().Length, "melodies on the enemy turn do not count");

		AdvanceFoolTestRound(combat, fool, teammate);
		Play(CreateFoolTestCard<WahChoKi>(fool));
		Play(CreateFoolTestCard<KiVerse>(fool));
		SequenceEqual(new[] { stretch, stretch, stretch * 3 }, Gains(), "next turn resets; one card bringing three new melodies gives three at once");

		AdvanceFoolTestRound(combat, fool, teammate);
		SingOutRune singOut = AttachFoolTestRune<SingOutRune>(fool);
		singOut.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal(stretch, Gains()[^1], "a melody granted by Sing Out counts as one at turn start");

		(CombatState _, Player opener, Player _) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(opener);
		UnbrokenSongRune withOpening = AttachFoolTestRune<UnbrokenSongRune>(opener);
		withOpening.AfterPlayerTurnStart(null!, opener).GetAwaiter().GetResult();
		Equal(stretch * 3, Gains()[^1], "Opening Note owns all three melodies at the first turn start");
	}

	/// <summary>悲喜交加：持有者生成悲伤时，喜悦经原版生成命令加入手牌（手牌已满时原版改放弃牌堆），生成者是持有者。</summary>
	[HextechTest]
	private static void FoolMixedFeelingsAddsJoyToHand()
	{
		Player owner = CreateFoolPileTestPlayers(1)[0];
		MixedFeelingsRune rune = CreateMutableTestModel<MixedFeelingsRune>();
		rune.Owner = owner;
		Sadness sadness = AddFoolTestCard<Sadness>(owner, PileType.Hand);
		using IDisposable models = InjectMissingModels(typeof(Joy));
		using FoolDeckEditTestScope deckEdit = new();
		Harmony harmony = new("Natsuki.HextechRunes.Tests.MixedFeelingsHand");
		MixedFeelingsGeneratedCards.Clear();
		try
		{
			harmony.Patch(
				AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat)),
				prefix: new HarmonyMethod(typeof(Program), nameof(CaptureMixedFeelingsGeneration)));
			rune.AfterCardGeneratedForCombat(sadness, owner).GetAwaiter().GetResult();
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}

		Equal(1, MixedFeelingsGeneratedCards.Count, "one generation call");
		(CardModel[] cards, PileType pile, Player? creator) = MixedFeelingsGeneratedCards[0];
		Equal(PileType.Hand, pile, "Joy goes to the hand");
		Expect(cards.Length == rune.DynamicVars.Cards.IntValue && cards.All(card => card is Joy && card.Owner == owner), "Cards Joy owned by the holder");
		Equal(owner, creator, "generated by the holder");
		MixedFeelingsGeneratedCards.Clear();
	}

	private static readonly List<(CardModel[] Cards, PileType Pile, Player? Creator)> MixedFeelingsGeneratedCards = [];

	private static bool CaptureMixedFeelingsGeneration(IEnumerable<CardModel> cards, PileType newPileType, Player? creator, ref Task<IReadOnlyList<CardPileAddResult>> __result)
	{
		MixedFeelingsGeneratedCards.Add((cards.ToArray(), newPileType, creator));
		__result = Task.FromResult<IReadOnlyList<CardPileAddResult>>([]);
		return false;
	}
}
#endif
