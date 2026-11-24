#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class AimForge
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<AimPower>()];

	// 时机与命令同渐入佳境（原版箭术 ArcheryPower）：己方阵营回合开始后施加；基类已按参与者过滤，
	// 队友的额外回合不触发，持有者自己的额外回合触发。
	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<AimPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, Stacked(DynamicVars["AimPower"].BaseValue), Owner.Creature, null);
	}
}

public sealed partial class StretchForge
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StretchPower>()];

	// 时机与命令同热身（原版隐秘之根、运动垫）。
	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StretchPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, Stacked(DynamicVars["StretchPower"].BaseValue), Owner.Creature, null);
	}
}

public sealed partial class ArrowForge
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForArrow(null)];

	// 与箭袋满满同一时机与命令：原版在 Hook.BeforeCombatStart 之前已按角色重建箭矢槽，这里加的槽在本场战斗内一直保留；
	// 每场战斗新建 PlayerCombatState，不需要自己的计数或清零。
	public override Task BeforeCombatStart()
	{
		if (Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		int arrows = (int)Stacked(DynamicVars["Arrows"].BaseValue);
		for (int i = 0; i < arrows; i++)
		{
			ArrowCmd.GainArrowSlot(Owner);
		}

		Flash();
		return Task.CompletedTask;
	}
}
#endif
