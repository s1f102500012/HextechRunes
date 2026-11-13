#if STS2_113_OR_NEWER
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;

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
	/// 柔韧：原版花费拉伸（自身层数的负数变化、无施加者、无来源牌）后补回 {RegainPercent}%，向下取整（5 → 2、6 → 3、1 → 0）；
	/// 获得拉伸、有施加者的扣减、队友的拉伸不算花费；补回本身是正数施加，回传给钩子也不会循环。
	/// 与原版 ChoKi 同一次出牌：ChoKi 的正数补回不算，随后那一笔扣除只返还一次。
	/// </summary>
	[HextechTest]
	private static void FoolFlexibilityRegainsHalfOfSpentStretch()
	{
		// 能力实例要在测试作用域登记 StretchPower 模型之前构造（已登记的模型类型不能再直接 new）。
		(CombatState _, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		StretchPower stretch = CreateTestPower<StretchPower>(0, fool.Creature);
		StretchPower teammateStretch = CreateTestPower<StretchPower>(0, teammate.Creature);
		using FoolMelodyTestScope scope = new();
		FlexibilityRune rune = AttachFoolTestRune<FlexibilityRune>(fool);

		rune.AfterPowerAmountChanged(null!, stretch, -5m, null, null).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool.Creature, 2m, (Creature?)fool.Creature) }, FoolMelodyTestScope.StretchApplications, "spending 5 regains 2 (rounded down)");
		rune.AfterPowerAmountChanged(null!, stretch, 2m, fool.Creature, null).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "the regained Stretch is not spending again");

		rune.AfterPowerAmountChanged(null!, stretch, -1m, null, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, stretch, -4m, fool.Creature, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, teammateStretch, -6m, null, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, CreateTestPower<StrengthPower>(0, fool.Creature), -6m, null, null).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "spending 1, attributed reductions, teammates and other powers do not regain");

		FoolMelodyTestScope.StretchApplications.Clear();
		rune.AfterPowerAmountChanged(null!, stretch, 4m, fool.Creature, null).GetAwaiter().GetResult();
		rune.AfterPowerAmountChanged(null!, stretch, -6m, null, null).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool.Creature, 3m, (Creature?)fool.Creature) }, FoolMelodyTestScope.StretchApplications, "Cho-Ki's own regain is ignored and the following spend of 6 regains 3 once");
	}
}
#endif
