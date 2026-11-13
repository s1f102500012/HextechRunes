#if STS2_113_OR_NEWER
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 塔罗组（水晶球、塔罗牌桌布）与共用的 HextechTarotCardGeneration。夹具不开始战斗：原版生成命令会提前返回，
// "牌真的进了抽牌堆、重放真的多打一次"要实机确认；这里测判定、挑选与不触发的路径。
internal static partial class Program
{
	private static readonly Type[] VanillaTarotCardTypes =
	[
		typeof(Judgement), typeof(TheFool), typeof(TheHighPriestess), typeof(TheEmperor), typeof(TheEmpress),
		typeof(TheHangedMan), typeof(TheHierophant), typeof(TheChariot), typeof(Justice), typeof(TheMagician),
		typeof(TheMoon), typeof(TheTower), typeof(Strength), typeof(Death), typeof(Temperance), typeof(TheDevil),
		typeof(TheHermit), typeof(TheLovers), typeof(TheStar), typeof(TheSun), typeof(TheWorld), typeof(WheelOfFortune)
	];

	/// <summary>塔罗牌按原版 TarotCards.All 的 22 种类型判定；悲伤、喜悦、神话形态与普通牌都不算。</summary>
	[HextechTest]
	private static void FoolTarotCardsAreRecognisedByVanillaType()
	{
		using IDisposable models = InjectMissingModels(VanillaTarotCardTypes);
		Equal(22, VanillaTarotCardTypes.Length, "test covers all vanilla tarot types");
		SequenceEqual(VanillaTarotCardTypes.OrderBy(static t => t.Name), TarotCards.All.Select(static c => c.GetType()).OrderBy(static t => t.Name), "test list matches TarotCards.All");
		// 已登记的模型不能再 new（原版按 ID 判重），可变实例从规范模型 ToMutable 得到。
		foreach (CardModel canonical in TarotCards.All)
		{
			Expect(HextechTarotCardGeneration.IsTarotCard(canonical.ToMutable()), $"{canonical.GetType().Name} is a tarot card");
		}

		Expect(!HextechTarotCardGeneration.IsTarotCard(new Sadness()), "Sadness is not a tarot card");
		Expect(!HextechTarotCardGeneration.IsTarotCard(new Joy()), "Joy is not a tarot card");
		Expect(!HextechTarotCardGeneration.IsTarotCard(new MythicForm()), "Mythic Form is not a tarot card");
		Expect(!HextechTarotCardGeneration.IsTarotCard(new StrikeIronclad()), "Strike is not a tarot card");
	}

	/// <summary>
	/// 挑塔罗牌：一次挑多张不重复；超过 22 张时下一轮允许重复且每轮仍覆盖全部候选；
	/// 相同盐结果相同，不同盐（序号）结果不同；单机与联机都从全部 22 种里挑。
	/// </summary>
	[HextechTest]
	private static void FoolTarotPickIsDistinctAndStable()
	{
		using IDisposable models = InjectMissingModels(VanillaTarotCardTypes);
		Player owner = CreateFoolPileTestPlayers(1)[0];

		List<CardModel> three = HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 3, "tarot-cloth", "ordinal", "0");
		Equal(3, three.Count, "picks the requested count");
		Equal(3, three.Distinct().Count(), "a batch has no duplicates");
		SequenceEqual(three, HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 3, "tarot-cloth", "ordinal", "0"), "same salt picks the same cards");
		Expect(three.All(static card => card.IsCanonical), "picks canonical models for CreateCard");

		HashSet<CardModel> singles = [];
		for (int ordinal = 0; ordinal < 16; ordinal++)
		{
			singles.Add(HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 1, "crystal-ball", "ordinal", ordinal.ToString()).Single());
		}

		Expect(singles.Count > 1, "different ordinals pick different tarot cards");

		List<CardModel> all = HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 22, "tarot-cloth");
		Equal(22, all.Distinct().Count(), "22 picks cover every tarot card exactly once");
		List<CardModel> overflow = HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 25, "tarot-cloth");
		Equal(25, overflow.Count, "more picks than candidates are still filled");
		Equal(22, overflow.Take(22).Distinct().Count(), "the first round has no duplicates");
		Equal(0, HextechTarotCardGeneration.PickCanonicalTarotCards(owner, 0, "tarot-cloth").Count, "zero picks nothing");

		List<Player> coop = CreateFoolPileTestPlayers(2);
		Equal(22, HextechTarotCardGeneration.PickCanonicalTarotCards(coop[0], 22, "tarot-cloth").Distinct().Count(), "every tarot card is allowed in multiplayer");
	}

	/// <summary>
	/// 塔罗牌桌布重放：持有者自己的塔罗牌出牌次数 +{Replays}（含神话形态、水晶球等其他来源产出的同类型牌），
	/// 非塔罗牌、队友的塔罗牌、非愚者持有者都不变；重复求值结果一致（只读）。
	/// </summary>
	[HextechTest]
	private static void FoolTarotClothReplaysOnlyOwnTarotCards()
	{
		using IDisposable models = InjectMissingModels(VanillaTarotCardTypes);
		List<Player> players = CreateFoolPileTestPlayers(2);
		Player owner = players[0], teammate = players[1];
		TarotClothRune rune = CreateMutableTestModel<TarotClothRune>();
		rune.Owner = owner;
		int replays = rune.DynamicVars["Replays"].IntValue;

		foreach (CardModel canonical in TarotCards.All)
		{
			CardModel tarot = CreateOwnedTarotCard(canonical, owner);
			string name = canonical.GetType().Name;
			Equal(1 + replays, rune.ModifyCardPlayCount(tarot, null, 1), $"{name} is replayed");
			Equal(2 + replays, rune.ModifyCardPlayCount(tarot, null, 2), $"{name}: replays stack on top of other modifiers");
			Equal(1 + replays, rune.ModifyCardPlayCount(tarot, null, 1), $"{name}: repeated evaluation is stable");
		}

		Equal(1, rune.ModifyCardPlayCount(AddFoolTestCard<Joy>(owner, PileType.Hand), null, 1), "non-tarot cards are not replayed");
		Equal(1, rune.ModifyCardPlayCount(AddFoolTestCard<MythicForm>(owner, PileType.Hand), null, 1), "Mythic Form itself is not replayed");
		Equal(1, rune.ModifyCardPlayCount(CreateOwnedTarotCard(ModelDb.Card<TheSun>(), teammate), null, 1), "a teammate's tarot card is not replayed");

		Player ironclad = CreateFoolPileTestPlayers(1, "FOOL-PILES", typeof(Ironclad))[0];
		TarotClothRune foreign = CreateMutableTestModel<TarotClothRune>();
		foreign.Owner = ironclad;
		Equal(1, foreign.ModifyCardPlayCount(CreateOwnedTarotCard(ModelDb.Card<TheSun>(), ironclad), null, 1), "non-Fool holders do not replay");
	}

	private static CardModel CreateOwnedTarotCard(CardModel canonical, Player owner)
	{
		CardModel card = canonical.ToMutable();
		card.Owner = owner;
		PileType.Hand.GetPile(owner).AddInternal(card);
		return card;
	}

	/// <summary>
	/// 水晶球：战斗开始与持有者自己的洗牌各触发一次，队友洗牌不触发；持有者不是愚者时都不触发。
	/// 触发判定以"本场序号是否推进"观察：夹具里摘掉持有者生物的 CombatState，让加牌在生成前返回
	/// （测试进程里原版 CreateCard/生成命令需要真实战斗，会触达原生层）。
	/// </summary>
	[HextechTest]
	private static void FoolCrystalBallTriggersOnCombatStartAndOwnShufflesOnly()
	{
		List<Player> players = CreateFoolPileTestPlayers(2);
		Player owner = players[0], teammate = players[1];
		CrystalBallRune rune = CreateMutableTestModel<CrystalBallRune>();
		rune.Owner = owner;
		System.Reflection.FieldInfo ordinal = AccessTools.Field(typeof(CrystalBallRune), "_addOrdinal");

		// 序号是本场内的盐：每场 BeforeCombatStart 清零后由战斗开始那次推进到 1。
		AccessTools.Property(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "CombatState").SetValue(owner.Creature, null);
		ordinal.SetValue(rune, 7);
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(1, (int)ordinal.GetValue(rune)!, "combat start resets the ordinal and triggers once");
		rune.AfterShuffle(new BlockingPlayerChoiceContext(), owner).GetAwaiter().GetResult();
		Equal(2, (int)ordinal.GetValue(rune)!, "the holder's own shuffle triggers");

		ordinal.SetValue(rune, 5);
		rune.AfterShuffle(new BlockingPlayerChoiceContext(), teammate).GetAwaiter().GetResult();
		Equal(5, (int)ordinal.GetValue(rune)!, "a teammate's shuffle does not trigger");

		Player ironclad = CreateFoolPileTestPlayers(1, "FOOL-PILES", typeof(Ironclad))[0];
		CrystalBallRune foreign = CreateMutableTestModel<CrystalBallRune>();
		foreign.Owner = ironclad;
		foreign.BeforeCombatStart().GetAwaiter().GetResult();
		foreign.AfterShuffle(new BlockingPlayerChoiceContext(), ironclad).GetAwaiter().GetResult();
		Equal(0, (int)ordinal.GetValue(foreign)!, "non-Fool holders never trigger");
		Equal(0, PileType.Draw.GetPile(ironclad).Cards.Count, "non-Fool holders add nothing");
	}
}
#endif
