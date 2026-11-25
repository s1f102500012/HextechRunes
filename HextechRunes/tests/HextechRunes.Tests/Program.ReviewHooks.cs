using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunes.Tests;

// Hooks/Patching 审查整理的回归测试与测试专用补丁工具（原先挂在生产代码 HextechPatcher 上）。
internal static partial class Program
{
	private const BindingFlags NestedPatchFlags = BindingFlags.Public | BindingFlags.NonPublic;

	/// <summary>只应用 <paramref name="outerType"/> 里声明的嵌套补丁类；给了名字就只应用点名的那几个。</summary>
	private static void ApplyNestedPatches(Harmony harmony, Type outerType, params string[] nestedNames)
	{
		foreach (Type nested in outerType.GetNestedTypes(NestedPatchFlags))
		{
			if (nestedNames.Length > 0 && !nestedNames.Contains(nested.Name, StringComparer.Ordinal))
			{
				continue;
			}

			if (HarmonyMethodExtensions.GetFromType(nested).Any())
			{
				harmony.CreateClassProcessor(nested).Patch();
			}
		}
	}

	/// <summary>按外层类型 + 嵌套补丁类名定位补丁方法。</summary>
	private static MethodInfo? FindPatchMethod(Type outerType, string nestedName, string methodName)
	{
		return outerType.GetNestedType(nestedName, NestedPatchFlags)
			?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
	}

	/// <summary>
	/// 解析嵌套补丁类 <c>[HarmonyPatch]</c> 属性声明的真实目标（不执行 Prepare/TargetMethod，测试进程里它们可能触碰 Godot）。
	/// </summary>
	private static MethodBase ResolveDeclaredPatchTarget(Type outerType, string nestedName)
	{
		Type patchType = outerType.GetNestedType(nestedName, NestedPatchFlags)
			?? throw new MissingMemberException(outerType.FullName, nestedName);
		HarmonyMethod merged = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patchType));
		MethodBase? target = merged.methodType switch
		{
			MethodType.Getter => AccessTools.PropertyGetter(merged.declaringType, merged.methodName),
			MethodType.Setter => AccessTools.PropertySetter(merged.declaringType, merged.methodName),
			_ => AccessTools.Method(merged.declaringType, merged.methodName, merged.argumentTypes)
		};
		return target ?? throw new MissingMethodException(merged.declaringType?.FullName, merged.methodName);
	}

	private static class FinalizerStateProbe
	{
		internal static bool? LastFinalizerState;

		[MethodImpl(MethodImplOptions.NoInlining)]
		internal static int Run(bool fail)
		{
			if (fail)
			{
				throw new InvalidOperationException("probe failure");
			}

			return 1;
		}

		internal static void Prefix(out bool __state)
		{
			__state = true;
		}

		internal static void Postfix(ref bool __state)
		{
			__state = false;
		}

		internal static Exception? Finalizer(bool __state, Exception? __exception)
		{
			LastFinalizerState = __state;
			return null;
		}
	}

	/// <summary>
	/// AsyncLocal 作用域补丁依赖 Harmony 把 Postfix 里对 ref __state 的清零传给 Finalizer：
	/// 正常路径 Finalizer 看到已清零、不重复出栈；原方法同步抛异常时 Postfix 不执行，Finalizer 看到入栈标记。
	/// </summary>
	[HextechTest]
	private static void HarmonyPassesPostfixStateByReferenceToFinalizer()
	{
		Harmony harmony = new("Natsuki.HextechRunes.Tests.FinalizerStateProbe");
		MethodInfo target = AccessTools.Method(typeof(FinalizerStateProbe), nameof(FinalizerStateProbe.Run));
		try
		{
			harmony.Patch(
				target,
				prefix: new HarmonyMethod(typeof(FinalizerStateProbe), nameof(FinalizerStateProbe.Prefix)),
				postfix: new HarmonyMethod(typeof(FinalizerStateProbe), nameof(FinalizerStateProbe.Postfix)),
				finalizer: new HarmonyMethod(typeof(FinalizerStateProbe), nameof(FinalizerStateProbe.Finalizer)));

			FinalizerStateProbe.Run(fail: false);
			Equal<bool?>(false, FinalizerStateProbe.LastFinalizerState, "finalizer sees the state cleared by the postfix");
			FinalizerStateProbe.Run(fail: true);
			Equal<bool?>(true, FinalizerStateProbe.LastFinalizerState, "finalizer sees the prefix state when the original throws");
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}
	}

	private static int ReadDoubleVisionSuppressionDepth()
	{
		AsyncLocal<int> depth = (AsyncLocal<int>)(typeof(DoubleVisionRune)
			.GetField("CommandDuplicationSuppressionDepth", BindingFlags.NonPublic | BindingFlags.Static)
			?.GetValue(null)
			?? throw new MissingFieldException(nameof(DoubleVisionRune), "CommandDuplicationSuppressionDepth"));
		return depth.Value;
	}

	// 原版战斗状态通知启动的延迟任务（随机数预测等模组在它的订阅里模拟出牌和伤害）不能继承伤害命令作用域，
	// 否则代偿、滑溜等只在真实伤害命令里记账的查询钩子会把模拟记成真实结算；通知返回后调用方的作用域原样恢复。
	[HextechTest]
	private static void CombatStateNotifyDoesNotCarryDamageCommandScope()
	{
		MethodInfo damagePrefix = FindPatchMethod(typeof(HextechCombatHooks), "DamageCommandPatch", "Prefix")
			?? throw new MissingMethodException("DamageCommandPatch.Prefix");
		MethodInfo damageFinalizer = FindPatchMethod(typeof(HextechCombatHooks), "DamageCommandPatch", "Finalizer")
			?? throw new MissingMethodException("DamageCommandPatch.Finalizer");
		MethodInfo notifyPrefix = FindPatchMethod(typeof(HextechCombatHooks), "CombatStateNotifyScopePatch", "Prefix")
			?? throw new MissingMethodException("CombatStateNotifyScopePatch.Prefix");
		MethodInfo notifyPostfix = FindPatchMethod(typeof(HextechCombatHooks), "CombatStateNotifyScopePatch", "Postfix")
			?? throw new MissingMethodException("CombatStateNotifyScopePatch.Postfix");
		MethodInfo notifyFinalizer = FindPatchMethod(typeof(HextechCombatHooks), "CombatStateNotifyScopePatch", "Finalizer")
			?? throw new MissingMethodException("CombatStateNotifyScopePatch.Finalizer");

		static async Task<long> ReadCommandIdLater()
		{
			await Task.Yield();
			return HextechCombatHooks.CurrentActualDamageCommandId;
		}

		object?[] damageState = [0L];
		damagePrefix.Invoke(null, damageState);
		long commandId = HextechCombatHooks.CurrentActualDamageCommandId;
		Expect(commandId != 0L, "inside a damage command");

		object?[] notifyState = [null];
		notifyPrefix.Invoke(null, notifyState);
		Task<long> deferred = ReadCommandIdLater();
		notifyPostfix.Invoke(null, notifyState);
		Equal(commandId, HextechCombatHooks.CurrentActualDamageCommandId, "the damage command keeps its scope after the notification");
		Equal(0L, deferred.GetAwaiter().GetResult(), "the deferred notification task does not see the damage command");

		InvalidOperationException failure = new("synchronous failure inside the notification");
		notifyPrefix.Invoke(null, notifyState);
		Equal(0L, HextechCombatHooks.CurrentActualDamageCommandId, "the notification runs outside the damage command");
		notifyFinalizer.Invoke(null, [notifyState[0], failure]);
		Equal(commandId, HextechCombatHooks.CurrentActualDamageCommandId, "the finalizer restores the scope after a synchronous failure");

		damageFinalizer.Invoke(null, [damageState[0], failure]);
		Equal(0L, HextechCombatHooks.CurrentActualDamageCommandId, "clean caller context after the damage command");
	}

	[HextechTest]
	private static void ScopedPatchFinalizersRestoreCallerContextAfterSynchronousFailure()
	{
		InvalidOperationException failure = new("synchronous failure inside the patched command");

		// 复视奖励命令抑制：原命令同步抛异常后，调用方不能带着抑制深度继续结算后续奖励。
		MethodInfo rewardPrefix = FindPatchMethod(typeof(HextechRewardSafetyHooks), "RewardSelectUnsynchronizedPatch", "Prefix")
			?? throw new MissingMethodException("RewardSelectUnsynchronizedPatch.Prefix");
		MethodInfo rewardPostfix = FindPatchMethod(typeof(HextechRewardSafetyHooks), "RewardSelectUnsynchronizedPatch", "Postfix")
			?? throw new MissingMethodException("RewardSelectUnsynchronizedPatch.Postfix");
		MethodInfo rewardFinalizer = FindPatchMethod(typeof(HextechRewardSafetyHooks), "RewardSelectUnsynchronizedPatch", "Finalizer")
			?? throw new MissingMethodException("RewardSelectUnsynchronizedPatch.Finalizer");
		Equal(0, ReadDoubleVisionSuppressionDepth(), "clean caller context before the reward command");
		object?[] rewardState = [null];
		rewardPrefix.Invoke(null, rewardState);
		Equal(1, ReadDoubleVisionSuppressionDepth(), "prefix enters reward command suppression");
		object? rethrown = rewardFinalizer.Invoke(null, [rewardState[0], failure]);
		Expect(ReferenceEquals(failure, rethrown), "finalizer must rethrow the original exception");
		Equal(0, ReadDoubleVisionSuppressionDepth(), "finalizer restores the caller context after a synchronous failure");

		rewardPrefix.Invoke(null, rewardState);
		rewardPostfix.Invoke(null, rewardState);
		Equal(0, ReadDoubleVisionSuppressionDepth(), "postfix restores the caller context on the normal path");
		Equal<object?>(null, rewardState[0], "postfix clears the state it already handled");
		rewardFinalizer.Invoke(null, [rewardState[0], failure]);
		Equal(0, ReadDoubleVisionSuppressionDepth(), "a later failure must not restore twice");

		// 疫情响应守卫：深度守卫不是幂等的，重复出栈会弹掉外层作用域。
		MethodInfo outbreakPrefix = FindPatchMethod(typeof(HextechCombatHooks), "OutbreakPatch", "Prefix")
			?? throw new MissingMethodException("OutbreakPatch.Prefix");
		MethodInfo outbreakPostfix = FindPatchMethod(typeof(HextechCombatHooks), "OutbreakPatch", "Postfix")
			?? throw new MissingMethodException("OutbreakPatch.Postfix");
		MethodInfo outbreakFinalizer = FindPatchMethod(typeof(HextechCombatHooks), "OutbreakPatch", "Finalizer")
			?? throw new MissingMethodException("OutbreakPatch.Finalizer");
		Expect(!HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse, "clean caller context before Outbreak");
		object?[] outbreakState = [null];
		outbreakPrefix.Invoke(null, outbreakState);
		Expect(HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse, "Outbreak prefix enters the response guard");
		outbreakFinalizer.Invoke(null, [outbreakState[0], failure]);
		Expect(!HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse, "Outbreak finalizer exits the guard after a synchronous failure");

		// 外层再套一次疫情响应作为外围作用域，内层在 Postfix 之后失败不能把它弹掉。
		object?[] enclosing = [null];
		outbreakPrefix.Invoke(null, enclosing);
		object?[] nested = [null];
		outbreakPrefix.Invoke(null, nested);
		object?[] postfixArgs = [nested[0], Task.CompletedTask];
		outbreakPostfix.Invoke(null, postfixArgs);
		outbreakFinalizer.Invoke(null, [postfixArgs[0], failure]);
		Expect(HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse, "a failure after the postfix must not pop the enclosing guard");
		outbreakFinalizer.Invoke(null, [enclosing[0], failure]);
		Expect(!HextechCombatHooks.IsResolvingOutbreakPowerPoisonResponse, "caller context is clean after the enclosing guard");
	}

	/// <summary>
	/// 关键词持久化标记的存档格式与合并追踪器之前逐字一致：Props.ints 里按
	/// 思维覆写 → 谢幕 → 扮演 → 腐化枝 → 不死 的顺序追加值为 1 的标记、已有同名项不重复写；
	/// 读档只认非 0 标记。旧存档（手写的旧格式）读回后关键词与追踪都恢复，再存一次得到同样的条目。
	/// </summary>
	[HextechTest]
	private static void KeywordPersistenceMarkersKeepLegacySaveFormat()
	{
		StrikeIronclad card = CreateMutableTestModel<StrikeIronclad>();
		KeywordPersistenceTrackers.ThoughtOverwrite.Track(card);
		KeywordPersistenceTrackers.CorruptedBranchInnate.Track(card);
		KeywordPersistenceTrackers.UndyingEthereal.Track(card);

		SerializableCard saved = new();
		saved.Props = new SavedProperties
		{
			ints = [new SavedProperties.SavedProperty<int>("ForeignMarker", 7), new SavedProperties.SavedProperty<int>(CorruptedBranchRune.InnateMarkerSavedPropertyName, 1)]
		};
		HextechThoughtOverwriteKeywordPersistenceHooks.WriteMarkers(card, saved);
		(string Name, int Value)[] expected =
		[
			("ForeignMarker", 7),
			(CorruptedBranchRune.InnateMarkerSavedPropertyName, 1),
			(ThoughtOverwriteRune.EtherealMarkerSavedPropertyName, 1),
			(UndyingUpgradeRune.EtherealMarkerSavedPropertyName, 1)
		];
		SequenceEqual(expected, saved.Props!.ints!.Select(static property => (property.name, property.value)), "keyword markers keep the legacy order, value and no-duplicate rule");
		Equal("SavedThoughtOverwriteEtherealMarker", ThoughtOverwriteRune.EtherealMarkerSavedPropertyName, "thought overwrite marker key");
		Equal("SavedCurtainCallRetainMarker", CurtainCallRune.RetainMarkerSavedPropertyName, "curtain call marker key");
		Equal("SavedCosplayInnateMarker", HextechRunesApi.PersistentInnateMarkerSavedPropertyName, "cosplay marker key");
		Equal("SavedCorruptedBranchInnateMarker", CorruptedBranchRune.InnateMarkerSavedPropertyName, "corrupted branch marker key");
		Equal("SavedUndyingUpgradeEtherealMarker", UndyingUpgradeRune.EtherealMarkerSavedPropertyName, "undying marker key");

		// 旧格式存档：谢幕标记为 0（不恢复），扮演标记为 1（恢复）。
		SerializableCard legacy = new()
		{
			Props = new SavedProperties
			{
				ints =
				[
					new SavedProperties.SavedProperty<int>(CurtainCallRune.RetainMarkerSavedPropertyName, 0),
					new SavedProperties.SavedProperty<int>(HextechRunesApi.PersistentInnateMarkerSavedPropertyName, 1)
				]
			}
		};
		StrikeIronclad loaded = CreateMutableTestModel<StrikeIronclad>();
		HextechThoughtOverwriteKeywordPersistenceHooks.RestoreFromMarkers(legacy, loaded);
		Expect(loaded.Keywords.Contains(CardKeyword.Innate) && KeywordPersistenceTrackers.CosplayInnate.IsTracked(loaded), "legacy non-zero marker restores keyword and tracking");
		Expect(!loaded.Keywords.Contains(CardKeyword.Retain) && !KeywordPersistenceTrackers.CurtainCall.IsTracked(loaded), "legacy zero marker stays inert");

		SerializableCard resaved = new();
		HextechThoughtOverwriteKeywordPersistenceHooks.WriteMarkers(loaded, resaved);
		SequenceEqual(
			new[] { (HextechRunesApi.PersistentInnateMarkerSavedPropertyName, 1) },
			resaved.Props!.ints!.Select(static property => (property.name, property.value)),
			"round trip writes back only the restored marker");
	}
}
