namespace HextechRunes;

public sealed class CollectorRune : HextechRelicBase
{
	internal const decimal ExecutePercent = 10m;
	internal const int CountPerExecute = 20;

	private const string ExecutePercentVar = "ExecutePercent";
	private const string CountPerExecuteVar = "CountPerExecute";

	private readonly HashSet<Creature> _creditedExecutions = new(ReferenceEqualityComparer.Instance);
	private bool _executing;

	// 旧版本存档兼容占位：原为待发放的战后金币计数，金币已改为触发时立即发放；名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedCountThisCombat
	{
		get => 0;
		set { }
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar(ExecutePercentVar, ExecutePercent),
		new DynamicVar(CountPerExecuteVar, CountPerExecute)
	];

	public override async Task AfterDamageGiven(
		PlayerChoiceContext choiceContext,
		Creature? dealer,
		DamageResult result,
		ValueProp props,
		Creature target,
		CardModel? cardSource)
	{
		if (_executing
			|| Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| result.UnblockedDamage <= 0m
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		if (result.WasTargetKilled)
		{
			await RecordExecution(target, IsCreditableDeath(target));
			return;
		}

		if (!target.IsAlive
			|| !IsBelowExecuteThreshold(target.CurrentHp, target.MaxHp, DynamicVars[ExecutePercentVar].BaseValue))
		{
			return;
		}

		// 真死亡判定要在 Kill 之前算:Kill 完成后怪物已从战斗移除、CombatState 置空,
		// 事后再判会一律得到 false,处决就永远记不上金币。
		bool creditable = IsCreditableDeath(target);
		_executing = true;
		try
		{
			HextechCombatVfx.CollectorExecute(target);
			await CreatureCmd.Kill(target);
			await RecordExecution(target, creditable);
		}
		finally
		{
			_executing = false;
		}
	}

	/// <summary>
	/// 可计数的死亡:仍在战斗里就按真死亡规则判(排除 Boss 转阶段等);已被移出战斗(CombatState 为空)
	/// 说明它确实死透了,直接计数。
	/// </summary>
	internal static bool IsCreditableDeath(Creature target)
	{
		return target.CombatState == null || HextechMonsterInteractionPolicy.IsTrueCombatDeath(target);
	}

	public override Task BeforeCombatStart()
	{
		_creditedExecutions.Clear();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_creditedExecutions.Clear();
		return Task.CompletedTask;
	}

	internal async Task RecordExecution(Creature target, bool isCreditableDeath)
	{
		if (target.Side == Owner.Creature.Side
			|| !isCreditableDeath
			|| !_creditedExecutions.Add(target))
		{
			return;
		}

		await PlayerCmd.GainGold(DynamicVars[CountPerExecuteVar].IntValue, Owner);
		Flash();
		HextechCombatVfx.CollectorCoins(target);
	}

	internal static bool IsBelowExecuteThreshold(decimal currentHp, decimal maxHp, decimal executePercent)
	{
		return maxHp > 0m
			&& executePercent > 0m
			&& currentHp < maxHp * executePercent / 100m;
	}
}
