namespace HextechRunes;

// 敌方翱翔：与原版 SoarPower 同样只减攻击伤害，但不靠落地招式解除，而是每层管一回合，
// 敌方回合结束时减 1 层。与原版翱翔是两个独立实例，同时存在时减伤相乘。
public sealed class HextechSoarPower : HextechPowerBase
{
	private const decimal DamageMultiplier = 0.5m;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return target == Owner && props.IsPoweredAttack() ? DamageMultiplier : 1m;
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == CombatSide.Enemy && Amount > 0)
		{
			await HextechPowerCmdCompat.Decrement(this);
		}
	}
}
