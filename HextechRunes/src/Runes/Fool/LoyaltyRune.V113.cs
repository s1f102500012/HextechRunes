#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class LoyaltyRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>()
	];

	// 持有者自己的回合开始时：与热身（LimberUpRune）、原版箭术（ArcheryPower）同一时机（己方阵营回合开始后），
	// 基类已按参与者过滤，队友的额外回合不触发，持有者自己的额外回合触发。这个时机在原版 SetupPlayerTurn
	// （起手抽牌、AfterPlayerTurnStart）之后，所以松脱引发的效果（如箭无虚发抽牌）发生在起手抽牌之后。
	// 松脱走原版卡牌召回（Recall）/连根拔起（Uproot）同一个公开入口，原版"松脱时"效果照常触发；
	// 只处理持有者的扎穿能力，队友的箭不动。多个目标按战斗编号逐个结算，各端顺序一致。
	// 该钩子不带选择上下文；松脱后续的钩子（抽牌等）可能需要等待，用阻塞上下文而不是遇到选择就抛异常的上下文。
	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side || Owner.Creature.IsDead || !IsFoolPlayer(Owner))
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
		PlayerChoiceContext choiceContext = new BlockingPlayerChoiceContext();
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
