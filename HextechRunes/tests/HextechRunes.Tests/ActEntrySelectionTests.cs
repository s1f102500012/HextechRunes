using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using Step = HextechRunes.HextechRunLifecycleHooks.PostOpeningEventStep;
using GateStep = HextechRunes.HextechMayhemModifier.BeforeRoomSelectionStep;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>
	/// 开幕事件之后的主触发：不看玩家怎么离开事件，只等本机玩家已离开事件（见过地图打开且可以行进，锁存）、所有玩家的事件都完成；
	/// 条件不满足时只等待（等待期间不禁用行进），已经出发离开开幕位置就交给进房关口。
	/// </summary>
	[HextechTest]
	private static void ActEntrySelectionPostOpeningEventWaitsForMapAndAllEvents()
	{
		static Step Decide(
			bool run = true,
			bool resolved = false,
			bool left = false,
			bool leftEvent = true,
			bool traveling = false,
			int events = 2,
			int finished = 2,
			int players = 2)
			=> HextechRunLifecycleHooks.DecidePostOpeningEventStep(run, resolved, left, leftEvent, traveling, events, finished, players);

		Equal(Step.Select, Decide(), "local player left the event and every player's event finished starts the selection");
		Equal(Step.Select, Decide(events: 1, finished: 1, players: 1), "single player uses the same rule");
		Equal(Step.Wait, Decide(finished: 1), "a teammate still inside the event keeps waiting (their event choices may reserve sync ids)");
		Equal(Step.Wait, Decide(events: 1, finished: 1), "fewer events than players (another mod skipped per-player events) waits instead of starting");
		Equal(Step.Wait, Decide(events: 0, finished: 0), "no event objects at all waits; the gate before the first room still covers it");
		Equal(Step.Wait, Decide(leftEvent: false), "local player still on the event screen (never saw the map open with travel enabled) waits");
		Equal(Step.Wait, Decide(traveling: true), "travel animation already running waits for the room change");
		Equal(Step.StopLeftOpeningRoom, Decide(left: true), "after the vote moved the party the synchronized gate takes over");
		Equal(Step.StopLeftOpeningRoom, Decide(left: true, finished: 0), "leaving the opening room stops even if events never reported finished");
		Equal(Step.StopResolved, Decide(resolved: true, left: true), "an already resolved act stops first");
		Equal(Step.StopRunEnded, Decide(run: false, resolved: true), "run change or save-and-quit cleanup stops before anything else");
	}

	/// <summary>
	/// 进房前兜底关口：第一幕不再整幕跳过，只排除原版开局/读档流程内部；第二幕起与额外阶段的分支保持原样。
	/// </summary>
	[HextechTest]
	private static void ActEntrySelectionBeforeRoomGateCoversFirstAct()
	{
		static GateStep Decide(int stage = 0, bool resolved = false, bool eventOrMap = false, bool firstAct = true, bool insideEntry = false)
			=> HextechMayhemModifier.DecideBeforeRoomSelection(stage, resolved, eventOrMap, firstAct, insideEntry);

		Equal(GateStep.FirstActGate, Decide(), "unresolved act0 before a combat/shop/treasure/rest room resolves at the gate");
		Equal(GateStep.DeferFirstActToLoadResume, Decide(insideEntry: true), "act0 inside vanilla LoadRun/StartRun is left to the load resume path");
		Equal(GateStep.None, Decide(resolved: true), "resolved act0 does nothing");
		Equal(GateStep.None, Decide(eventOrMap: true), "event and map rooms are never gated");
		Equal(GateStep.None, Decide(stage: -1), "invalid stage does nothing");
		Equal(GateStep.LaterStage, Decide(stage: 1, firstAct: false), "act1+ keeps the existing fallback");
		Equal(GateStep.LaterStage, Decide(stage: 4, firstAct: false, insideEntry: true), "act1+ fallback is unchanged even inside LoadRun");
		Equal(GateStep.None, Decide(stage: 4, firstAct: false, eventOrMap: true), "act1+ still skips event/map rooms");
	}

	/// <summary>
	/// 接线：进房关口走"先等进行中的选择、再补选"的入口；主触发只在选择期间禁用行进（禁用在选择前、恢复在选择后），
	/// 等待循环本身不碰行进开关。
	/// </summary>
	[HextechTest]
	private static void ActEntrySelectionWiringDisablesTravelOnlyAroundSelection()
	{
		static string[] Calls(Type type, string methodName)
		{
			MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				?? throw new MissingMethodException(type.Name, methodName);
			return PatchProcessor.GetOriginalInstructions(GetAsyncStateMachineMoveNext(method))
				.Select(static instruction => instruction.operand)
				.OfType<MethodInfo>()
				.Select(static call => call.Name)
				.ToArray();
		}

		string[] gate = Calls(typeof(HextechMayhemModifier), nameof(HextechMayhemModifier.BeforeRoomEntered));
		Expect(gate.Contains(nameof(HextechRunLifecycleHooks.ResolveFirstActSelectionBeforeRoomAsync)), "act0 gate resolves through the in-flight-aware entry");

		string[] gateBody = Calls(typeof(HextechRunLifecycleHooks), nameof(HextechRunLifecycleHooks.ResolveFirstActSelectionBeforeRoomAsync));
		int reveal = Array.IndexOf(gateBody, "RevealRoomTransitionForSelectionAsync");
		int select = Array.IndexOf(gateBody, nameof(HextechRuneSelectionCoordinator.HandleStageSelectionAfterInFlightAsync));
		int cover = Array.IndexOf(gateBody, "CoverRoomTransitionAfterSelectionAsync");
		Expect(reveal >= 0 && select > reveal && cover > select, "gate reveals the room transition, selects, then restores the transition");

		string[] watch = Calls(typeof(HextechRunLifecycleHooks), "WatchPostOpeningEventSelection");
		Expect(!watch.Contains("SetTravelEnabled"), "the waiting loop never toggles map travel");

		string[] afterEvent = Calls(typeof(HextechRunLifecycleHooks), "SelectAfterOpeningEvent");
		int disable = Array.IndexOf(afterEvent, "SetTravelEnabled");
		int run = Array.IndexOf(afterEvent, nameof(HextechRuneSelectionCoordinator.HandleStageSelectionAfterInFlightAsync));
		int enable = Array.LastIndexOf(afterEvent, "SetTravelEnabled");
		Expect(disable >= 0 && run > disable && enable > run, "travel is disabled right before the selection and re-enabled after it");
	}

	/// <summary>同一局同一时刻只有一个选择；进房关口能拿到进行中选择的完成信号，结束（含中止）后唤醒等待者并释放名额。</summary>
	[HextechTest]
	private static void ActEntrySelectionInFlightTrackingDeduplicatesTriggers()
	{
		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		RunState otherRun = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		Expect(HextechRuneSelectionCoordinator.GetInFlightStageSelection(run) == null, "no selection is in flight initially");
		Expect(HextechRuneSelectionCoordinator.TryBeginStageSelection(run), "first trigger takes the selection slot");
		try
		{
			Expect(!HextechRuneSelectionCoordinator.TryBeginStageSelection(run), "a second trigger for the same run does not start a parallel selection");
			Expect(!HextechRuneSelectionCoordinator.TryBeginStageSelection(otherRun), "another run cannot take the slot while it is held");
			Task? inFlight = HextechRuneSelectionCoordinator.GetInFlightStageSelection(run);
			Expect(inFlight is { IsCompleted: false }, "the gate can await the in-flight selection");
			Expect(HextechRuneSelectionCoordinator.GetInFlightStageSelection(otherRun) == null, "in-flight signal is scoped to its run");
			HextechRuneSelectionCoordinator.EndStageSelection(otherRun);
			Expect(!inFlight!.IsCompleted, "ending a different run does not release the slot");
			HextechRuneSelectionCoordinator.EndStageSelection(run);
			Expect(inFlight.IsCompleted, "ending the selection wakes waiters");
			Expect(HextechRuneSelectionCoordinator.GetInFlightStageSelection(run) == null, "slot is free after the selection ends");
			Expect(HextechRuneSelectionCoordinator.TryBeginStageSelection(otherRun), "slot can be taken again");
			HextechRuneSelectionCoordinator.EndStageSelection(otherRun);
		}
		finally
		{
			HextechRuneSelectionCoordinator.EndStageSelection(run);
			HextechRuneSelectionCoordinator.EndStageSelection(otherRun);
		}
	}
}
