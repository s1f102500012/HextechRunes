#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class HeldShotRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.Static(StaticHoverTip.Block)
	];

	// 时机同原版奥利哈钢（Orichalcum）与精神护盾：己方阵营回合结束前（BeforeSideTurnEnd，基类已按参与者过滤），
	// 格挡在敌方行动前生效，并照常在持有者下回合开始时按原版规则清除。
	// "可用的箭矢"即原版 ArrowSlot.IsAvailable（已收回、此刻能发射），按原版 ArrowCmd.GetAvailableArrowCount 计；
	// 射出去扎在敌人身上的、被摧毁的都不算。原版箭只在发射、松脱、摧毁时改变状态，回合结束本身不收回箭。
	public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side != Owner.Creature.Side
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		int arrows = ArrowCmd.GetAvailableArrowCount(Owner);
		decimal block = DynamicVars.Block.BaseValue * arrows;
		if (block <= 0m)
		{
			return Task.CompletedTask;
		}

		Flash();
		return CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Unpowered, null);
	}
}
#endif
