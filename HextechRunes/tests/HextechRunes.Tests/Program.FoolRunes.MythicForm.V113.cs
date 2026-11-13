#if STS2_113_OR_NEWER
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 升级：神话形态。效果由 AutoPlayFormsAtCombatStartRuneBase 提供；夹具不开始战斗，
// 真正的移到出牌区与原版自动打出（CardPileCmd.Add / CardCmd.AutoPlay）要实机确认，这里测入口判定与不进合并结算。
internal static partial class Program
{
	private static System.Reflection.FieldInfo MythicFormStartedField =>
		AccessTools.Field(typeof(AutoPlayFormsAtCombatStartRuneBase<MythicForm>), "_startedThisCombat");

	/// <summary>只对愚者可得；获得时补 1 张神话形态（基类 CardUpgradeRuneBase 的拾起效果）。</summary>
	[HextechTest]
	private static void FoolMythicFormUpgradeIsFoolOnlyAndGrantsCard()
	{
		MythicFormUpgradeRune rune = CreateMutableTestModel<MythicFormUpgradeRune>();
		Expect(rune.IsAvailableForPlayer(CreateFoolPileTestPlayers(1)[0]), "available to the Fool");
		Expect(!rune.IsAvailableForPlayer(CreateFoolPileTestPlayers(1, "FOOL-PILES", typeof(Ironclad))[0]), "not available to other characters");
		Expect(rune.GrantsCardOnPickup && rune.HasUponPickupEffect, "grants one Mythic Form on pickup");
	}

	/// <summary>
	/// 开局自动打出只在持有者本场第一个自己的回合（AfterPlayerTurnStartLate）进行一次：队友的回合、非愚者持有者都不动；
	/// 持有者的第一个回合消耗本场标志，之后（含额外回合）不再触发；BeforeCombatStart 重置。
	/// 消耗堆里的神话形态不算"你所有的"，此时没有可打出的牌，直接结束。
	/// </summary>
	[HextechTest]
	private static void FoolMythicFormUpgradeRunsOnceOnOwnersFirstTurn()
	{
		List<Player> players = CreateFoolPileTestPlayers(2);
		Player owner = players[0], teammate = players[1];
		MythicFormUpgradeRune rune = CreateMutableTestModel<MythicFormUpgradeRune>();
		rune.Owner = owner;
		MythicForm exhausted = AddFoolTestCard<MythicForm>(owner, PileType.Exhaust);
		MythicForm teammateForm = AddFoolTestCard<MythicForm>(teammate, PileType.Hand);
		rune.BeforeCombatStart().GetAwaiter().GetResult();

		rune.AfterPlayerTurnStartLate(new BlockingPlayerChoiceContext(), teammate).GetAwaiter().GetResult();
		Expect(!(bool)MythicFormStartedField.GetValue(rune)!, "a teammate's turn start does not consume the combat flag");
		Equal(PileType.Hand, teammateForm.Pile?.Type, "a teammate's Mythic Form is not auto-played");

		rune.AfterPlayerTurnStartLate(new BlockingPlayerChoiceContext(), owner).GetAwaiter().GetResult();
		Expect((bool)MythicFormStartedField.GetValue(rune)!, "the holder's first turn consumes the combat flag");
		Equal(PileType.Exhaust, exhausted.Pile?.Type, "an exhausted Mythic Form is not one of \"your\" forms");

		MythicForm late = AddFoolTestCard<MythicForm>(owner, PileType.Hand);
		rune.AfterPlayerTurnStartLate(new BlockingPlayerChoiceContext(), owner).GetAwaiter().GetResult();
		Equal(PileType.Hand, late.Pile?.Type, "later turns (including extra turns) do not auto-play again");

		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Expect(!(bool)MythicFormStartedField.GetValue(rune)!, "the next combat starts fresh");

		Player ironclad = CreateFoolPileTestPlayers(1, "FOOL-PILES", typeof(Ironclad))[0];
		MythicFormUpgradeRune foreign = CreateMutableTestModel<MythicFormUpgradeRune>();
		foreign.Owner = ironclad;
		MythicForm ironcladForm = AddFoolTestCard<MythicForm>(ironclad, PileType.Hand);
		foreign.AfterPlayerTurnStartLate(new BlockingPlayerChoiceContext(), ironclad).GetAwaiter().GetResult();
		Equal(PileType.Hand, ironcladForm.Pile?.Type, "non-Fool holders do not auto-play");
	}

	/// <summary>神话形态不进形态合并结算：多张同时在出牌区时合并入口返回 false，基类回退到逐张原版自动打出。</summary>
	[HextechTest]
	private static void FoolMythicFormFallsBackToPerCardAutoPlay()
	{
		Player owner = CreateFoolPileTestPlayers(1)[0];
		List<CardModel> forms = [AddFoolTestCard<MythicForm>(owner, PileType.Play), AddFoolTestCard<MythicForm>(owner, PileType.Play)];
		using (HextechFormAutoPlayHooks.BeginCardPlayBatch(forms))
		{
			Expect(!HextechFormAutoPlayHooks.TryPlayCombinedFinalEffect(new BlockingPlayerChoiceContext(), forms).GetAwaiter().GetResult(),
				"Mythic Form batches are not combined");
		}

		Expect(forms.All(static card => card.Pile?.Type == PileType.Play), "declining the combined path leaves the cards for per-card auto-play");
	}
}
#endif
