#if !STS2_114_OR_NEWER
namespace HextechRunes;

// 0.113.x 及更早：原版在结算格挡之前派发 BeforeDamageReceived，穿透量在这里登记。
public sealed partial class PiercingThreadRune
{
	public override Task BeforeDamageReceived(
		PlayerChoiceContext choiceContext,
		Creature target,
		decimal amount,
		ValueProp props,
		Creature? dealer,
		CardModel? cardSource)
	{
		RegisterPendingDamage(target, amount, props, dealer, cardSource);
		return Task.CompletedTask;
	}
}
#endif
