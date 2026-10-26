using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes;

internal static class ArchaicToothEternalHooks
{
	private static readonly AsyncLocal<ConversionScope?> Current = new();

	private sealed class ConversionScope(ArchaicTooth tooth, bool enabled, ConversionScope? previous)
	{
		internal ArchaicTooth Tooth { get; } = tooth;
		internal ConversionScope? Previous { get; } = previous;
		internal bool Active { get; set; } = enabled;
		internal CardModel? Source { get; set; }
	}

	private static async Task CompleteConversion(Task task, ConversionScope scope)
	{
		try
		{
			await task;
		}
		finally
		{
			scope.Active = false;
		}
	}

	[HarmonyPatch(typeof(ArchaicTooth), nameof(ArchaicTooth.AfterObtained))]
	[HextechPatch("relic.archaic-tooth-eternal.scope", "古老牙齿转换永恒牌")]
	private static class ObtainPatch
	{
		[HarmonyPrefix]
		private static void Prefix(ArchaicTooth __instance, out ConversionScope __state)
		{
			__state = new ConversionScope(__instance,
				HextechMayhemModifier.IsEnabledForRun(__instance.Owner.RunState)
					&& !HextechGodslayerTrial.IsTrialRun(__instance.Owner.RunState), Current.Value);
			Current.Value = __state;
		}

		[HarmonyPostfix]
		private static void Postfix(ref Task __result, ConversionScope __state)
		{
			__result = CompleteConversion(__result, __state);
		}

		[HarmonyFinalizer]
		private static void Finalizer(Exception? __exception, ConversionScope? __state)
		{
			if (__state == null)
			{
				return;
			}
			// 原版返回 Task 后立即还原调用者上下文；其异步续体保留自己的作用域，完成或失败后失效。
			Current.Value = __state.Previous;
			if (__exception != null)
			{
				__state.Active = false;
			}
		}
	}

	[HarmonyPatch(typeof(ArchaicTooth), "GetTranscendenceTransformedCard", [typeof(CardModel)])]
	[HextechPatch("relic.archaic-tooth-eternal.replacement", "古老牙齿继承永恒")]
	private static class ReplacementPatch
	{
		[HarmonyPostfix]
		[HarmonyPriority(Priority.Last)]
		private static void Postfix(ArchaicTooth __instance, CardModel starterCard, CardModel __result)
		{
			if (!starterCard.Keywords.Contains(CardKeyword.Eternal)
				|| !HextechMayhemModifier.IsEnabledForRun(starterCard.Owner.RunState)
				|| HextechGodslayerTrial.IsTrialRun(starterCard.Owner.RunState))
			{
				return;
			}

			// 原版已复制升级和附魔；单独添加的永恒也要继承，不从原牌移除关键词来绕过限制。
			if (!__result.Keywords.Contains(CardKeyword.Eternal))
			{
				__result.AddKeyword(CardKeyword.Eternal);
			}
			if (Current.Value is { Active: true } scope && ReferenceEquals(scope.Tooth, __instance))
			{
				scope.Source = starterCard;
			}
		}
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsTransformable), MethodType.Getter)]
	[HextechPatch("relic.archaic-tooth-eternal.transformable", "古老牙齿定向转换放行")]
	private static class TransformablePatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, ref bool __result)
		{
			if (!__result && Current.Value is { Active: true } scope
				&& ReferenceEquals(scope.Source, __instance)
				&& __instance.Keywords.Contains(CardKeyword.Eternal))
			{
				__result = true;
			}
		}
	}
}
