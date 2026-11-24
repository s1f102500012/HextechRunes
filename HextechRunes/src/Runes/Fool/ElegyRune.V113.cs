#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class ElegyRune
{
	// 持有者正在结算的攻击（含散雨射击这类由 AttackContext 包裹的攻击），攻击可能嵌套，所以按命令记录。
	// 战斗内状态，不存档（战斗中不存档，读档会重开战斗）。
	private readonly List<AttackCommand> _ownerAttacksInFlight = [];

	// 攻击进行中生成悲伤所得、留到最外层攻击结束再获得的瞄准。
	private int _pendingAim;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Sadness>(),
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

	// 原版 AimPower 在箭矢攻击开始时记下层数、攻击结束时整笔扣除，登记后不清除；攻击中途获得的瞄准留在这个
	// "已登记过攻击"的旧实例上，之后既不加伤也不再被消耗。所以持有者攻击进行中生成的悲伤只记账，
	// 等最外层攻击的 AfterAttack 再给（同一生物的能力先于遗物收到 AfterAttack，此时 AimPower 已完成扣除）。
	// 写法同 0.10.0 的箭无虚发（松脱给瞄准）。
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

	// "生成悲伤"口径同苦中作乐（原版万事无忧、肥料）：每张持有者生成并进入持有者牌堆的悲伤触发一次，
	// 一次生成多张逐张触发，战斗中变化出的悲伤也算。
	public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
	{
		if (!IsOwnSadnessGeneration(card, creator))
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

		await PowerCmd.Apply<AimPower>(new BlockingPlayerChoiceContext(), Owner.Creature, aim, Owner.Creature, null);
	}

	internal bool IsOwnSadnessGeneration(CardModel card, Player? creator)
	{
		return creator == Owner
			&& card is Sadness
			&& card.Owner == Owner
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner);
	}

	private void ResetCombatState()
	{
		_ownerAttacksInFlight.Clear();
		_pendingAim = 0;
	}
}
#endif
