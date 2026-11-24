using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static MoveState FanTheHammerTestMove(string id, params AbstractIntent[] intents)
	{
		return new MoveState(id, static _ => Task.CompletedTask, intents);
	}

	private static bool FanTheHammerPerformedFirstMove(MonsterMoveStateMachine stateMachine)
	{
		return (bool)typeof(MonsterMoveStateMachine)
			.GetField("_performedFirstMove", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(stateMachine)!;
	}

	[HextechTest]
	private static void EnemyFanTheHammerAdvancesOnceAndHoldsPulledMoveForNextRoll()
	{
		MoveState first = FanTheHammerTestMove("FIRST", new BuffIntent());
		MoveState second = FanTheHammerTestMove("SECOND", new DebuffIntent());
		MoveState third = FanTheHammerTestMove("THIRD", new BuffIntent());
		first.FollowUpState = second;
		second.FollowUpState = third;
		third.FollowUpState = first;
		MonsterMoveStateMachine stateMachine = new([first, second, third], first);
		Creature[] targets = [];
		Creature owner = null!;
		Rng rng = new();

		// 战斗第一回合：原版 RollMove 停在初始行动上，A 还没执行。
		Expect(ReferenceEquals(first, stateMachine.RollMove(targets, owner, rng)), "first roll should stay on the initial move");
		Expect(
			HextechCombatHooks.TryAdvanceMoveStateMachineOnce(stateMachine, first, targets, owner, rng, out MoveState? pulled),
			"Fan the Hammer should advance a first-turn move");
		Expect(ReferenceEquals(second, pulled), "Fan the Hammer should pull the next turn's move");
		Equal(2, stateMachine.StateLog.Count, "pulled move should be logged like a normal roll");

		// B 没在本回合执行时，下一次原版 RollMove 原样交出 B，不再往前推进。
		HextechCombatHooks.HoldCurrentMoveForNextRoll(stateMachine);
		Expect(ReferenceEquals(second, stateMachine.RollMove(targets, owner, rng)), "held move should be delivered on the next roll");
		Equal(2, stateMachine.StateLog.Count, "delivering the held move must not advance the sequence");

		// B 执行之后照常推进。
		stateMachine.OnMovePerformed(second);
		Expect(ReferenceEquals(third, stateMachine.RollMove(targets, owner, rng)), "sequence should continue after the held move");
	}

	[HextechTest]
	private static void EnemyFanTheHammerLeavesLockedMovesUntouched()
	{
		MoveState locked = FanTheHammerTestMove("LOCKED", new BuffIntent());
		locked.MustPerformOnceBeforeTransitioning = true;
		MoveState next = FanTheHammerTestMove("NEXT", new BuffIntent());
		locked.FollowUpState = next;
		next.FollowUpState = locked;
		MonsterMoveStateMachine stateMachine = new([locked, next], locked);
		Creature[] targets = [];
		Rng rng = new();
		stateMachine.RollMove(targets, null!, rng);

		Expect(
			!HextechCombatHooks.TryAdvanceMoveStateMachineOnce(stateMachine, locked, targets, null!, rng, out _),
			"a move that must be performed first cannot be advanced past");
		Equal(1, stateMachine.StateLog.Count, "a refused advance must not log a move");
		Expect(!FanTheHammerPerformedFirstMove(stateMachine), "a refused advance must restore the first-move flag");
		Expect(
			!HextechCombatHooks.TryAdvanceMoveStateMachineOnce(stateMachine, next, targets, null!, rng, out _),
			"advancing from a move that is not the current state must be refused");
	}

	[HextechTest]
	private static void EnemyFanTheHammerSkipsStateMachinesWithConditionalBranches()
	{
		MoveState attack = FanTheHammerTestMove("ATTACK", new BuffIntent());
		MoveState defend = FanTheHammerTestMove("DEFEND", new BuffIntent());
		RandomBranchState random = new("RANDOM");
		random.AddBranch(attack, MoveRepeatType.CanRepeatForever);
		random.AddBranch(defend, MoveRepeatType.CannotRepeat);
		attack.FollowUpState = random;
		defend.FollowUpState = random;
		Expect(
			HextechCombatHooks.IsFanTheHammerStateMachineEligible(new MonsterMoveStateMachine([attack, defend, random], attack)),
			"move and random-branch states are eligible");

		MoveState low = FanTheHammerTestMove("LOW", new BuffIntent());
		MoveState high = FanTheHammerTestMove("HIGH", new BuffIntent());
		ConditionalBranchState conditional = new("CONDITIONAL");
		conditional.AddState(low, static () => true);
		conditional.AddState(high, static () => false);
		low.FollowUpState = conditional;
		high.FollowUpState = conditional;
		Expect(
			!HextechCombatHooks.IsFanTheHammerStateMachineEligible(new MonsterMoveStateMachine([low, high, conditional], low)),
			"conditional branches read live combat state and must not be evaluated a turn early");
	}

	[HextechTest]
	private static void EnemyFanTheHammerListsBothTurnsWithJeweledGauntletRepeats()
	{
		BuffIntent buff = new();
		DebuffIntent debuff = new();
		IReadOnlyList<AbstractIntent> both =
			HextechCombatHooks.ComposeMonsterTurnIntents([buff], repeatCurrent: false, followUp: [debuff], repeatFollowUp: false);
		Equal(2, both.Count, "current and pulled move intent count");
		Expect(ReferenceEquals(buff, both[0]) && ReferenceEquals(debuff, both[1]), "current move is listed before the pulled move");

		IReadOnlyList<AbstractIntent> repeated =
			HextechCombatHooks.ComposeMonsterTurnIntents([buff], repeatCurrent: true, followUp: [debuff], repeatFollowUp: true);
		Equal(4, repeated.Count, "both moves repeated by Jeweled Gauntlet");
		Expect(
			ReferenceEquals(buff, repeated[0]) && ReferenceEquals(buff, repeated[1])
				&& ReferenceEquals(debuff, repeated[2]) && ReferenceEquals(debuff, repeated[3]),
			"intents follow execution order: A, repeated A, B, repeated B");
	}
}
