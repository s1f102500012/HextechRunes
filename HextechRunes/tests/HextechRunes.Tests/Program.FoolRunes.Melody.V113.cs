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

// 旋律组：开嗓（直接拥有旋律）、弦外之音、三重唱、呱？（换成哇 啾 叽）。
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
#if !STS2_114_OR_NEWER
		// 0.114.0 删掉了 MelodyCmd.MelodyCardsPlayedThisTurn（原版不再按旋律牌张数计数）。
		Equal(0, MelodyCmd.MelodyCardsPlayedThisTurn(fool), "granted melodies leave no card plays behind");
#endif
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
	/// 弦外之音：本回合每种旋律首次获得时各给 {Energy} 点能量（每回合最多三次）；一张牌同时带来多种新旋律时一次给多份；
	/// 同种旋律再打出、非旋律牌、队友出牌、敌方回合都不给；下一回合重置。
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
	/// 呱？：获得时按牌组顺序把基础打击与基础防御两两配对，每对换成 {Cards} 张哇 啾 叽；落单的基础牌和非基础牌不动；
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
