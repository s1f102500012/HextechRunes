namespace HextechRunes;

// 自有 Power 基类。0.108.0 起伤害管线加 CardPlay 参数:游戏侧 override 按版本二选一,子类统一重写版本无关的
// Compat 虚方法。回合钩子把原版 participants 原样传给 *ForParticipants 虚方法,默认按持有者是否参与本次回合过滤。
public abstract class HextechPowerBase : PowerModel
{
	// 自有 Power 的本地 UI 事件不能截断后续共享命令；不拦截原版或第三方 Power。
	protected new void Flash()
	{
		HextechPresentation.TryRun("PowerVisual", $"Flash failed for {GetType().Name}", base.Flash);
	}

	protected new void InvokeDisplayAmountChanged()
	{
		HextechPresentation.TryRun("PowerVisual", $"Counter refresh failed for {GetType().Name}", base.InvokeDisplayAmountChanged);
	}

	public virtual decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return 1m;
	}

	public sealed override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
	{
		return ModifyDamageMultiplicativeCompat(target, amount, props, dealer, cardSource);
	}

	public virtual Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	public sealed override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		if (ShouldSkipOwnerTurn(side, participants))
		{
			return Task.CompletedTask;
		}

		return BeforeSideTurnStart(choiceContext, side, combatState);
	}

	public virtual Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	/// <summary>
	/// 默认按持有者的回合参与状态过滤后转发；需要自定义参与者语义的能力可覆盖此入口。
	/// 异阵营触发保留，例如敌人身上的下回合伤害在玩家回合开始时结算。
	/// </summary>
	public virtual Task AfterSideTurnStartForParticipants(CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		if (ShouldSkipOwnerTurn(side, participants))
		{
			return Task.CompletedTask;
		}

		return AfterSideTurnStart(side, combatState);
	}

	public sealed override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		return AfterSideTurnStartForParticipants(side, participants, combatState);
	}

	public virtual Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	// 与回合开始相同：默认过滤同阵营缺席的持有者，覆盖此入口可自定义参与者语义。
	public virtual Task BeforeTurnEndForParticipants(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (ShouldSkipOwnerTurn(side, participants))
		{
			return Task.CompletedTask;
		}

		return BeforeTurnEnd(choiceContext, side);
	}

	public sealed override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return BeforeTurnEndForParticipants(choiceContext, side, participants);
	}

	public virtual Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	// 与回合结束前相同：默认过滤同阵营缺席的持有者，覆盖此入口可自定义参与者语义。
	public virtual Task AfterTurnEndForParticipants(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		if (ShouldSkipOwnerTurn(side, participants))
		{
			return Task.CompletedTask;
		}

		return AfterTurnEnd(choiceContext, side);
	}

	public sealed override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return AfterTurnEndForParticipants(choiceContext, side, participants);
	}

	// 原版回合开始的 participants 在普通回合含宠物、额外回合不含，玩家侧回合结束则永远只含玩家本人；
	// 宠物身上的 Power 按其主人是否参与本次回合判定，否则奥斯提身上的灼烧在回合结束永远不会结算。
	private bool ShouldSkipOwnerTurn(CombatSide side, IEnumerable<Creature> participants)
	{
		return Owner is { } owner
			&& side == owner.Side
			&& !HextechTurnParticipants.Includes(participants, owner);
	}
}
