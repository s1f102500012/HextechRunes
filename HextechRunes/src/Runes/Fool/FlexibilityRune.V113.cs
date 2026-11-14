#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class FlexibilityRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StretchPower>()];

	// "获得拉伸"= 持有者自己的拉伸正数变化（首次施加与叠加都经 AfterPowerAmountChanged，实参是修正后的实际变化量）。
	// 原版出牌后的整笔扣除是负数，不算获得。
	// 防回传：柔韧分给队友的拉伸施加者是给出者、没有来源牌；收到这种拉伸的队友即使也持有柔韧，也不再转发。
	// 原版与本模组的其他来源都不是这个形状：自己给自己的（伸展系卡牌、运动垫、隐秘之根、热身等）施加者是自己；
	// 队友给的瑜伽、狂喜施加者是队友但带来源牌。两端看到同一组实参，判断一致，不需要异步作用域标记。
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (power is not StretchPower
			|| power.Owner != Owner.Creature
			|| amount <= 0m
			|| IsSharedByTeammate(applier, cardSource)
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		// 按战斗里的玩家顺序（各端一致）逐个结算；死亡的队友跳过，等待后再核一次存活。
		List<Creature> teammates = combatState.Players
			.Where(player => player != Owner && player.Creature.IsAlive)
			.Select(static player => player.Creature)
			.ToList();
		if (teammates.Count == 0)
		{
			return;
		}

		Flash();
		foreach (Creature teammate in teammates)
		{
			if (teammate.IsDead)
			{
				continue;
			}

			await PowerCmd.Apply<StretchPower>(choiceContext, teammate, amount, Owner.Creature, null);
		}
	}

	private bool IsSharedByTeammate(Creature? applier, CardModel? cardSource)
	{
		return cardSource == null
			&& applier != null
			&& applier != Owner.Creature
			&& applier.Player != null;
	}
}
#endif
