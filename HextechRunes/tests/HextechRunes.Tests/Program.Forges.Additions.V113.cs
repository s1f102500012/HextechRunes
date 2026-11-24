#if STS2_113_OR_NEWER
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes.Tests;

// 愚者专属锻造器的战斗行为。瞄准走 FoolArrowCombat 的真实 PowerCmd 数值路径；拉伸用 FoolMelodyTestScope 只记录调用。
internal static partial class Program
{
	/// <summary>
	/// 瞄准锻造器：己方阵营回合开始时给持有者 {AimPower} 点瞄准，按叠层放大；敌方回合、只有队友参与的额外回合、
	/// 非愚者持有者都不给。
	/// </summary>
	[HextechTest]
	private static void FoolAimForgeGainsStackedAimAtOwnTurnStart()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 1);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		AimForge forge = CreateArrowRune<AimForge>(fool);
		int perStack = forge.DynamicVars["AimPower"].IntValue;
		int Aim(Player player) => player.Creature.GetPower<AimPower>()?.Amount ?? 0;
		void TurnStart(AimForge holder, CombatSide side, params Creature[] participants) =>
			holder.AfterSideTurnStart(side, participants, combat.Combat).GetAwaiter().GetResult();

		TurnStart(forge, CombatSide.Player, fool.Creature, teammate.Creature);
		Equal(perStack, Aim(fool), "one forge grants its Aim");
		Equal(0, Aim(teammate), "the teammate gains nothing");

		TurnStart(forge, CombatSide.Enemy);
		TurnStart(forge, CombatSide.Player, teammate.Creature);
		Equal(perStack, Aim(fool), "enemy turns and a teammate's extra turn do not trigger");

		forge.AddForgeStack(flash: false);
		int stacks = forge.StackCount;
		Expect(stacks > 1, "a second forge stacks on the first");
		TurnStart(forge, CombatSide.Player, fool.Creature);
		Equal(perStack + perStack * stacks, Aim(fool), "stacked forges grant Aim per stack");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 0);
		TurnStart(CreateArrowRune<AimForge>(ironclad), CombatSide.Player, ironclad.Creature);
		Equal(0, Aim(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>拉伸锻造器：己方阵营回合开始时给持有者 {StretchPower} 点拉伸；敌方回合、队友的额外回合、非愚者持有者都不给。</summary>
	[HextechTest]
	private static void FoolStretchForgeGainsStretchAtOwnTurnStart()
	{
		using FoolMelodyTestScope scope = new();
		(CombatState combat, Player fool, Player teammate) = CreateFoolMelodyCombatFixture();
		StretchForge forge = AttachFoolTestRune<StretchForge>(fool);
		decimal stretch = forge.DynamicVars["StretchPower"].BaseValue;

		forge.AfterSideTurnStart(CombatSide.Player, [fool.Creature, teammate.Creature], combat).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool.Creature, stretch, (Creature?)fool.Creature) }, FoolMelodyTestScope.StretchApplications, "turn start grants Stretch to the holder");

		forge.AfterSideTurnStart(CombatSide.Enemy, [], combat).GetAwaiter().GetResult();
		forge.AfterSideTurnStart(CombatSide.Player, [teammate.Creature], combat).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "enemy turns and a teammate's extra turn do not trigger");

		SetAutoProperty(fool, nameof(Player.Character), CreateCharacterTestPlayer(typeof(Ironclad)).Character);
		forge.AfterSideTurnStart(CombatSide.Player, [fool.Creature], combat).GetAwaiter().GetResult();
		Equal(1, FoolMelodyTestScope.StretchApplications.Count, "non-Fool holders do not trigger");
	}

	/// <summary>箭矢锻造器：战斗开始时在原版已初始化的箭矢槽之上每层再加 {Arrows} 个，新槽可立即发射；非愚者持有者不加。</summary>
	[HextechTest]
	private static void FoolArrowForgeAddsArrowSlotsAtCombatStart()
	{
		Player fool = CreateFoolCombatTestPlayer();
		ArrowForge forge = CreateMutableTestModel<ArrowForge>();
		forge.Owner = fool;
		int perStack = forge.DynamicVars["Arrows"].IntValue;
		forge.AddForgeStack(flash: false);
		int stacks = forge.StackCount;

		forge.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(1 + perStack * stacks, fool.PlayerCombatState!.Arrows.Count, "each forge stack adds its arrows on top of the base slot");
		Expect(fool.PlayerCombatState.Arrows.All(static arrow => arrow.Status == ArrowStatus.Retrieved), "added arrows are ready to shoot");

		Player ironclad = CreateCharacterTestPlayer(typeof(Ironclad), netId: 2);
		AttachArrowOnlyCombatState(ironclad, 0);
		ArrowForge foreign = CreateMutableTestModel<ArrowForge>();
		foreign.Owner = ironclad;
		foreign.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(0, ironclad.PlayerCombatState!.Arrows.Count, "non-Fool holders gain no arrows");
	}
}
#endif
