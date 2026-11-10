using Godot;

namespace HextechRunes;

public abstract partial class HextechRelicBase
{
	// 原版这三个非虚方法直接同步调用本机 UI 订阅者。闪光会加载场景和图标，
	// 计数刷新会访问节点；只隔离这些表现回调，不包裹调用方的状态修改或命令链。
	// 保留成功路径的事件时机，避免为数百个内容改写异步结算顺序。
	public new void Flash()
	{
		try
		{
			base.Flash();
		}
		catch (Exception ex)
		{
			HextechLog.Warn("RelicVisual", $"Flash failed for {GetType().Name}: {ex.Message}");
		}
	}

	public new void Flash(IEnumerable<Creature> targets)
	{
		try
		{
			base.Flash(targets);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("RelicVisual", $"Target flash failed for {GetType().Name}: {ex.Message}");
		}
	}

	protected new void InvokeDisplayAmountChanged()
	{
		try
		{
			base.InvokeDisplayAmountChanged();
		}
		catch (Exception ex)
		{
			HextechLog.Warn("RelicVisual", $"Counter refresh failed for {GetType().Name}: {ex.Message}");
		}
	}

	private HextechCombatState? _turnScopedCombatState;
	private int _turnScopedTurnNumber = -1;

	// "每回合 N 次"按持有者自己的回合计：原版 TurnNumber 在该玩家开始新回合（含额外回合）时递增，
	// 队友的额外回合不变。联机计数（Modifier 记账）按同一口径只在参与回合的玩家开始回合时清零。
	protected void EnsureTurnScopedStateCurrent(Action resetState)
	{
		HextechCombatState? combatState = Owner?.Creature.CombatState;
		if (combatState == null)
		{
			resetState();
			_turnScopedCombatState = null;
			_turnScopedTurnNumber = -1;
			return;
		}

		if (!ReferenceEquals(_turnScopedCombatState, combatState)
			|| _turnScopedTurnNumber != GetOwnerTurnNumber(combatState))
		{
			resetState();
			UpdateTurnScopedStateIdentity(combatState);
		}
	}

	protected void UpdateTurnScopedStateIdentity(HextechCombatState? combatState = null)
	{
		combatState ??= Owner?.Creature.CombatState;
		_turnScopedCombatState = combatState;
		_turnScopedTurnNumber = combatState == null ? -1 : GetOwnerTurnNumber(combatState);
	}

	private int GetOwnerTurnNumber(HextechCombatState combatState)
	{
		return Owner?.PlayerCombatState?.TurnNumber ?? combatState.RoundNumber;
	}

	protected bool ShouldUseNetworkCombatHistory()
	{
		return IsNetworkMultiplayer()
			&& CombatManager.Instance?.IsInProgress == true
			&& Owner != null;
	}

	protected string GetStableTurnProcKey()
	{
		return GetStableTurnProcKey(GetType());
	}

	internal static string GetStableTurnProcKey(Type runtimeType)
	{
		if (runtimeType.Assembly == typeof(HextechRelicBase).Assembly)
		{
			return runtimeType.Name;
		}

		string assemblyName = runtimeType.Assembly.GetName().Name ?? "<unknown>";
		return $"{assemblyName}:{runtimeType.FullName ?? runtimeType.Name}";
	}

	// 每回合/每场计数一律记在持有这件遗物的实例上，单机联机同一套逻辑。
	// 以前联机时改记在 Mayhem Modifier 的共享账上，但那份账不进联机校验，也不跟遗物实例绑定：
	// #47 里房主那份账漏记了一次腐化树枝的触发，第二次生成两端序号不同而分歧，直到生成结果不同才被校验发现。
	// 遗物自己的字段由各端同一组钩子推进，清零时机由调用方在战斗/回合边界决定。
	protected bool HasTurnProcReachedLimit(string procKey, int localCount, int maxPerTurn)
	{
		return localCount >= maxPerTurn;
	}

	protected int GetTurnProcCount(string procKey, int localCount)
	{
		return localCount;
	}

	protected bool HasTurnProcTriggered(string procKey, bool localTriggered)
	{
		return HasTurnProcReachedLimit(procKey, localTriggered ? 1 : 0, 1);
	}

	protected bool TryConsumeTurnProc(string procKey, ref int localCount, int maxPerTurn)
	{
		if (maxPerTurn <= 0 || localCount >= maxPerTurn)
		{
			return false;
		}

		localCount++;
		UpdateTurnScopedStateIdentity();
		InvokeDisplayAmountChanged();
		return true;
	}

	protected bool TryConsumeTurnProc(string procKey, ref bool localTriggered)
	{
		int localCount = localTriggered ? 1 : 0;
		if (!TryConsumeTurnProc(procKey, ref localCount, 1))
		{
			return false;
		}

		localTriggered = true;
		return true;
	}

	// 只读、不消费。ModifyCardPlayCount 等引擎可能针对同一次出牌重复调用的钩子只应在这里取序号;
	// 真正的消费必须放在每次真实出牌只触发一次的钩子里调用 ConsumeCombatProcOrdinal,
	// 否则各端序号推进次数不一致会导致稳定随机结果分叉。
	protected int PeekCombatProcOrdinal(string procKey, int localCount)
	{
		return localCount;
	}

	protected int ConsumeCombatProcOrdinal(string procKey, ref int localCount)
	{
		return localCount++;
	}

	protected void FlashDeferred(IEnumerable<Creature>? targets = null)
	{
		try
		{
			Creature[] targetArray = targets?.ToArray() ?? Array.Empty<Creature>();
			Callable.From(() => Flash(targetArray)).CallDeferred();
		}
		catch (Exception ex)
		{
			HextechLog.Warn("RelicVisual", $"Deferred flash failed for {GetType().Name}: {ex.Message}");
		}
	}
}
