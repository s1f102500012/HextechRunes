using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

public sealed partial class PiercingThreadRune : HextechRelicBase
{
	internal const decimal PiercingPercent = 50m;

	// 静态登记表：TryTakeBlockableDamage 由静态伤害补丁调用，拿不到符文实例，只能从这里找有待结算记录的符文。
	// 清理：每条伤害命令结束时在 finally 里按命令 ID 清掉(HextechCombatHooks.DamageCommand)，战斗开始/结束再整体清空。
	// 中途退出对局时被放弃的命令不会走到 finally，符文实例会留到进程结束；命令 ID 单调递增不复用，残留记录不会误匹配，
	// 只占内存。改成实例字段需要在静态补丁里遍历所有玩家遗物，收益不抵改动风险，暂保持静态。
	private static readonly HashSet<PiercingThreadRune> RunesWithPendingDamage = new();

	private readonly List<PendingPiercingDamage> _pendingDamage = [];

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PiercingPercent", PiercingPercent)
	];

	public override Task BeforeCombatStart()
	{
		ClearPendingDamageForRune();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ClearPendingDamageForRune();
		return Task.CompletedTask;
	}

	// 在原版结算格挡之前登记这次伤害要穿透的量，随后由 Creature.DamageBlockInternal 的前缀取走。
	// 登记入口随版本不同：0.113.x 及更早是 BeforeDamageReceived（PiercingThreadRune.Legacy.cs），
	// 0.114.0 起原版先试算格挡再派发 BeforeDamageReceived，改由 CreatureCmd.GetDamageResults 的前缀登记
	// （HextechCombatHooks.PiercingThread.V114.cs）。只在真实伤害命令里登记，预览与模拟不登记。
	internal void RegisterPendingDamage(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target.Side != CombatSide.Enemy
			|| amount <= 0m
			|| props.HasFlag(ValueProp.Unblockable)
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		long commandId = HextechCombatHooks.CurrentActualDamageCommandId;
		int piercingDamage = CalculatePiercingDamage(amount);
		if (commandId == 0L || piercingDamage <= 0)
		{
			return;
		}

		Creature blockReceiver = target.PetOwner?.Creature ?? target;
		_pendingDamage.Add(new PendingPiercingDamage(commandId, blockReceiver, target, amount, props, piercingDamage));
		RunesWithPendingDamage.Add(this);
	}

	internal static int CalculatePiercingDamage(decimal amount)
	{
		decimal piercingDamage = Math.Floor(amount * PiercingPercent / 100m);
		return (int)Math.Min(piercingDamage, HextechCreatureStatLimits.StatHardCap);
	}

	internal static bool TryTakeBlockableDamage(
		long commandId,
		Creature blockReceiver,
		decimal amount,
		ValueProp props,
		out decimal blockableDamage)
	{
		if (commandId == 0L || RunesWithPendingDamage.Count == 0)
		{
			blockableDamage = amount;
			return false;
		}

		PiercingThreadRune[] runes = RunesWithPendingDamage.ToArray();
		foreach (PiercingThreadRune rune in runes)
		{
			if (!rune.TryTakePendingDamage(commandId, blockReceiver, amount, props, out PendingPiercingDamage? pending))
			{
				continue;
			}

			blockableDamage = Math.Max(0m, amount - pending.PiercingDamage);
			rune.Flash([pending.EffectTarget]);
			return true;
		}

		blockableDamage = amount;
		return false;
	}

	internal static void ClearPendingDamage(long commandId)
	{
		if (RunesWithPendingDamage.Count == 0)
		{
			return;
		}

		PiercingThreadRune[] runes = RunesWithPendingDamage.ToArray();
		foreach (PiercingThreadRune rune in runes)
		{
			rune._pendingDamage.RemoveAll(pending => pending.CommandId == commandId);
			rune.RemoveFromPendingRegistryIfEmpty();
		}
	}

	private bool TryTakePendingDamage(
		long commandId,
		Creature blockReceiver,
		decimal amount,
		ValueProp props,
		[NotNullWhen(true)] out PendingPiercingDamage? pending)
	{
		for (int i = _pendingDamage.Count - 1; i >= 0; i--)
		{
			pending = _pendingDamage[i];
			if (pending.CommandId != commandId
				|| pending.BlockReceiver != blockReceiver
				|| pending.Amount != amount
				|| pending.Props != props)
			{
				continue;
			}

			_pendingDamage.RemoveAt(i);
			RemoveFromPendingRegistryIfEmpty();
			return true;
		}

		pending = null;
		return false;
	}

	private void ClearPendingDamageForRune()
	{
		_pendingDamage.Clear();
		RunesWithPendingDamage.Remove(this);
	}

	private void RemoveFromPendingRegistryIfEmpty()
	{
		if (_pendingDamage.Count == 0)
		{
			RunesWithPendingDamage.Remove(this);
		}
	}

	private sealed record PendingPiercingDamage(
		long CommandId,
		Creature BlockReceiver,
		Creature EffectTarget,
		decimal Amount,
		ValueProp Props,
		int PiercingDamage);
}
