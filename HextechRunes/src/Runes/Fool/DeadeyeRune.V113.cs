#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class DeadeyeRune
{
	// 本次被原版 AimPower 登记、结束时会整笔扣除瞄准的箭矢攻击，以及攻击开始时的层数。
	private AttackCommand? _aimSpendingAttack;
	private int _aimAtAttackStart;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<AimPower>()
	];

	public override Task BeforeCombatStart()
	{
		_aimSpendingAttack = null;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_aimSpendingAttack = null;
		return Task.CompletedTask;
	}

	// 登记条件逐条照抄原版 AimPower.BeforeAttack（持有者的有力箭矢攻击、来源为空或卡牌、同一时刻只登记一次）。
	// 同一生物的能力先于遗物收到 BeforeAttack，此时读到的层数就是 AimPower 记下的层数。
	public override Task BeforeAttack(AttackCommand command)
	{
		if (_aimSpendingAttack != null
			|| command.Attacker != Owner.Creature
			|| !command.DamageProps.IsPoweredAttack()
			|| !command.DamageProps.HasFlag(ValueProp.FromArrow)
			|| (command.ModelSource != null && command.ModelSource is not CardModel)
			|| !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		int aim = Owner.Creature.GetPower<AimPower>()?.Amount ?? 0;
		if (aim <= 0)
		{
			return Task.CompletedTask;
		}

		_aimSpendingAttack = command;
		_aimAtAttackStart = aim;
		return Task.CompletedTask;
	}

	// AimPower.AfterAttack 先扣掉开始时的全部层数（归零即移除），之后遗物才收到 AfterAttack，
	// 在这里补回被消耗量的 (100 − AimSpentPercent)%，向下取整（消耗 5 → 补回 2）。
	// 补回落在新的瞄准实例上，下一次射击照常登记与消耗。多段攻击只有一次 AfterAttack，只补一次。
	public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
	{
		if (command != _aimSpendingAttack)
		{
			return;
		}

		_aimSpendingAttack = null;
		if (Owner.Creature.IsDead)
		{
			return;
		}

		int keptPercent = 100 - Math.Clamp(DynamicVars["AimSpentPercent"].IntValue, 0, 100);
		int retained = _aimAtAttackStart * keptPercent / 100;
		if (retained <= 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<AimPower>(choiceContext, Owner.Creature, retained, Owner.Creature, null);
	}
}
#endif
