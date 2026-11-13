using System.Reflection;
using HextechRunes;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void NaturalRelicPoolPreservesVanillaRngAndForeignContent()
	{
		// 保留原版 Starter，确保过滤依据是内容归属而非稀有度。
		RelicModel[] vanilla = [new MegaCrit.Sts2.Core.Models.Relics.Anchor(), new Vajra(), new PenNib(), new CrackedCore()];
		RelicModel[] registered = [vanilla[0], new BlankCheckRune(), vanilla[1], new DoubleVisionRune(), vanilla[2], new VoltaicUpgradeRune(), vanilla[3]];
		RelicModel[] filtered = HextechNaturalRelicPoolHooks.FilterNaturalRelics(registered).ToArray();
		SequenceEqual(vanilla, filtered, "natural pool preserves other content and its order");
		Equal(7, registered.Length, "filter must not mutate model registration");

		// 原版共享池（RunManager 用 SharedRelicPool.GetUnlockedRelics 填共享 GrabBag）没有 Starter：
		// 未过滤时海克斯条目会新建一个 Starter 牌堆，多一次洗牌。0.111.0 及以前每次洗牌在外层 RNG 上走 n-1 步，
		// 0.113.0 起先派生子 RNG、外层只走 1 步，两种语义下都会让后续 UpFront 流偏移。
		RelicModel[] sharedVanilla = [vanilla[0], vanilla[1], vanilla[2]];
		RelicModel[] sharedRegistered = [vanilla[0], new BlankCheckRune(), vanilla[1], new DoubleVisionRune(), vanilla[2], new VoltaicUpgradeRune()];
		RelicModel[] sharedFiltered = HextechNaturalRelicPoolHooks.FilterNaturalRelics(sharedRegistered).ToArray();
		SequenceEqual(sharedVanilla, sharedFiltered, "shared-pool shaped input filters back to vanilla");

		foreach (uint seed in new uint[] {0, 1, 42, 123456789})
		{
			ExpectNaturalPoolMatchesVanilla(vanilla, filtered, registered, seed, expectCounterDrift: false, "with vanilla Starter");
			ExpectNaturalPoolMatchesVanilla(sharedVanilla, sharedFiltered, sharedRegistered, seed, expectCounterDrift: true, "shared pool");
		}
	}

	// 受保护的性质：过滤后的输入与纯原版输入填出的各稀有度牌堆顺序相同、外层 RNG 位置相同、后续随机流相同。
	// 未过滤输入必须能被同一套比较区分出来，证明比较本身有效；expectCounterDrift 时还要求外层 RNG 位置偏移。
	private static void ExpectNaturalPoolMatchesVanilla(
		RelicModel[] vanilla,
		RelicModel[] filtered,
		RelicModel[] unfiltered,
		uint seed,
		bool expectCounterDrift,
		string label)
	{
		Rng baseline = new(seed);
		Rng fixedRng = new(seed);
		Rng oldRng = new(seed);
		RelicGrabBag baselineBag = new();
		RelicGrabBag fixedBag = new();
		RelicGrabBag oldBag = new();
		baselineBag.Populate(vanilla, baseline);
		fixedBag.Populate(filtered, fixedRng);
		oldBag.Populate(unfiltered, oldRng);
		string baselineDeques = DescribeGrabBagDeques(baselineBag);
		Equal(baselineDeques, DescribeGrabBagDeques(fixedBag), $"{label}: natural pool order must match vanilla");
		Equal(GetGenerationRngCounter(baseline), GetGenerationRngCounter(fixedRng), $"{label}: pool RNG consumption must match vanilla");
		Expect(
			DescribeGrabBagDeques(oldBag) != baselineDeques || GetGenerationRngCounter(oldRng) != GetGenerationRngCounter(baseline),
			$"{label}: unfiltered Starter entries must be observable in the populated pool");
		if (expectCounterDrift)
		{
			Expect(GetGenerationRngCounter(oldRng) > GetGenerationRngCounter(baseline), $"{label}: unfiltered Starter entries reproduce the old RNG drift");
		}

		for (int i = 0; i < 32; i++)
		{
			Equal(baseline.NextInt(1000000), fixedRng.NextInt(1000000), $"{label}: later encounter/Boss random stream must remain identical");
		}
	}

	private static string DescribeGrabBagDeques(RelicGrabBag bag)
	{
		object? deques = typeof(RelicGrabBag).GetField("_deques", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(bag);
		Expect(deques is System.Collections.IDictionary, "RelicGrabBag._deques must stay readable for the natural pool test");
		List<string> parts = [];
		foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)deques)
		{
			IEnumerable<RelicModel> relics = (IEnumerable<RelicModel>)entry.Value!;
			parts.Add($"{entry.Key}:{string.Join(",", relics.Select(static relic => relic.GetType().Name))}");
		}

		return string.Join(";", parts);
	}

	private static ulong GetGenerationRngCounter(Rng rng)
	{
#if STS2_110_OR_NEWER
		return (ulong)rng.ToSerializable().counter;
#else
		return (ulong)rng.Counter;
#endif
	}

	[HextechTest]
	private static void RunActivationUsesSnapshotBeforeFreezeAndPreservesFrozenValue()
	{
		// ModifierModel 构造器接触 Godot 本地化；这里只检查托管的本局配置状态。
		HextechMayhemModifier modifier = (HextechMayhemModifier)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(HextechMayhemModifier));
		HextechMayhemRunContext context = new();
		typeof(HextechMayhemModifier).GetField("_runContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(modifier, context);
		context.RunConfigurationSnapshot = HextechRuneConfiguration.GetDefaultSnapshot() with {ModEnabled = false};
		Expect(!modifier.IsModActiveForRun, "disabled snapshot applies before the first act roll");
		context.ModActiveForRun = false;
		context.RunConfigurationSnapshot = context.RunConfigurationSnapshot with {ModEnabled = true};
		Expect(!modifier.IsModActiveForRun, "reenabling only affects the next run");
		context.ModActiveForRun = true;
		context.RunConfigurationSnapshot = context.RunConfigurationSnapshot with {ModEnabled = false};
		Expect(modifier.IsModActiveForRun, "disabling does not change an already active run");
	}

	[HextechTest]
	private static void DisabledStageBranchesBeforeEnemyGeneration()
	{
		MethodInfo method = typeof(HextechRuneSelectionCoordinator).GetMethod(nameof(HextechRuneSelectionCoordinator.HandleStageSelection))!;
		MethodInfo moveNext = GetAsyncStateMachineMoveNext(method);
		MethodInfo[] calls = HarmonyLib.PatchProcessor.GetOriginalInstructions(moveNext)
			.Select(static instruction => instruction.operand).OfType<MethodInfo>().ToArray();
		int freeze = Array.FindIndex(calls, static call => call.Name == "FreezeModActiveForRunAndCheckDisabled");
		int generate = Array.FindIndex(calls, static call => call.Name == "ResolveNewMonsterHexesForAct");
		Expect(freeze >= 0 && generate > freeze, "host-synchronized disable check must precede additional enemy rolls");
	}
}
