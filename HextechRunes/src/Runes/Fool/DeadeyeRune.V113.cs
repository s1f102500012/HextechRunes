#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class DeadeyeRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<AimPower>()
	];

	// 原版瞄准只有一处换算成伤害：AimPower.ModifyDamageAdditive（持有者的有力箭矢伤害、已登记攻击的卡牌来源，
	// 沉重射击再乘它的 AimMultiplier）。这里直接调用持有者身上瞄准的这个方法，把它的结果再加 (倍率 − 1) 份，
	// 于是“瞄准最终贡献的伤害”整体乘以倍率：普通射击 1 倍 → 2 倍，沉重射击 X 倍 → 2X 倍；条件与原版逐条一致，
	// 不另抄一份。走伤害加法修正钩子，所以卡牌伤害预览同步变化，乘法修正（易伤等）照常作用于加上后的总数。
	// 只改加伤，不碰原版的登记与消耗（攻击结束整笔扣除、散雨射击中途移除），预览可多次调用，这里只计算。
	public override decimal ModifyDamageAdditiveCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (dealer == null || dealer != Owner.Creature || !IsFoolPlayer(Owner))
		{
			return 0m;
		}

		decimal extraShare = DynamicVars["AimMultiplier"].BaseValue - 1m;
		if (extraShare == 0m)
		{
			return 0m;
		}

		decimal aimDamage = 0m;
		foreach (AimPower aim in dealer.GetPowerInstances<AimPower>())
		{
			aimDamage += aim.ModifyDamageAdditive(target, amount, props, dealer, cardSource, null);
		}

		return aimDamage * extraShare;
	}
}
#endif
