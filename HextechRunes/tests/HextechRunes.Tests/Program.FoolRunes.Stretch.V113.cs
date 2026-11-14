#if STS2_113_OR_NEWER
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace HextechRunes.Tests;

// 拉伸组：热身、柔韧。施加拉伸只记录调用（见 FoolMelodyTestScope），不走真实 PowerCmd。
internal static partial class Program
{
	/// <summary>
	/// 热身：持有者阵营回合开始给自己 {StretchPower} 点拉伸；敌方回合、只有队友参与的额外回合、非愚者持有者都不触发；
	/// 持有者自己的额外回合照常触发。
	/// </summary>
	[HextechTest]
	private static void FoolLimberUpGainsStretchAtOwnTurnStart()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		LimberUpRune rune = AttachFoolTestRune<LimberUpRune>(fool);
		decimal stretch = rune.DynamicVars["StretchPower"].BaseValue;

		rune.AfterSideTurnStart(CombatSide.Player, [fool.Creature, teammate.Creature], combat).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool.Creature, stretch, (Creature?)fool.Creature) }, FoolMelodyTestScope.StretchApplications, "turn start grants Stretch to the holder");

		rune.AfterSideTurnStart(CombatSide.Enemy, [], combat).GetAwaiter().GetResult();
		rune.AfterSideTurnStart(CombatSide.Player, [teammate.Creature], combat).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "enemy turns and a teammate's extra turn do not trigger");
		rune.AfterSideTurnStart(CombatSide.Player, [fool.Creature], combat).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.StretchApplications.Count, "the holder's own extra turn triggers");

		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		rune.AfterSideTurnStart(CombatSide.Player, [fool.Creature], combat).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.StretchApplications.Count, "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 柔韧：持有者获得拉伸（正数变化）时，按战斗里的玩家顺序给每名存活队友等量拉伸，施加者为持有者、没有来源牌。
	/// 自己给的（施加者是自己）、队友卡牌给的（施加者是队友、带来源牌）都算获得；原版出牌后的扣除（负数）、
	/// 别人身上的拉伸、其他能力、非愚者持有者都不触发；死亡队友跳过。
	/// </summary>
	[HextechTest]
	private static void FoolFlexibilitySharesGainedStretchWithTeammates()
	{
		// 能力实例要在测试作用域登记 StretchPower 模型之前构造（已登记的模型类型不能再直接 new）。
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		List<Player> players = JoinFoolMelodyPlayersToCombat(combat, [fool, teammate], 3);
		Player third = players[2];
		StretchPower stretch = CreateTestPower<StretchPower>(0, fool.Creature);
		StretchPower teammateStretch = CreateTestPower<StretchPower>(0, teammate.Creature);
		StrengthPower strength = CreateTestPower<StrengthPower>(0, fool.Creature);
		CardModel teammateCard = CreateFoolTestCard<StrikeIronclad>(teammate);
		using FoolMelodyTestScope scope = new();
		FlexibilityRune rune = AttachFoolTestRune<FlexibilityRune>(fool);

		rune.AfterPowerAmountChanged(null!, stretch, 4m, fool.Creature, null).GetAwaiter().GetResult();
		SequenceEqual(
			new[] { (teammate.Creature, 4m, (Creature?)fool.Creature), (third.Creature, 4m, (Creature?)fool.Creature) },
			FoolMelodyTestScope.StretchApplications,
			"self-applied Stretch is shared with every teammate in combat order");

		FoolMelodyTestScope.StretchApplications.Clear();
		rune.AfterPowerAmountChanged(null!, stretch, 6m, teammate.Creature, teammateCard).GetAwaiter().GetResult();
		SequenceEqual(
			new[] { (teammate.Creature, 6m, (Creature?)fool.Creature), (third.Creature, 6m, (Creature?)fool.Creature) },
			FoolMelodyTestScope.StretchApplications,
			"Stretch from a teammate's card (Yoga / Enrapture) counts as gaining it");

		FoolMelodyTestScope.StretchApplications.Clear();
		rune.AfterPowerAmountChanged(null!, stretch, -5m, null, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, teammateStretch, 3m, teammate.Creature, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, strength, 3m, fool.Creature, null).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.StretchApplications.Count, "the vanilla spend, a teammate's Stretch and other powers do not share");

		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(third.Creature, 0);
		rune.AfterPowerAmountChanged(null!, stretch, 2m, null, null).GetAwaiter().GetResult();
		SequenceEqual(
			new[] { (teammate.Creature, 2m, (Creature?)fool.Creature) },
			FoolMelodyTestScope.StretchApplications,
			"dead teammates are skipped; unattributed gains still count");

		FoolMelodyTestScope.StretchApplications.Clear();
		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		rune.AfterPowerAmountChanged(null!, stretch, 4m, fool.Creature, null).GetAwaiter().GetResult();
		Equal(0, FoolMelodyTestScope.StretchApplications.Count, "non-Fool holders do not share");
	}

	/// <summary>
	/// 柔韧双持不回传：队友因柔韧得到的拉伸施加者是给出者、没有来源牌，收到的一方即使也持有柔韧也不再转发；
	/// 这一笔从任何一端看都是同一组实参，两端判断一致。
	/// </summary>
	[HextechTest]
	private static void FoolFlexibilityDoesNotEchoBetweenTwoHolders()
	{
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		JoinFoolMelodyPlayersToCombat(combat, [fool, teammate]);
		StretchPower stretch = CreateTestPower<StretchPower>(0, fool.Creature);
		StretchPower teammateStretch = CreateTestPower<StretchPower>(0, teammate.Creature);
		using FoolMelodyTestScope scope = new();
		FlexibilityRune rune = AttachFoolTestRune<FlexibilityRune>(fool);
		FlexibilityRune teammateRune = AttachFoolTestRune<FlexibilityRune>(teammate);

		rune.AfterPowerAmountChanged(null!, stretch, 3m, fool.Creature, null).GetAwaiter().GetResult();
		SequenceEqual(
			new[] { (teammate.Creature, 3m, (Creature?)fool.Creature) },
			FoolMelodyTestScope.StretchApplications,
			"the first holder shares once");

		// 原版把这笔施加分发给所有监听者：队友的柔韧看到的是"施加者是对方、无来源牌"。
		rune.AfterPowerAmountChanged(null!, teammateStretch, 3m, fool.Creature, null).GetAwaiter().GetResult();
		teammateRune.AfterPowerAmountChanged(null!, teammateStretch, 3m, fool.Creature, null).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "Stretch received through Flexibility is not shared back");
	}

	/// <summary>柔韧仅联机出现、仍只给愚者：单机愚者、联机非愚者都拿不到，联机愚者可以。</summary>
	[HextechTest]
	private static void FoolFlexibilityIsMultiplayerOnlyAndFoolOnly()
	{
		Player fool = CreateCharacterTestPlayer(typeof(Fool));
		Player ironclad = CreateCharacterTestPlayer(typeof(Ironclad));
		FlexibilityRune rune = CreateMutableTestModel<FlexibilityRune>();
		WithFeedbackNetGameType(NetGameType.Singleplayer, () => Expect(!rune.IsAvailableForPlayer(fool), "single-player Fool cannot get Flexibility"));
		WithFeedbackNetGameType(NetGameType.Host, () =>
		{
			Expect(rune.IsAvailableForPlayer(fool), "multiplayer Fool can get Flexibility");
			Expect(!rune.IsAvailableForPlayer(ironclad), "multiplayer non-Fool cannot get Flexibility");
		});
	}
}
#endif
