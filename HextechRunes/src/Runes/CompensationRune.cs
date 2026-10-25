using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

public sealed class CompensationRune : HextechRelicBase
{
	// 与敌方代偿相同：查询钩子只记下最近一次查询，原版只在真实伤害里紧接着调的 AfterModifyingHpLostAfterOsty 确认，
	// AfterDamageReceived 取走施加。不依赖伤害命令的 AsyncLocal 作用域。
	private PendingCompensation? _lastQuerySplit;

	private readonly List<PendingCompensation> _pendingCompensations = [];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<DoomPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		ClearPendingCompensationsForRune();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ClearPendingCompensationsForRune();
		return Task.CompletedTask;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		ClearPendingCompensationsForRune();
		return Task.CompletedTask;
	}

	public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		_lastQuerySplit = null;
		if (target != Owner.Creature
			|| Owner.Creature.IsDead
			|| amount <= 0m
			|| !IsNecrobinderPlayer(Owner)
			|| !IsInActiveCombat(Owner))
		{
			return amount;
		}

		int doom = Math.Min((int)Math.Floor(amount), HextechCreatureStatLimits.StatHardCap);
		if (doom <= 0)
		{
			return amount;
		}

		_lastQuerySplit = new PendingCompensation(target, doom, dealer, cardSource);
		return 0m;
	}

	public override Task AfterModifyingHpLostAfterOsty()
	{
		if (_lastQuerySplit is { } split)
		{
			_lastQuerySplit = null;
			ConfirmPendingCompensation(split);
		}

		return Task.CompletedTask;
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner.Creature || !IsInActiveCombat(Owner))
		{
			return;
		}

		if (!TryTakePendingCompensation(target, out PendingCompensation? compensation))
		{
			return;
		}

		Flash();
		await HextechCombatHooks.RunWithCompensationReplacementGuard(
			() => PowerCmd.Apply<DoomPower>(Owner.Creature, compensation.Amount, compensation.Dealer ?? Owner.Creature, compensation.CardSource));
	}

	// 只在本局正在进行的战斗里替换伤害：事件等战斗外伤害，以及残留的其他局战斗状态都不算。
	private static bool IsInActiveCombat(Player owner)
	{
		return CombatManager.Instance?.IsInProgress == true
			&& owner.RunState.CurrentRoom is CombatRoom
			&& ReferenceEquals(owner.Creature.CombatState?.RunState, owner.RunState);
	}

	private void ConfirmPendingCompensation(PendingCompensation split)
	{
		for (int i = _pendingCompensations.Count - 1; i >= 0; i--)
		{
			PendingCompensation pending = _pendingCompensations[i];
			if (pending.Target == split.Target)
			{
				_pendingCompensations[i] = pending with
				{
					Amount = pending.Amount + split.Amount,
					Dealer = split.Dealer ?? pending.Dealer,
					CardSource = split.CardSource ?? pending.CardSource
				};
				return;
			}
		}

		_pendingCompensations.Add(split);
	}

	private bool TryTakePendingCompensation(Creature target, [NotNullWhen(true)] out PendingCompensation? pending)
	{
		for (int i = 0; i < _pendingCompensations.Count; i++)
		{
			pending = _pendingCompensations[i];
			if (pending.Target != target)
			{
				continue;
			}

			_pendingCompensations.RemoveAt(i);
			return true;
		}

		pending = null;
		return false;
	}

	private void ClearPendingCompensationsForRune()
	{
		_lastQuerySplit = null;
		_pendingCompensations.Clear();
	}

	private sealed record PendingCompensation(Creature Target, decimal Amount, Creature? Dealer, CardModel? CardSource);
}
