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

// 旋律组：开嗓、引吭高歌（直接拥有旋律）、弦外之音、三重唱、呱！（换成哇 啾 叽）。
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
	/// 弦外之音：本回合每种旋律首次获得时各给 {Energy} 点能量（每回合最多三次）；一张牌同时带来多种新旋律时一次给多份；
	/// 同种旋律再打出、非旋律牌、队友出牌、敌方回合都不给；下一回合重置；引吭高歌给予的旋律在回合开始检查时即算一种。
	/// </summary>
	[HextechTest]
	private static void FoolOvertoneGainsEnergyForEachNewMelodyEachTurn()
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
		SequenceEqual(new[] { (fool, energy) }, FoolMelodyTestScope.EnergyGains, "first Wah of the turn grants energy");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.EnergyGains.Count, "a second Wah is not a new melody");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<ChoChorus>(fool))).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, energy), (fool, energy) }, FoolMelodyTestScope.EnergyGains, "first Cho of the turn grants energy again");

		combat.CurrentSide = CombatSide.Enemy;
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(fool))).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.EnergyGains.Count, "melodies on the enemy turn do not count");

		AdvanceFoolTestRound(combat, fool, teammate);
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahChoKi>(fool))).GetAwaiter().GetResult();
		Equal((fool, energy * 3), FoolMelodyTestScope.EnergyGains[^1], "next turn resets; one card bringing all three new melodies grants three at once");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(fool))).GetAwaiter().GetResult();
		Equal(3, FoolMelodyTestScope.EnergyGains.Count, "at most three per turn");

		AdvanceFoolTestRound(combat, fool, teammate);
		SingOutRune singOut = AttachFoolTestRune<SingOutRune>(fool);
		singOut.BeforeHandDraw(fool, null!, combat).GetAwaiter().GetResult();
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal((fool, energy), FoolMelodyTestScope.EnergyGains[^1], "a granted melody counts as one at turn start");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahChoKi>(fool))).GetAwaiter().GetResult();
		Equal((fool, energy * 2), FoolMelodyTestScope.EnergyGains[^1], "Wah Cho Ki after a granted melody grants only the two missing ones");
		Equal(5, FoolMelodyTestScope.EnergyGains.Count, "granted plus played melodies are rewarded once each");
	}

	/// <summary>
	/// 弦外之音与开嗓同持：第一回合开始检查时三种旋律都已拥有，一次获得 3 × {Energy} 点能量，之后本回合出牌不再给；
	/// 第二回合开嗓失效，按打出的旋律重新逐种给。
	/// </summary>
	[HextechTest]
	private static void FoolOvertoneWithOpeningNoteGainsThreeOnFirstTurn()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(fool);
		OvertoneRune rune = AttachFoolTestRune<OvertoneRune>(fool);
		decimal energy = rune.DynamicVars.Energy.BaseValue;

		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, energy * 3) }, FoolMelodyTestScope.EnergyGains, "Opening Note owns all three melodies at turn start");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<WahTouch>(fool))).GetAwaiter().GetResult();
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.EnergyGains.Count, "nothing more on the first turn");

		AdvanceFoolTestRound(combat, fool, teammate);
		rune.AfterPlayerTurnStart(null!, fool).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.EnergyGains.Count, "Opening Note no longer grants on the second turn");
		rune.AfterCardPlayed(null!, RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(fool))).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, energy * 3), (fool, energy) }, FoolMelodyTestScope.EnergyGains, "the second turn rewards played melodies again");
	}

	/// <summary>
	/// 三重唱：打出时（原版求打出次数那一刻，这张牌还不在出牌历史里）持有者本回合已拥有全部三种旋律，才加 {Replays} 次；
	/// 凑齐第三种旋律的那张牌自己不重放，之后本回合每张牌都重放；加法叠加、重复求值不变（原版每次出牌只求一次，重放不再求值，不会连锁）；
	/// 队友的牌、敌方回合、下一回合都不重放；开嗓给予的旋律也算。
	/// </summary>
	[HextechTest]
	private static void FoolTrioReplaysCardsWhileOwningAllMelodies()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		TrioRune rune = AttachFoolTestRune<TrioRune>(fool);
		int replays = rune.DynamicVars["Replays"].IntValue;

		CardModel wah = CreateFoolTestCard<WahTouch>(fool);
		Equal(1, rune.ModifyCardPlayCount(wah, null, 1), "no melodies yet");
		RecordFoolTestCardPlay(combat, wah);
		RecordFoolTestCardPlay(combat, CreateFoolTestCard<ChoChorus>(fool));
		RecordFoolTestCardPlay(combat, CreateFoolTestCard<KiVerse>(teammate));
		CardModel ki = CreateFoolTestCard<KiVerse>(fool);
		Equal(1, rune.ModifyCardPlayCount(ki, null, 1), "the card completing the set is not replayed itself");
		RecordFoolTestCardPlay(combat, ki);

		CardModel strike = CreateFoolTestCard<StrikeIronclad>(fool);
		Equal(1 + replays, rune.ModifyCardPlayCount(strike, null, 1), "after owning all melodies every card is replayed");
		Equal(1 + replays, rune.ModifyCardPlayCount(strike, null, 1), "repeated evaluation is stable");
		Equal(2 + replays, rune.ModifyCardPlayCount(strike, null, 2), "replays add on top of other modifiers, not multiply");
		Equal(1 + replays, rune.ModifyCardPlayCount(CreateFoolTestCard<WahTouch>(fool), null, 1), "melody cards are replayed too");
		Equal(1, rune.ModifyCardPlayCount(CreateFoolTestCard<StrikeIronclad>(teammate), null, 1), "a teammate's card is not replayed");

		combat.CurrentSide = CombatSide.Enemy;
		Equal(1, rune.ModifyCardPlayCount(strike, null, 1), "no melodies on the enemy turn");
		AdvanceFoolTestRound(combat, fool, teammate);
		Equal(1, rune.ModifyCardPlayCount(strike, null, 1), "the next turn starts without melodies");

		(CombatState _, Player both, Player _) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(both);
		TrioRune withOpening = AttachFoolTestRune<TrioRune>(both);
		Equal(1 + replays, withOpening.ModifyCardPlayCount(CreateFoolTestCard<StrikeIronclad>(both), null, 1), "with Opening Note the first card of the first turn is replayed");
	}

	/// <summary>
	/// 三重唱与塔罗牌桌布同持：原版 ModifyCardPlayCount 依次累加，两者各加自己的次数（塔罗牌共重放 2 次，其他牌 1 次）。
	/// </summary>
	[HextechTest]
	private static void FoolTrioStacksWithTarotCloth()
	{
		using FoolMelodyTestScope scope = new();
		using IDisposable models = InjectMissingModels(VanillaTarotCardTypes);
		(CombatState _, Player fool, Player _) = CreateFoolMelodyCombatFixture();
		AttachFoolTestRune<OpeningNoteRune>(fool);
		TrioRune trio = AttachFoolTestRune<TrioRune>(fool);
		TarotClothRune cloth = AttachFoolTestRune<TarotClothRune>(fool);
		int trioReplays = trio.DynamicVars["Replays"].IntValue;
		int clothReplays = cloth.DynamicVars["Replays"].IntValue;

		CardModel tarot = CreateOwnedTarotCard(ModelDb.Card<TheSun>(), fool);
		Equal(1 + trioReplays + clothReplays, cloth.ModifyCardPlayCount(tarot, null, trio.ModifyCardPlayCount(tarot, null, 1)), "tarot card: both replays add up");
		Equal(1 + trioReplays + clothReplays, trio.ModifyCardPlayCount(tarot, null, cloth.ModifyCardPlayCount(tarot, null, 1)), "order does not matter");
		CardModel strike = CreateFoolTestCard<StrikeIronclad>(fool);
		Equal(1 + trioReplays, cloth.ModifyCardPlayCount(strike, null, trio.ModifyCardPlayCount(strike, null, 1)), "non-tarot card: only Trio replays");
	}

	/// <summary>
	/// 呱！：获得时按牌组顺序把基础打击与基础防御两两配对，每对换成 {Cards} 张哇 啾 叽；落单的基础牌和非基础牌不动；
	/// 没有成对时什么都不做。原版移除 / 加牌命令换成只记录调用的前缀（测试进程没有真实存档与牌堆界面）。
	/// </summary>
	[HextechTest]
	private static void FoolRibbitReplacesBasicStrikeDefendPairs()
	{
		using IDisposable models = InjectMissingModels(typeof(WahChoKi));
		using FoolDeckEditTestScope scope = new();
		Player fool = CreateFoolPileTestPlayers(1)[0];
		RibbitRune rune = CreateMutableTestModel<RibbitRune>();
		rune.Owner = fool;
		CardModel[] strikes = [AddFoolTestCard<StrikeFool>(fool, PileType.Deck), AddFoolTestCard<StrikeFool>(fool, PileType.Deck), AddFoolTestCard<StrikeFool>(fool, PileType.Deck)];
		CardModel[] defends = [AddFoolTestCard<DefendFool>(fool, PileType.Deck), AddFoolTestCard<DefendFool>(fool, PileType.Deck)];
		CardModel other = AddFoolTestCard<WahTouch>(fool, PileType.Deck);

		rune.AfterObtained().GetAwaiter().GetResult();
		SequenceEqual(new[] { strikes[0], defends[0], strikes[1], defends[1] }, FoolDeckEditTestScope.Removed, "two pairs are removed in deck order");
		Equal(2 * rune.DynamicVars.Cards.IntValue, FoolDeckEditTestScope.Added.Count, "each pair becomes Cards Wah Cho Ki");
		Expect(FoolDeckEditTestScope.Added.All(card => card is WahChoKi && card.Owner == fool), "added cards are the holder's Wah Cho Ki");
		Expect(fool.Deck.Cards.Contains(strikes[2]) && fool.Deck.Cards.Contains(other), "the unpaired Strike and non-basic cards stay");
		Equal(4, fool.Deck.Cards.Count, "deck: leftover Strike, Wah Touch and two Wah Cho Ki");

		FoolDeckEditTestScope.Removed.Clear();
		FoolDeckEditTestScope.Added.Clear();
		RibbitRune again = CreateMutableTestModel<RibbitRune>();
		again.Owner = fool;
		again.AfterObtained().GetAwaiter().GetResult();
		Equal(0, FoolDeckEditTestScope.Removed.Count + FoolDeckEditTestScope.Added.Count, "no pairs left, nothing changes");
	}
}
#endif
