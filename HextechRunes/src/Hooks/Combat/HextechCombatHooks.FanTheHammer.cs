using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// 敌方「连拨击锤」：怪物 RollMove 定下本回合行动 A 之后，按稳定随机判定是否把状态机再推进一步得到 B
	// （"下一回合本来会做的行动"），A、B 一起列在头顶意图上，敌方回合里执行完 A 接着执行 B。
	//
	// 推进只用原版公开的 MonsterMoveStateMachine.RollMove，与原版回合开始的推进是同一条路径：消耗同一条
	// RunRng.MonsterAi、写同一份 StateLog，只是提前了一步。它发生在 PrepareForNextTurn / AfterCreatureAdded
	// 调用的 MonsterModel.RollMove 里，各端按同一顺序执行；判定只读同步状态，所以各端推进结果一致。
	// 原版战斗不在中途存档，读档从房间开头重打，同样的操作序列会重现同样的推进。
	//
	// 推进后原版不变量"状态机当前状态 == NextMove"被临时打破（当前状态是 B、NextMove 仍是 A）。
	// 任何 SetMoveImmediate（眩晕、阶段转换、复活、偷窃草蜢的逃跑）都会经 ForceCurrentState 恢复不变量，
	// 此时 B 随之作废，与原版"强制改招覆盖原计划"一致。B 没能在本回合执行时（B 本身不适合连续行动、
	// 怪物本回合没有行动等），下一次 RollMove 前把 _performedFirstMove 置回 false，让原版 RollMove
	// 原样返回 B 而不再推进：被推进出来的那一步要么本回合连着执行，要么下回合照常执行，不会丢失。

	// MonsterMoveStateMachine._currentState（MonsterState）与 _performedFirstMove（bool），0.107.1/0.111.0/0.113.0 原版私有字段；
	// MonsterModel.NextMove 的 private setter 同三版。任一缺失时 RollMove 补丁的 Prepare 返回 false，连拨击锤整体停用。
	private static readonly FieldInfo MoveStateMachineCurrentStateField =
		TryGetField(typeof(MonsterMoveStateMachine), "_currentState")!;
	private static readonly FieldInfo MoveStateMachinePerformedFirstMoveField =
		TryGetField(typeof(MonsterMoveStateMachine), "_performedFirstMove")!;
	private static readonly MethodInfo MonsterNextMoveSetter =
		TryGetPropertySetter(typeof(MonsterModel), nameof(MonsterModel.NextMove))!;

	// 只登记本模组推进出来的那一步；不在登记表里的"当前状态 != NextMove"属于别人的状态机，不去动它。
	// 怪物模型每场战斗都是新实例，表项随怪物回收，执行或顺延 B 时移除。
	private static readonly ConditionalWeakTable<MonsterModel, FanTheHammerFollowUp> FanTheHammerFollowUps = new();

	private static bool FanTheHammerHooksAvailable =>
		JeweledGauntletHooksAvailable
		&& MoveStateMachineCurrentStateField != null
		&& MoveStateMachinePerformedFirstMoveField != null
		&& MonsterNextMoveSetter != null;

	/// <summary>
	/// 判定成立时把状态机从 <paramref name="current"/> 再推进一步。没有真正推进（当前行动必须先执行一次、
	/// 状态机不允许离开等）时撤销对 <c>_performedFirstMove</c> 的改动并返回 false；推进成功则不可撤销。
	/// </summary>
	internal static bool TryAdvanceMoveStateMachineOnce(
		MonsterMoveStateMachine stateMachine,
		MoveState current,
		IEnumerable<Creature> targets,
		Creature owner,
		Rng rng,
		[NotNullWhen(true)] out MoveState? followUp)
	{
		followUp = null;
		if (!ReferenceEquals(MoveStateMachineCurrentStateField.GetValue(stateMachine), current)
			|| !current.CanTransitionAway)
		{
			return false;
		}

		// 战斗第一个行动还没执行时原版 FindNextMoveState 不推进；先把这一步视为已执行（A 本回合必定先执行）。
		bool performedFirstMove = (bool)MoveStateMachinePerformedFirstMoveField.GetValue(stateMachine)!;
		SetPerformedFirstMove(stateMachine, true);
		int loggedStates = stateMachine.StateLog.Count;
		MoveState rolled = stateMachine.RollMove(targets, owner, rng);
		// 真正推进时 FindNextMoveState 必定往 StateLog 追加落点（行动状态都会进日志）；推进到自身也算推进。
		if (stateMachine.StateLog.Count == loggedStates)
		{
			SetPerformedFirstMove(stateMachine, performedFirstMove);
			return false;
		}

		followUp = rolled;
		return true;
	}

	/// <summary>让下一次原版 RollMove 原样返回当前状态（推进出来的 B），不再往前推进。</summary>
	internal static void HoldCurrentMoveForNextRoll(MonsterMoveStateMachine stateMachine)
	{
		SetPerformedFirstMove(stateMachine, false);
	}

	private static void SetPerformedFirstMove(MonsterMoveStateMachine stateMachine, bool value)
	{
		MoveStateMachinePerformedFirstMoveField.SetValue(stateMachine, value);
	}

	/// <summary>
	/// 只在全部由原版定招状态（行动、随机分支）组成的状态机上推进。条件分支读的是生命值、召唤物数量、
	/// 自身计数器之类的实时状态，提前一步求值会在 A 与整个玩家回合发生之前替 B 做决定（知识恶魔的诅咒计数、
	/// 制造者的可制造数量、青蛙骑士的半血阶段……）；自定义状态类可能有任意副作用。原版随机分支的权重全为常数，
	/// 只读 StateLog，提前推进与按时推进抽到的结果分布相同。
	/// </summary>
	internal static bool IsFanTheHammerStateMachineEligible(MonsterMoveStateMachine stateMachine)
	{
		return stateMachine.States.Values.All(static state =>
			state.GetType() == typeof(MoveState) || state.GetType() == typeof(RandomBranchState));
	}

	private static bool IsFanTheHammerMoveEligible(MonsterModel monster, MoveState move)
	{
		return AreJeweledGauntletIntentsRepeatable(move.Intents)
			&& !ShouldSuppressJeweledGauntletRepeat(monster, move);
	}

	private static void TryPullFanTheHammerFollowUp(MonsterModel monster, IEnumerable<Creature> targets)
	{
		Creature creature = monster.Creature;
		if (creature.IsDead
			|| monster.IsPerformingMove
			|| monster.MoveStateMachine is not { } stateMachine
			|| creature.CombatId is not uint combatId
			|| creature.CombatState is not { } combatState
			|| combatState.RunState is not RunState runState
			|| !TryGetActiveEnemyHexModifier(creature, MonsterHexKind.FanTheHammer, out HextechMayhemModifier? modifier)
			|| !ReferenceEquals(MoveStateMachineCurrentStateField.GetValue(stateMachine), monster.NextMove)
			|| FanTheHammerFollowUps.TryGetValue(monster, out _))
		{
			return;
		}

		MoveState current = monster.NextMove;
		if (!IsFanTheHammerStateMachineEligible(stateMachine)
			|| !IsFanTheHammerMoveEligible(monster, current))
		{
			return;
		}

		int chance = modifier.GetMonsterHexStrengthTier(MonsterHexKind.FanTheHammer) switch
		{
			<= 1 => 10,
			2 => 20,
			_ => 30
		};
		if (!HextechStableRandom.PercentChance(
				runState,
				chance,
				"enemy-fan-the-hammer",
				combatId.ToString(CultureInfo.InvariantCulture),
				combatState.RoundNumber.ToString(CultureInfo.InvariantCulture),
				current.Id)
			|| !TryAdvanceMoveStateMachineOnce(stateMachine, current, targets, creature, monster.RunRng.MonsterAi, out MoveState? followUp))
		{
			return;
		}

		// 原版只给 RollMove 落定的 NextMove 补仪式兽/啃咬机意图；B 不经 MonsterModel.RollMove，这里补上。
		// 不在 B 上规划偷牌：偷牌之后要逃跑，只在 B 真正成为 NextMove 时由原路径规划。
		AddMonsterUpgradeIntents(monster, followUp, rollTheft: false);
		bool runsThisTurn = IsFanTheHammerMoveEligible(monster, followUp);
		FanTheHammerFollowUps.AddOrUpdate(monster, new FanTheHammerFollowUp(current, followUp, runsThisTurn));
		HextechLog.Info(
			"FanTheHammer",
			$"Monster {monster.Id.Entry} pulled next move {followUp.Id} after {current.Id} (runs this turn: {runsThisTurn})");
	}

	/// <summary>
	/// 推进出来、且仍然有效的那一步；状态机被强制改写过（不变量已恢复）则返回 false。
	/// 只读，意图显示也会调用；失效的表项由逻辑路径（RollMove 前缀、开始执行 B）移除。
	/// </summary>
	private static bool TryGetPendingFanTheHammerFollowUp(
		MonsterModel monster,
		[NotNullWhen(true)] out FanTheHammerFollowUp? pending)
	{
		return FanTheHammerFollowUps.TryGetValue(monster, out pending)
			&& IsFanTheHammerFollowUpStillPending(monster, pending);
	}

	private static bool IsFanTheHammerFollowUpStillPending(MonsterModel monster, FanTheHammerFollowUp pending)
	{
		return monster.MoveStateMachine is { } stateMachine
			&& ReferenceEquals(monster.NextMove, pending.Current)
			&& ReferenceEquals(MoveStateMachineCurrentStateField.GetValue(stateMachine), pending.FollowUp);
	}

	/// <summary>意图显示与行动执行用：本回合要接着执行的 B。</summary>
	private static MoveState? GetFanTheHammerFollowUpForThisTurn(MonsterModel monster)
	{
		return TryGetPendingFanTheHammerFollowUp(monster, out FanTheHammerFollowUp? pending) && pending.RunsThisTurn
			? pending.FollowUp
			: null;
	}

	/// <summary>开始执行 B：B 成为 NextMove，状态机不变量随之恢复，下次 RollMove 照常从 B 推进。</summary>
	private static void BeginFanTheHammerFollowUp(MonsterModel monster, MoveState followUp)
	{
		FanTheHammerFollowUps.Remove(monster);
		MonsterNextMoveSetter.Invoke(monster, [followUp]);
	}

	private static void HoldUnperformedFanTheHammerFollowUp(MonsterModel monster)
	{
		if (!FanTheHammerFollowUps.TryGetValue(monster, out FanTheHammerFollowUp? pending))
		{
			return;
		}

		FanTheHammerFollowUps.Remove(monster);
		if (IsFanTheHammerFollowUpStillPending(monster, pending))
		{
			HoldCurrentMoveForNextRoll(monster.MoveStateMachine!);
			HextechLog.Info("FanTheHammer", $"Monster {monster.Id.Entry} keeps pulled move {pending.FollowUp.Id} for its next turn");
		}
	}

	private static void LogFanTheHammerFailure(string hook, Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("combat.fan-the-hammer-failure", 10))
		{
			HextechLog.Error("Mayhem", $"{hook} failed; enemy Fan the Hammer skipped this roll: {ex}");
		}
	}

	private sealed record FanTheHammerFollowUp(MoveState Current, MoveState FollowUp, bool RunsThisTurn);

	[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.RollMove), typeof(IEnumerable<Creature>))]
	[HextechPatch("combat.fan-the-hammer.roll-move", "连拨击锤", MonsterHexes = [MonsterHexKind.FanTheHammer])]
	private static class FanTheHammerRollMovePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => FanTheHammerHooksAvailable;

		// 上一次推进出来、却没在本回合执行的 B：让原版这次 RollMove 原样返回它。
		[HarmonyPrefix]
		private static void Prefix(MonsterModel __instance)
		{
			try
			{
				HoldUnperformedFanTheHammerFollowUp(__instance);
			}
			catch (Exception ex)
			{
				LogFanTheHammerFailure("FanTheHammerRollMovePatch.Prefix", ex);
			}
		}

		// 排在升级意图补丁（仪式兽/偷窃草蜢/啃咬机）之后：判定要看到 A 上已规划的偷牌意图。
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Low)]
		private static void Postfix(MonsterModel __instance, IEnumerable<Creature> targets)
		{
			try
			{
				TryPullFanTheHammerFollowUp(__instance, targets);
			}
			catch (Exception ex)
			{
				LogFanTheHammerFailure("FanTheHammerRollMovePatch.Postfix", ex);
			}
		}
	}
}
