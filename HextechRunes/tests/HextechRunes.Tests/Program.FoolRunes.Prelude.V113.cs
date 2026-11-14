#if STS2_113_OR_NEWER
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 前奏：起手抽牌改为从抽牌堆选牌。选牌界面与原版选择同步需要真实战斗流程，这里只覆盖张数计算与拦截闸门。
internal static partial class Program
{
	/// <summary>
	/// 张数 = min(本应抽的张数, 手牌剩余空间)；抽牌堆不多于这个张数时全部加入、不弹选择；没有空间或抽牌堆为空时什么都不做。
	/// </summary>
	[HextechTest]
	private static void FoolPreludeCountsCardsToAddFromTheOpeningDraw()
	{
		Equal((6, true), PreludeRune.GetCardsToAdd(6, 0, 10, 20), "6 of 20 needs a choice");
		Equal((6, false), PreludeRune.GetCardsToAdd(6, 0, 10, 6), "exactly 6 left: take all without a choice");
		Equal((4, false), PreludeRune.GetCardsToAdd(6, 0, 10, 4), "fewer than 6 left: take all");
		Equal((3, true), PreludeRune.GetCardsToAdd(6, 7, 10, 20), "hand limit caps the count");
		Equal((3, false), PreludeRune.GetCardsToAdd(6, 7, 10, 3), "pile no larger than the capped count: take all");
		Equal((0, false), PreludeRune.GetCardsToAdd(6, 10, 10, 20), "full hand adds nothing");
		Equal((0, false), PreludeRune.GetCardsToAdd(0, 0, 10, 20), "no opening draw adds nothing");
		Equal((0, false), PreludeRune.GetCardsToAdd(6, 0, 10, 0), "empty draw pile adds nothing");
	}

	/// <summary>
	/// 只拦持有者自己战斗第一回合的起手抽牌：要先经过本次起手的 BeforeHandDraw，且在真实抽牌请求内（模拟查询拿不到张数、不拦）；
	/// 回合内的抽牌、队友的起手、第二回合、非愚者持有者都不拦。拦下后不论是否结算都清掉本次状态。
	/// </summary>
	[HextechTest]
	private static void FoolPreludeOnlyInterceptsTheHoldersFirstOpeningDraw()
	{
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		PreludeRune rune = AttachFoolTestRune<PreludeRune>(fool);
		PlayerChoiceContext context = new ThrowingPlayerChoiceContext();

		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "not armed before this opening's BeforeHandDraw");

		rune.BeforeHandDraw(teammate, context, combat).GetAwaiter().GetResult();
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "a teammate's opening does not arm it");

		rune.BeforeHandDraw(fool, context, combat).GetAwaiter().GetResult();
		Expect(rune.ShouldDraw(fool, true), "a simulated query outside a draw is not intercepted");
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, false)), "in-turn draws are not intercepted");
		Expect(WithDrawRequest(teammate, 6, () => rune.ShouldDraw(teammate, true)), "a teammate's opening draw is not intercepted");
		Expect(!WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "the holder's first opening draw is intercepted");

		// 持有者已倒下时 AfterPreventingDraw 直接返回，但仍清掉本次状态。
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(fool.Creature, 0);
		rune.AfterPreventingDraw().GetAwaiter().GetResult();
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(fool.Creature, 50);
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "state is cleared after the intercepted draw");

		AdvanceFoolTestRound(combat, fool, teammate);
		rune.BeforeHandDraw(fool, context, combat).GetAwaiter().GetResult();
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "later turns are not intercepted");

		SetAutoProperty(fool.PlayerCombatState!, nameof(PlayerCombatState.TurnNumber), 1);
		rune.BeforeHandDraw(fool, context, combat).GetAwaiter().GetResult();
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "combat start resets the armed state");

		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		rune.BeforeHandDraw(fool, context, combat).GetAwaiter().GetResult();
		Expect(WithDrawRequest(fool, 6, () => rune.ShouldDraw(fool, true)), "non-Fool holders are not intercepted");
	}

	private static bool WithDrawRequest(Player player, int count, Func<bool> query)
	{
		HextechDrawRequest.Begin(player, count);
		try
		{
			return query();
		}
		finally
		{
			HextechDrawRequest.End();
		}
	}
}
#endif
