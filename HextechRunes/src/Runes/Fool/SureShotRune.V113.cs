#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class SureShotRune
{
	// 持有者正在结算的攻击（含 RainShot 这类由 AttackContext 包裹的攻击），攻击可能嵌套，所以按命令记录。
	private readonly List<AttackCommand> _ownerAttacksInFlight = [];

	// 攻击进行中松脱所得、留到攻击结束再获得的瞄准。
	private int _pendingAim;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<AimPower>()
	];

	public override Task BeforeCombatStart()
	{
		ResetCombatState();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetCombatState();
		return Task.CompletedTask;
	}

	// 原版 AimPower 在箭矢攻击开始时记下层数、攻击结束时整笔扣除，扣到 0 才移除并在下次获得时换新实例。
	// 攻击中途获得的瞄准会留在这个"已登记过攻击"的旧实例上，之后既不加伤也不再被消耗；RainShot 还会在攻击中途移除瞄准。
	// 所以持有者攻击进行中的松脱只记账，等最外层攻击的 AfterAttack 再获得。
	// 同一生物的能力先于遗物收到 AfterAttack，此时 AimPower 已完成扣除。
	public override Task BeforeAttack(AttackCommand command)
	{
		if (command.Attacker == Owner.Creature)
		{
			_ownerAttacksInFlight.Add(command);
		}

		return Task.CompletedTask;
	}

	public override async Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command)
	{
		if (!_ownerAttacksInFlight.Remove(command) || _ownerAttacksInFlight.Count > 0 || _pendingAim <= 0)
		{
			return;
		}

		int aim = _pendingAim;
		_pendingAim = 0;
		if (Owner.Creature.IsDead)
		{
			return;
		}

		await PowerCmd.Apply<AimPower>(choiceContext, Owner.Creature, aim, Owner.Creature, null);
	}

	// 口径同原版合股线（Twine）：AfterArrowDislodged 且箭属于持有者；被摧毁的箭原版不触发该钩子。
	public override async Task AfterArrowDislodged(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (arrow.owner != Owner || Owner.Creature.IsDead || !IsFoolPlayer(Owner))
		{
			return;
		}

		int aim = DynamicVars["AimPower"].IntValue;
		if (aim <= 0)
		{
			return;
		}

		Flash();
		if (_ownerAttacksInFlight.Count > 0)
		{
			_pendingAim += aim;
			return;
		}

		await PowerCmd.Apply<AimPower>(context, Owner.Creature, aim, Owner.Creature, null);
	}

	private void ResetCombatState()
	{
		_ownerAttacksInFlight.Clear();
		_pendingAim = 0;
	}
}
#endif
