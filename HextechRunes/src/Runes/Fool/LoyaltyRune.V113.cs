#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class LoyaltyRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>()
	];

	// 持有者自己的回合结束时（基类已按参与者过滤本阵营回合，这里再排除敌方回合）。
	// 松脱走原版卡牌召回（Recall）/连根拔起（Uproot）同一个公开入口，原版"松脱时"效果照常触发；
	// 只处理持有者的扎穿能力，队友的箭不动。多个目标按战斗编号逐个结算，各端顺序一致。
	public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side != Owner.Creature.Side || Owner.Creature.IsDead || !IsFoolPlayer(Owner))
		{
			return;
		}

		HextechCombatState? combatState = Owner.Creature.CombatState;
		if (combatState == null)
		{
			return;
		}

		List<Creature> targets = combatState.Creatures
			.Where(creature => ArrowCmd.CreatureIsImpaledByPlayersArrow(creature, Owner))
			.OrderBy(static creature => creature.CombatId ?? uint.MaxValue)
			.ToList();
		if (targets.Count == 0)
		{
			return;
		}

		Flash();
		foreach (Creature target in targets)
		{
			while (ArrowCmd.CreatureIsImpaledByPlayersArrow(target, Owner))
			{
				await ArrowCmd.DislodgeArrow(target, Owner, choiceContext, null);
			}
		}
	}
}
#endif
