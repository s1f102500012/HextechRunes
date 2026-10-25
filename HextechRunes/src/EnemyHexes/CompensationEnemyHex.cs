using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

internal sealed class CompensationEnemyHex : HextechEnemyHexEffect
{
	// 效果是单例（HextechEnemyHexEffects），状态放静态字段。
	// ModifyHpLostAfterOsty 是查询钩子，只记下"最近一次查询拆出了什么"；原版只在真实伤害里、紧接着这次查询调
	// AfterModifyingHpLostAfterOsty，由它把记录确认成待结算项，AfterEnemyDamageReceivedAny 再按目标取走施加。
	// 不依赖伤害命令的 AsyncLocal 作用域：作用域在某一端丢失时，只有那一端不拆分，联机分歧（主机全额、客户端拆分）。
	private static PendingCompensation? _lastQuerySplit;
	private static readonly List<PendingCompensation> PendingCompensations = [];

	internal override MonsterHexKind Kind => MonsterHexKind.Compensation;

	internal override void ResetRunScopedState()
	{
		ClearAll();
	}

	internal override Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		ClearAll();
		return Task.CompletedTask;
	}

	internal override Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		ClearAll();
		return Task.CompletedTask;
	}

	internal override Task AfterCombatVictory(HextechEnemyHexContext context, CombatRoom room)
	{
		ClearAll();
		return Task.CompletedTask;
	}

	internal override decimal ModifyHpLostAfterOsty(HextechEnemyHexContext context, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		// 分发层已限定 target 在本局战斗中。每次查询都覆盖记录：没拆分的查询不能让上一次的记录被确认。
		_lastQuerySplit = null;
		if (target.Side != CombatSide.Enemy
			|| target.IsDead
			|| ShouldSkipDamageReplacement()
			|| amount <= 0m)
		{
			return amount;
		}

		(decimal immediateDamage, int nextTurnDamage) = SplitDamage(amount);
		if (nextTurnDamage <= 0)
		{
			return amount;
		}

		_lastQuerySplit = new PendingCompensation(target, nextTurnDamage, dealer, cardSource);
		return immediateDamage;
	}

	internal override void AfterModifyingHpLostAfterOsty(HextechEnemyHexContext context)
	{
		if (_lastQuerySplit is not { } split)
		{
			return;
		}

		_lastQuerySplit = null;
		ConfirmPendingCompensation(split);
	}

	internal override async Task AfterEnemyDamageReceivedAny(HextechEnemyHexContext context, Creature target, DamageResult result, Creature? dealer, CardModel? cardSource)
	{
		if (!TryTakePendingCompensation(target, out PendingCompensation? compensation))
		{
			return;
		}

		if (!CanApplyPendingCompensation(context, target, compensation))
		{
			return;
		}

		Creature applier = compensation.Dealer is { IsAlive: true } ? compensation.Dealer : target;
		await HextechCombatHooks.RunWithCompensationReplacementGuard(
			() => PowerCmd.Apply<HextechNextTurnDamagePower>(target, compensation.Amount, applier, compensation.CardSource));
	}

	private static void ClearAll()
	{
		_lastQuerySplit = null;
		PendingCompensations.Clear();
	}

	internal static (decimal ImmediateDamage, int NextTurnDamage) SplitDamage(decimal damage)
	{
		if (damage <= 0m)
		{
			return (damage, 0);
		}

		int nextTurnDamage = (int)Math.Min(Math.Floor(damage / 2m), HextechCreatureStatLimits.StatHardCap);
		return (damage - nextTurnDamage, nextTurnDamage);
	}

	internal static bool ShouldSkipDamageReplacement()
	{
		// 血肉戏法(Sleight of Flesh)在玩家给敌人施加 debuff 时会对该敌人造成一次伤害。
		// 这次伤害不能再被代偿延期,否则「血肉戏法伤害 → 下回合伤害(debuff) → 血肉戏法响应 → …」
		// 会无限递归直至栈溢出。源头切断这条边:代偿在血肉戏法响应期间不替换伤害。
		// 与「代偿施加下回合伤害时抑制血肉戏法响应」(RunWithCompensationReplacementGuard)构成双向防护。
		return HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse
			|| HextechCombatHooks.IsResolvingSleightOfFleshPowerDebuffResponse
			|| HextechNextTurnDamagePower.IsResolvingDamage;
	}

	// 同一目标在受伤回调取走之前又确认一次时累加，与旧实现按目标合并一致。
	private static void ConfirmPendingCompensation(PendingCompensation split)
	{
		for (int i = PendingCompensations.Count - 1; i >= 0; i--)
		{
			PendingCompensation pending = PendingCompensations[i];
			if (pending.Target == split.Target)
			{
				PendingCompensations[i] = pending with
				{
					Amount = pending.Amount + split.Amount,
					Dealer = split.Dealer ?? pending.Dealer,
					CardSource = split.CardSource ?? pending.CardSource
				};
				return;
			}
		}

		PendingCompensations.Add(split);
	}

	private static bool TryTakePendingCompensation(Creature target, [NotNullWhen(true)] out PendingCompensation? pending)
	{
		for (int i = 0; i < PendingCompensations.Count; i++)
		{
			pending = PendingCompensations[i];
			if (pending.Target != target)
			{
				continue;
			}

			PendingCompensations.RemoveAt(i);
			return true;
		}

		pending = null;
		return false;
	}

	private static bool CanApplyPendingCompensation(HextechEnemyHexContext context, Creature target, PendingCompensation compensation)
	{
		return compensation.Amount > 0m
			&& target.IsAlive
			&& target.CombatState?.RunState == context.RunState;
	}

	private sealed record PendingCompensation(Creature Target, decimal Amount, Creature? Dealer, CardModel? CardSource);
}
