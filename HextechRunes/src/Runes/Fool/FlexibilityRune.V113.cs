#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class FlexibilityRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StretchPower>()];

	// "花费拉伸"= 原版 StretchPower.AfterCardPlayed 里那一步 PowerCmd.ModifyAmount(-出牌开始时层数, applier: null, cardSource: null)；
	// 原版没有其他扣除拉伸的路径。AfterPowerAmountChanged 在归零移除之前触发，这里补回后层数大于 0，能力不会被移除。
	// 补回走正数 Apply（施加者为持有者），不满足"负数且无施加者"，不会再被当成花费而循环。
	// 原版 ChoKi 在出牌中途按当时层数正数补回、不经这里；柔韧只看随后那一笔扣除，每笔扣除返还一次，两者各自独立。
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (power is not StretchPower
			|| power.Owner != Owner.Creature
			|| amount >= 0m
			|| applier != null
			|| cardSource != null
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		// 向下取整：花费 5 补回 2。
		int regained = FloorToInt(-amount * DynamicVars["RegainPercent"].BaseValue / 100m);
		if (regained <= 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StretchPower>(choiceContext, Owner.Creature, regained, Owner.Creature, null);
	}
}
#endif
