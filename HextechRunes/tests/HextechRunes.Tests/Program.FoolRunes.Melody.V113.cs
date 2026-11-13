#if STS2_113_OR_NEWER
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes.Tests;

// 旋律组：开嗓、引吭高歌（直接拥有旋律）、弦外之音、三重唱。
// 原版旋律判定走真实的 MelodyCmd + 战斗历史 + 本模组的 PlayedThisTurn 后缀；获得能量 / 抽牌只记录调用（见 FoolMelodyTestScope）。
internal static partial class Program
{
	/// <summary>
	/// 开嗓：持有者第一回合的己方回合里三种旋律都算拥有（含 MelodiesThisTurn），敌方回合、第二回合、队友、非愚者持有者都不算；
	/// 直接拥有不产生出牌记录（按出牌计数的"获得旋律时"不会触发）。
	/// </summary>
	[HextechTest]
	private static void FoolOpeningNoteGrantsAllMelodiesOnFirstTurn()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(fool);

		Expect(HextechGrantedMelodies.MelodyTags.All(tag => MelodyCmd.PlayedThisTurn(fool, tag)), "first turn grants every melody");
		Equal(3, MelodyCmd.MelodiesThisTurn(fool), "melody count includes granted melodies");
		Equal(0, MelodyCmd.MelodyCardsPlayedThisTurn(fool), "granted melodies leave no card plays behind");
		Equal(0, MelodyCmd.MelodiesThisTurn(teammate), "teammate without the rune has no melodies");

		combat.CurrentSide = CombatSide.Enemy;
		Equal(0, MelodyCmd.MelodiesThisTurn(fool), "enemy turn has no melodies");

		AdvanceFoolTestRound(combat, fool, teammate);
		Equal(0, MelodyCmd.MelodiesThisTurn(fool), "second turn no longer grants melodies");

		(CombatState _, Player ironclad, Player _) = CreateFoolMelodyCombatFixture();
		SetAutoProperty(ironclad, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		AttachFoolTestRune<OpeningNoteRune>(ironclad);
		Equal(0, MelodyCmd.MelodiesThisTurn(ironclad), "non-Fool holders are not granted melodies");
	}

	/// <summary>
	/// 引吭高歌：回合开始（抽牌前）给予一种本回合尚未拥有的旋律；结果由稳定随机决定、同回合重复计算结果相同；
	/// 下一回合旧的给予失效后重选；已拥有两种时只能给第三种；与开嗓同持的第一回合不再给予；队友的回合钩子不触发；战斗结束清空。
	/// </summary>
	[HextechTest]
	private static void FoolSingOutGrantsOneMissingMelodyPerTurn()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		SingOutRune rune = AttachFoolTestRune<SingOutRune>(fool);

		Equal(0, MelodyCmd.MelodiesThisTurn(fool), "nothing granted before the turn starts");
		rune.BeforeHandDraw(teammate, null!, combat).GetAwaiter().GetResult();
		Equal(0, MelodyCmd.MelodiesThisTurn(fool), "a teammate's turn start does not grant");

		rune.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		Equal(1, MelodyCmd.MelodiesThisTurn(fool), "turn start grants exactly one melody");
		CardTag first = HextechGrantedMelodies.MelodyTags.Single(tag => MelodyCmd.PlayedThisTurn(fool, tag));
		Equal(0, MelodyCmd.MelodiesThisTurn(teammate), "teammate is unaffected");
		rune.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		Expect(MelodyCmd.PlayedThisTurn(fool, first) && MelodyCmd.MelodiesThisTurn(fool) == 1, "re-evaluating the same turn picks the same melody");

		AdvanceFoolTestRound(combat, fool, teammate);
		Equal(0, MelodyCmd.MelodiesThisTurn(fool), "last turn's grant expires");
		RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool));
		RecordFoolTestCardPlay(combat, CreateFoolTestCard<ChoChorus>(fool));
		rune.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		Expect(rune.GrantsMelodyThisTurn(CardTag.Ki), "with Wah and Cho already owned only Ki is left to grant");
		Equal(3, MelodyCmd.MelodiesThisTurn(fool), "granted Ki completes the set");

		combat.CurrentSide = CombatSide.Enemy;
		Expect(!rune.GrantsMelodyThisTurn(CardTag.Ki), "the grant does not cover the enemy turn");
		combat.CurrentSide = CombatSide.Player;
		rune.AfterCombatEnd(null!).GetAwaiter().GetResult();
		Expect(!rune.GrantsMelodyThisTurn(CardTag.Ki), "combat end clears the grant");

		(CombatState combat2, Player both, Player _) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(both);
		SingOutRune withOpening = AttachFoolTestRune<SingOutRune>(both);
		withOpening.BeforeHandDraw(both, null!, combat2).GetAwaiter().GetResult();
		Expect(HextechGrantedMelodies.MelodyTags.All(tag => !withOpening.GrantsMelodyThisTurn(tag)), "with Opening Note the first turn already owns everything, so nothing is granted");
		AdvanceFoolTestRound(combat2, both);
		withOpening.BeforeHandDraw(both, null!, combat2).GetAwaiter().GetResult();
		Equal(1, MelodyCmd.MelodiesThisTurn(both), "from the second turn Sing Out grants again");
	}

	/// <summary>
	/// 引吭高歌给予旋律算"获得旋律"：曲中意、失落鲁特琴对任意旋律结算，暴力和弦只认哇、歌之道只认啾，顺序按能力列表；
	/// 结算在抽完起手牌后的回合开始钩子里，每次给予只结算一次，队友的钩子不触发。
	/// </summary>
	[HextechTest]
	private static void FoolSingOutResolvesMelodyGainedPowersOncePerGrant()
	{
		using FoolMelodyTestScope scope = new();
		using IDisposable models = InjectMissingModels(typeof(MotifPower), typeof(LostLutePower), typeof(ViolentChordPower), typeof(SongPathPower));
		(CombatState _, Player fool, Player _) = CreateFoolMelodyCombatFixture();
		SingOutRune rune = AttachFoolTestRune<SingOutRune>(fool);
		PowerModel songPath = AddFoolTestPower<SongPathPower>(fool, 5);
		PowerModel motif = AddFoolTestPower<MotifPower>(fool, 2);
		PowerModel violentChord = AddFoolTestPower<ViolentChordPower>(fool, 9);

		SequenceEqual(new[] { motif, violentChord }, rune.GetMelodyGainedPowers(CardTag.Wah), "Wah: Motif and Violent Chord, in power-list order");
		SequenceEqual(new[] { songPath, motif }, rune.GetMelodyGainedPowers(CardTag.Cho), "Cho: Song Path and Motif");
		SequenceEqual(new[] { motif }, rune.GetMelodyGainedPowers(CardTag.Ki), "Ki: only the any-melody powers");

		(CombatState combat2, Player drawer, Player other) = CreateFoolMelodyCombatFixture();
		SingOutRune drawRune = AttachFoolTestRune<SingOutRune>(drawer);
		AddFoolTestPower<MotifPower>(drawer, 2);
		drawRune.AfterPlayerTurnStart(null!, drawer).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.Draws.Count, "nothing resolves before a melody is granted");

		drawRune.BeforeHandDraw(drawer, null!, combat2).GetAwaiter().GetResult();
		drawRune.AfterPlayerTurnStart(null!, other).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.Draws.Count, "a teammate's turn start does not resolve the holder's grant");
		drawRune.AfterPlayerTurnStart(null!, drawer).GetAwaiter().GetResult();
		SequenceEqual(new[] { (drawer, 2m) }, FoolMelodyTestScope.Draws, "Motif draws its amount when the melody is granted");
		drawRune.AfterPlayerTurnStart(null!, drawer).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.Draws.Count, "each grant resolves only once");

		AdvanceFoolTestRound(combat2, drawer, other);
		drawRune.AfterPlayerTurnStart(null!, drawer).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.Draws.Count, "last turn's grant does not resolve again");
		drawRune.BeforeHandDraw(drawer, null!, combat2).GetAwaiter().GetResult();
		drawRune.AfterPlayerTurnStart(null!, drawer).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.Draws.Count, "the next turn's grant resolves again");
	}

	/// <summary>
	/// 弦外之音：本回合种类数首次达到 1 时获得 {Energy} 点能量，每回合一次、下一回合重置；
	/// 非旋律牌、队友出牌、敌方回合不触发；引吭高歌给予的旋律在回合开始检查时即触发。
	/// </summary>
	[HextechTest]
	private static void FoolOvertoneGainsEnergyOnFirstMelodyEachTurn()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		OvertoneRune rune = AttachFoolTestRune<OvertoneRune>(fool);
		decimal energy = rune.DynamicVars.Energy.BaseValue;

		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<StrikeIronclad>(fool))).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.EnergyGains.Count, "no melody yet, no energy");

		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(teammate))).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.EnergyGains.Count, "a teammate's melody is not the holder's");

		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, energy) }, FoolMelodyTestScope.EnergyGains, "first melody of the turn grants energy");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<ChoChorus>(fool))).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.EnergyGains.Count, "only once per turn");

		combat.CurrentSide = CombatSide.Enemy;
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(fool))).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.EnergyGains.Count, "melodies on the enemy turn do not count");

		AdvanceFoolTestRound(combat, fool, teammate);
		SingOutRune singOut = AttachFoolTestRune<SingOutRune>(fool);
		singOut.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.EnergyGains.Count, "next turn resets; a granted melody triggers at turn start");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.EnergyGains.Count, "a played melody after the granted one does not trigger again");
	}

	/// <summary>
	/// 三重唱：本回合首次拥有全部三种旋律时获得 {Energy} 点能量并抽 {Cards} 张牌，每回合一次；
	/// 与开嗓同持时第一回合开始即触发（弦外之音同时触发一次），之后出牌不重复；引吭高歌给予的一种与打出的两种合计也算。
	/// </summary>
	[HextechTest]
	private static void FoolTrioRewardsFirstTimeAllMelodies()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		TrioRune rune = AttachFoolTestRune<TrioRune>(fool);
		decimal energy = rune.DynamicVars.Energy.BaseValue;
		decimal cards = rune.DynamicVars.Cards.BaseValue;

		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<ChoChorus>(fool))).GetAwaiter().GetResult();
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(teammate))).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.EnergyGains.Count, "two melodies and a teammate's Ki are not all melodies");

		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(fool))).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, energy) }, FoolMelodyTestScope.EnergyGains, "all three melodies grant energy");
		SequenceEqual(new[] { (fool, cards) }, FoolMelodyTestScope.Draws, "and draw cards");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.Draws.Count, "only once per turn");

		AdvanceFoolTestRound(combat, fool, teammate);
		SingOutRune singOut = AttachFoolTestRune<SingOutRune>(fool);
		singOut.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.Draws.Count, "one granted melody is not all melodies");
		foreach (CardModel card in new CardModel[] { CreateFoolTestCard<WahTouch>(fool), CreateFoolTestCard<ChoChorus>(fool), CreateFoolTestCard<KiVerse>(fool) })
		{
			if (!MelodyCmd.PlayedThisTurn(fool, card.Tags.Single()))
			{
				rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, card)).GetAwaiter().GetResult();
			}
		}
		Equal(2, FoolMelodyTestScope.Draws.Count, "the granted melody plus two played ones completes the set on a new turn");

		FoolMelodyTestScope.EnergyGains.Clear();
		FoolMelodyTestScope.Draws.Clear();
		(CombatState combat2, Player both, Player _) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(both);
		OvertoneRune overtone = AttachFoolTestRune<OvertoneRune>(both);
		TrioRune trio = AttachFoolTestRune<TrioRune>(both);
		overtone.AfterPlayerTurnStart(null!, both).GetAwaiter().GetResult();
		trio.AfterPlayerTurnStart(null!, both).GetAwaiter().GetResult();
		Equal(overtone.DynamicVars.Energy.BaseValue + trio.DynamicVars.Energy.BaseValue, FoolMelodyTestScope.EnergyGains.Sum(static gain => gain.Amount), "Opening Note triggers Overtone and Trio once each at turn start");
		Equal(1, FoolMelodyTestScope.Draws.Count, "Trio draws once");
		CardPlay play = RecordFoolTestCardPlay(combat2, CreateFoolTestCard<WahTouch>(both));
		overtone.AfterCardPlayed(null!, play).GetAwaiter().GetResult();
		trio.AfterCardPlayed(null!, play).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.EnergyGains.Count, "later plays on the same turn do not repeat either reward");
	}
}
#endif
