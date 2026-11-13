using System.Reflection;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// Harmony 按返回类型绑定 __result；类型不兼容时整个补丁类（含败北存档前补历史的前缀）都不会应用。
	[HextechTest]
	private static void RunEndPatchBindsOnEndedReturnType()
	{
		MethodInfo onEnded = typeof(RunManager).GetMethod(nameof(RunManager.OnEnded), [typeof(bool)])!;
		Type patch = typeof(HextechRunLifecycleHooks).GetNestedType("RunEndedPatch", BindingFlags.NonPublic)!;
		Expect(patch != null, "run.end patch class must exist");
		MethodInfo postfix = patch!.GetMethods(PatchMemberFlags).Single(static method => method.GetCustomAttribute<HarmonyPostfix>() != null);
		ParameterInfo result = postfix.GetParameters().Single(static parameter => parameter.Name == "__result");
		Equal(onEnded.ReturnType, result.ParameterType, "run.end postfix __result must match RunManager.OnEnded return type");
	}

	[HextechTest]
	private static void RunEndRepeatedCallSkipsTelemetryAndKeepsCleanupIdempotent()
	{
		SerializableRun run = new();
#if STS2_113_OR_NEWER
		Expect(HextechV113ApiCompat.GetEndedRun(default) == null, "repeated OnEnded (default result) carries no save");
		Expect(ReferenceEquals(run, HextechV113ApiCompat.GetEndedRun(MegaCrit.Sts2.Core.Entities.Save.PostRunProgressUpdateResult.Unsuccessful(run))), "ended run comes from result.run");
#else
		Expect(ReferenceEquals(run, HextechV113ApiCompat.GetEndedRun(run)), "older targets pass the save through");
#endif
		// 原版判定本局已结束过时只做清理；连续两次不能抛异常。
		HextechRunLifecycleHooks.HandleRunEnded(null, null, isVictory: false);
		HextechRunLifecycleHooks.HandleRunEnded(null, null, isVictory: false);
	}

	[HextechTest]
	private static void UnsupportedGameModesOnlyIncludeBlitz()
	{
		foreach (GameMode mode in Enum.GetValues<GameMode>())
		{
			bool unsupported = HextechV113ApiCompat.GetUnsupportedGameModeReason(mode) != null;
			Equal(mode.ToString() == "Blitz", unsupported, $"game mode {mode} support");
		}

		Expect(!HextechMayhemModifier.IsGameModeUnsupportedForRun(null), "missing run state is not treated as unsupported");

		HextechMayhemModifier modifier = (HextechMayhemModifier)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(HextechMayhemModifier));
		HextechMayhemRunContext context = new();
		typeof(HextechMayhemModifier).GetField("_runContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(modifier, context);
		context.RunConfigurationSnapshot = HextechRuneConfiguration.GetDefaultSnapshot() with {ModEnabled = true};
		context.ModActiveForRun = true;
		Expect(modifier.IsModActiveForRun, "supported mode keeps the frozen value");
		context.UnsupportedGameModeReason = "test";
		Expect(!modifier.IsModActiveForRun, "unsupported game mode disables the run regardless of configuration");
	}

	[HextechTest]
	private static void UnsupportedGameModeBranchesBeforeActRollSync()
	{
		MethodInfo method = typeof(HextechRuneSelectionCoordinator).GetMethod(nameof(HextechRuneSelectionCoordinator.HandleStageSelection))!;
		MethodInfo[] calls = PatchProcessor.GetOriginalInstructions(GetAsyncStateMachineMoveNext(method))
			.Select(static instruction => instruction.operand).OfType<MethodInfo>().ToArray();
		int gate = Array.FindIndex(calls, static call => call.Name == nameof(HextechMayhemModifier.IsGameModeUnsupportedForRun));
		int roll = Array.FindIndex(calls, static call => call.Name == "ResolveActRoll");
		Expect(gate >= 0 && roll > gate, "game mode gate must precede the act-roll synchronization");
	}
}
