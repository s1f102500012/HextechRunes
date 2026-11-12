using System.Text;

namespace HextechRunes;

/// <summary>
/// 属性式补丁的统一应用入口:逐类应用、逐条汇报、失败按功能归因,并在启动时列出与其他模组共享的补丁点。
/// </summary>
/// <remarks>
/// 需要先后关系的补丁必须显式声明优先级或 before/after 约束，不能依赖类型枚举或声明顺序。
/// </remarks>
internal static class HextechPatcher
{
	private const string DumpEnvVar = "HEXTECH_DUMP_PATCHES";

	/// <summary>补丁类的应用结果。</summary>
	internal enum PatchOutcome
	{
		/// <summary>至少打上了一个目标方法，或动态 <c>Apply(Harmony)</c> 正常返回。</summary>
		Applied,

		/// <summary>主动跳过：带 <c>[HarmonyPrepare]</c> 且一个方法都没打上，或 <c>Optional</c> 且没有目标。不算失败，不标记符文。</summary>
		Skipped,

		/// <summary>安装异常、声明了元数据却没有目标，或非可选、无 Prepare 的类处理器一个方法都没打上。</summary>
		Failed
	}

	internal sealed record PatchResult(string Id, string Feature, Type PatchType, PatchOutcome Outcome, string? Error);

	private static readonly List<PatchResult> Results = [];

	/// <summary>
	/// 应用 <paramref name="assembly"/> 中所有补丁类:带 <c>[HarmonyPatch]</c> 的走 Harmony 类处理器;
	/// 只带 <c>[HextechPatch]</c> 且声明 <c>static void Apply(Harmony)</c> 的是"动态目标"补丁
	/// (目标需在运行时解析)，由该方法自行逐个 Patch。
	/// </summary>
	internal static void ApplyAll(Harmony harmony, Assembly assembly)
	{
		foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
		{
			HextechPatchAttribute? meta = type.GetCustomAttribute<HextechPatchAttribute>();
			bool hasHarmonyAttributes = HarmonyMethodExtensions.GetFromType(type).Any();
			if (hasHarmonyAttributes && meta == null)
			{
				// 缺元数据的补丁照常应用，但失败无法归因到功能/符文，清单与冲突报告也认不出它：显形防回归。
				HextechLog.Warn("Patch", $"[HarmonyPatch] class without [HextechPatch] metadata: {type.FullName}");
			}

			MethodInfo? dynamicApply = hasHarmonyAttributes || meta == null
				? null
				: type.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Harmony)]);
			if (!hasHarmonyAttributes && dynamicApply == null)
			{
				if (meta != null)
				{
					// 声明了元数据却没有任何目标:属性挂错了类。这类错误静默跳过等于补丁凭空消失,必须显形。
					Results.Add(new PatchResult(meta.Id, meta.Feature, type, PatchOutcome.Failed, Error: "no [HarmonyPatch] target and no Apply(Harmony)"));
					HextechLog.Warn("Patch", $"Patch declared but has no target: {meta.Id} ({meta.Feature}) on {type.FullName}");
				}

				continue;
			}

			string id = meta?.Id ?? type.FullName ?? type.Name;
			string feature = meta?.Feature ?? "unspecified";
			try
			{
				PatchOutcome outcome = PatchOutcome.Applied;
				if (dynamicApply != null)
				{
					dynamicApply.Invoke(null, [harmony]);
				}
				else
				{
					List<MethodInfo>? patched = harmony.CreateClassProcessor(type).Patch();
					bool gated = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
						.Any(static method => method.GetCustomAttribute<HarmonyPrepare>() != null);
					outcome = ClassifyClassProcessorResult(patched?.Count ?? 0, gated, meta?.Optional == true);
					if (outcome == PatchOutcome.Failed)
					{
						throw new InvalidOperationException("class processor patched no methods");
					}
				}

				Results.Add(new PatchResult(id, feature, type, outcome, Error: null));
			}
			catch (Exception ex)
			{
				Exception root = ex switch
				{
					HarmonyException { InnerException: not null } harmonyException => harmonyException.InnerException!,
					TargetInvocationException { InnerException: not null } invocation => invocation.InnerException!,
					_ => ex
				};
				Results.Add(new PatchResult(id, feature, type, PatchOutcome.Failed, Error: $"{root.GetType().Name}: {root.Message}"));
				ReportFailure(meta, id, feature, root);
			}
		}
	}

	/// <summary>
	/// 类处理器的结果分类：打上任何方法即已应用；一个都没打上时，带 <c>[HarmonyPrepare]</c>（Prepare 返回 false）
	/// 或声明 <c>Optional</c> 记为主动跳过，否则为失败。动态 <c>Apply(Harmony)</c> 不经过这里，正常返回即已应用。
	/// </summary>
	internal static PatchOutcome ClassifyClassProcessorResult(int patchedMethodCount, bool hasPrepare, bool optional)
	{
		if (patchedMethodCount > 0)
		{
			return PatchOutcome.Applied;
		}

		return hasPrepare || optional ? PatchOutcome.Skipped : PatchOutcome.Failed;
	}

	/// <summary>
	/// 关联符文的补丁失败时把符文标为本运行时不可用；关联敌方海克斯时逐个 Warn 点名（不标记、不过滤敌方池）。
	/// 未关联符文时，可选目标缺失记 Info，其余记 Warn。
	/// 动态 <c>Apply(Harmony)</c> 与属性式补丁走同一条归因路径，所以动态安装失败必须抛异常而不是自行吞掉。
	/// </summary>
	private static void ReportFailure(HextechPatchAttribute? meta, string id, string feature, Exception root)
	{
		foreach (MonsterHexKind monsterHex in meta?.MonsterHexes ?? [])
		{
			HextechLog.Warn("Patch", $"Patch failed: {id} ({feature}): enemy hex {monsterHex} effect is inactive on this client; in multiplayer its results will differ from other clients. {root.GetType().Name}: {root.Message}");
		}

		Type[] runes = meta?.AffectedRunes.ToArray() ?? [];
		foreach (Type rune in runes)
		{
			HextechRuntimeRuneCompatibility.MarkPlayerRuneHookFailed(rune, id, root);
		}

		if (runes.Length > 0 || meta?.MonsterHexes is { Length: > 0 })
		{
			return;
		}

		if (meta?.Optional == true)
		{
			HextechLog.Info("Patch", $"Optional patch skipped: {id} ({feature}): {root.GetType().Name}: {root.Message}");
			return;
		}

		HextechLog.Warn("Patch", $"Patch failed: {id} ({feature}): {root.GetType().Name}: {root.Message}");
	}

	/// <summary>返回 bool 的前缀可以跳过原方法（以及优先级更低的前缀）。</summary>
	internal static bool IsSkipCapable(Patch patch)
	{
		return patch.PatchMethod.ReturnType == typeof(bool);
	}

	/// <summary>启动汇总:应用/跳过/失败计数与跳过清单。失败项已由 <see cref="ReportFailure"/> 逐条记录。</summary>
	internal static void LogSummary()
	{
		foreach (string line in BuildSummaryLines(Results))
		{
			HextechLog.Info("Patch", line);
		}

		IReadOnlyList<string> missingMembers = HextechHookReflection.MissingMembers;
		if (missingMembers.Count > 0)
		{
			HextechLog.Warn("Patch", $"{missingMembers.Count} vanilla private member(s) missing in this game build (dependent features degraded):\n  {string.Join("\n  ", missingMembers)}");
		}
	}

	/// <summary>
	/// 摘要文本（纯函数，不写日志）：首行保持以 <c>Applied </c> 开头，供按关键字看日志；有跳过项时第二行列出跳过的补丁 ID。
	/// </summary>
	internal static IReadOnlyList<string> BuildSummaryLines(IReadOnlyCollection<PatchResult> results)
	{
		int applied = results.Count(static result => result.Outcome == PatchOutcome.Applied);
		int failed = results.Count(static result => result.Outcome == PatchOutcome.Failed);
		string[] skipped = results
			.Where(static result => result.Outcome == PatchOutcome.Skipped)
			.Select(static result => result.Id)
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToArray();
		List<string> lines = [$"Applied {applied}/{results.Count} patch classes, skipped {skipped.Length}, failed {failed}."];
		if (skipped.Length > 0)
		{
			lines.Add($"Skipped patch classes (Prepare returned false or optional target absent): {string.Join(", ", skipped)}");
		}

		return lines;
	}

	/// <summary>
	/// 列出本模组补丁与其他 owner 共享的目标方法。这是排查"装了别的模组后行为变样"的第一手线索。
	/// </summary>
	internal static void LogSharedPatchTargets(Harmony harmony)
	{
		try
		{
			List<string> lines = [];
			List<string> shadowed = [];
			foreach (MethodBase method in Harmony.GetAllPatchedMethods())
			{
				Patches? info = Harmony.GetPatchInfo(method);
				if (info == null)
				{
					continue;
				}

				List<Patch> all = [.. info.Prefixes, .. info.Postfixes, .. info.Transpilers, .. info.Finalizers];
				if (!all.Any(patch => patch.owner == harmony.Id))
				{
					continue;
				}

				string[] others = all
					.Where(patch => patch.owner != harmony.Id)
					.Select(patch => patch.owner)
					.Distinct(StringComparer.Ordinal)
					.OrderBy(owner => owner, StringComparer.Ordinal)
					.ToArray();
				if (others.Length == 0)
				{
					continue;
				}

				string target = $"{method.DeclaringType?.FullName}.{method.Name}";
				lines.Add($"{target} <- {string.Join(", ", others)}");

				// bool 前缀可能跳过原方法并影响后续前缀；这里只按优先级与安装序报告潜在冲突，不代表必然跳过。
				Patch[] ourSkippingPrefixes = info.Prefixes
					.Where(patch => patch.owner == harmony.Id && IsSkipCapable(patch))
					.ToArray();
				foreach (Patch other in info.Prefixes.Where(patch => patch.owner != harmony.Id))
				{
					Patch? shadowing = ourSkippingPrefixes.FirstOrDefault(ours => ours.priority > other.priority
						|| (ours.priority == other.priority && ours.index < other.index));
					if (shadowing != null)
					{
						shadowed.Add($"{target}: {other.owner}.{other.PatchMethod.Name} (priority {other.priority}) runs after our skipping prefix {shadowing.PatchMethod.DeclaringType?.Name}.{shadowing.PatchMethod.Name} (priority {shadowing.priority}) and may be skipped");
					}
				}
			}

			if (lines.Count == 0)
			{
				HextechLog.Info("Patch", $"No patch targets are shared with other mods.");
				return;
			}

			lines.Sort(StringComparer.Ordinal);
			HextechLog.Info("Patch", $"{lines.Count} patch target(s) shared with other mods:\n  {string.Join("\n  ", lines)}");
			if (shadowed.Count > 0)
			{
				shadowed.Sort(StringComparer.Ordinal);
				HextechLog.Warn("Patch", $"{shadowed.Count} third-party prefix(es) may be skipped by this mod's prefixes:\n  {string.Join("\n  ", shadowed)}");
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Patch", $"Shared patch target scan failed: {ex.GetType().Name}: {ex.Message}");
		}
	}

	/// <summary>
	/// 环境变量 <c>HEXTECH_DUMP_PATCHES=&lt;path&gt;</c> 存在时,把本模组全部补丁按目标方法排序写成文本。
	/// 用于重构前后比对:目标集合、补丁种类、优先级与同目标内的执行序都必须一致。
	/// </summary>
	internal static void DumpIfRequested(Harmony harmony)
	{
		string? path = Environment.GetEnvironmentVariable(DumpEnvVar);
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			File.WriteAllText(path, BuildPatchTable(harmony.Id), Encoding.UTF8);
			HextechLog.Info("Patch", $"Patch table written to {path}.");
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Patch", $"Patch table dump failed: {ex.GetType().Name}: {ex.Message}");
		}
	}

	internal static string BuildPatchTable(string ownerId)
	{
		StringBuilder builder = new();
		IEnumerable<MethodBase> methods = Harmony.GetAllPatchedMethods()
			.OrderBy(method => $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})", StringComparer.Ordinal);
		foreach (MethodBase method in methods)
		{
			Patches? info = Harmony.GetPatchInfo(method);
			if (info == null)
			{
				continue;
			}

			List<string> lines = [];
			AppendKind(lines, "prefix", info.Prefixes, ownerId);
			AppendKind(lines, "postfix", info.Postfixes, ownerId);
			AppendKind(lines, "transpiler", info.Transpilers, ownerId);
			AppendKind(lines, "finalizer", info.Finalizers, ownerId);
			if (lines.Count == 0)
			{
				continue;
			}

			builder.Append(method.DeclaringType?.FullName).Append('.').Append(method.Name)
				.Append('(').Append(string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))).Append(')')
				.Append(" il=").Append(HextechVanillaCopyGuard.ComputeIlHash(method) ?? "<none>")
				.Append(" key=").Append(HextechVanillaCopyGuard.DescribeTarget(method))
				.Append('\n');
			foreach (string line in lines)
			{
				builder.Append("  ").Append(line).Append('\n');
			}
		}

		return builder.ToString();
	}

	private static void AppendKind(List<string> lines, string kind, IReadOnlyCollection<Patch> patches, string ownerId)
	{
		// 导出按优先级、安装序排列并附带 before/after；这是比对视图，不模拟跨 owner 的完整执行序。
		foreach (Patch patch in patches.Where(patch => patch.owner == ownerId).OrderByDescending(patch => patch.priority).ThenBy(patch => patch.index))
		{
			string extras = string.Empty;
			if (patch.before.Length > 0)
			{
				extras += $" before={string.Join("|", patch.before)}";
			}

			if (patch.after.Length > 0)
			{
				extras += $" after={string.Join("|", patch.after)}";
			}

			if (kind == "prefix" && IsSkipCapable(patch))
			{
				extras += " skip=true";
			}

			lines.Add($"{kind} priority={patch.priority} {patch.PatchMethod.DeclaringType?.Name}.{patch.PatchMethod.Name}{extras}");
		}
	}
}
