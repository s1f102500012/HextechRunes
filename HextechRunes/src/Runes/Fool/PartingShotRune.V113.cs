#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class PartingShotRune
{
	// 已射出、还没扎穿的持有者的箭，以及这次射击期间持有者对各目标造成的箭矢伤害（含被格挡部分）。
	private ArrowSlot? _arrowInFlight;
	private readonly Dictionary<Creature, int> _inFlightDamageByTarget = [];

	// 每支扎在敌人身上的箭：扎穿的目标与命中时造成的伤害。只在本场战斗内有效（箭矢槽每场新建），不存档。
	private readonly Dictionary<ArrowSlot, (Creature Target, int Damage)> _impaleDamageByArrow = [];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>()
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

	// 原版 ArrowCmd：TryShootArrow 触发 AfterArrowShot → ImpaleTarget 先结算攻击 → AddArrow → AfterArrowImpaled。
	// 所以从射出到扎穿之间持有者的 FromArrow 伤害就是这支箭命中时的伤害。
	public override Task AfterArrowShot(Player owner, ArrowSlot? arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (owner == Owner)
		{
			_arrowInFlight = arrow;
			_inFlightDamageByTarget.Clear();
		}

		return Task.CompletedTask;
	}

	// 伤害口径取 DamageResult.TotalDamage（格挡 + 未格挡），与原版 UnderworldPower / ReaperFormPower 按伤害换算时相同。
	public override Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (_arrowInFlight == null || dealer != Owner.Creature || !props.HasFlag(ValueProp.FromArrow))
		{
			return Task.CompletedTask;
		}

		_inFlightDamageByTarget[target] = _inFlightDamageByTarget.GetValueOrDefault(target) + result.TotalDamage;
		return Task.CompletedTask;
	}

	public override Task AfterArrowImpaled(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (arrow != _arrowInFlight)
		{
			return Task.CompletedTask;
		}

		// 箭命中直接击杀时，原版对已死目标施加扎穿不生效，这里拿到的能力没有持有者（Owner 为 null），
		// 随后原版立刻把箭松脱取回。没有可结算的目标，只清掉在途记录；这里抛异常会让原版的取回不执行。
		Creature? impaled = power.Owner;
		if (impaled != null)
		{
			_impaleDamageByArrow[arrow] = (impaled, _inFlightDamageByTarget.GetValueOrDefault(impaled));
		}

		_arrowInFlight = null;
		_inFlightDamageByTarget.Clear();
		return Task.CompletedTask;
	}

	// 追加伤害用 Unpowered（同模组其他追加伤害），不吃力量与瞄准，也不是有力攻击，不会让扎穿计数减少或再次松脱。
	// 目标已死（击杀松脱、敌人死亡时的全部松脱）不结算。
	public override async Task AfterArrowDislodged(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		if (arrow.owner != Owner || !_impaleDamageByArrow.Remove(arrow, out (Creature Target, int Damage) impale))
		{
			return;
		}

		if (impale.Target != power.Owner
			|| impale.Damage <= 0
			|| impale.Target.IsDead
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		Flash();
		await CreatureCmd.Damage(context, impale.Target, impale.Damage, ValueProp.Unpowered, Owner.Creature);
	}

	private void ResetCombatState()
	{
		_arrowInFlight = null;
		_inFlightDamageByTarget.Clear();
		_impaleDamageByArrow.Clear();
	}
}
#endif
