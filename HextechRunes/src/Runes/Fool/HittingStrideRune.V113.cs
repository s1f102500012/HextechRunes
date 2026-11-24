#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class HittingStrideRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<AimPower>()];

	// 时机与命令同原版箭术（ArcheryPower）与热身：己方阵营回合开始后，ThrowingPlayerChoiceContext 施加瞄准；
	// 基类已按参与者过滤，队友的额外回合不触发，持有者自己的额外回合触发。
	// "当前回合数"取战斗轮次 RoundNumber（回合横幅显示的数字，同星光璀璨），各端一致；
	// 额外回合不推进轮次，所以额外回合给的层数与本轮相同。原版历石（StoneCalendar）等按 PlayerCombatState.TurnNumber 计，额外回合会多 1。
	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		int aim = GetAimAmount(combatState);
		if (aim <= 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<AimPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, aim, Owner.Creature, null);
	}

	internal static int GetAimAmount(HextechCombatState combatState)
	{
		return combatState.RoundNumber;
	}
}
#endif
