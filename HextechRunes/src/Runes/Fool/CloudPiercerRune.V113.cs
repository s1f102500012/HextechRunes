#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class CloudPiercerRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>(),
		HoverTipFactory.FromPower<VulnerablePower>()
	];

	// 时机与归属同原版爆炸箭矢（ExplosiveArrowsPower）：AfterArrowImpaled，且箭属于持有者（看箭的 owner）；每支箭扎穿各结算一次，
	// 包括随后被摧毁的箭（寂灭/Quietus 的箭也先派发该钩子再摧毁）。
	// 原版 ArrowCmd.ImpaleTarget 先结算这一箭的攻击再派发该钩子，所以这一箭自己吃不到这次易伤，之后的命中才吃到。
	// 箭命中直接击杀时原版对已死目标施加扎穿不生效，传进来的能力没有持有者（Owner 为 null），随后原版把箭松脱取回；
	// 这里什么都不做，也不能抛异常，否则原版的取回不执行。
	public override async Task AfterArrowImpaled(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		Creature? target = power.Owner;
		if (target == null
			|| target.IsDead
			|| arrow.owner != Owner
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		decimal vulnerable = DynamicVars.Vulnerable.BaseValue;
		if (vulnerable <= 0m)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<VulnerablePower>(context, target, vulnerable, Owner.Creature, null);
	}
}
#endif
