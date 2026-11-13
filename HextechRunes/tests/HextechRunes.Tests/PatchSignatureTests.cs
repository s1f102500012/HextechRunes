using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using HextechRunes;

namespace HextechRunes.Tests;

// 补丁方法签名与目标的静态匹配。PatchDeclarationsResolveToRealTargets 只检查目标能否解析；
// 补丁方法的注入参数写错时（__result 声明成旧返回类型、按名注入的参数被原版改名等），
// Harmony 在游戏里拒绝整个补丁类，测试进程却照常通过。这里按 Harmony 2.x 的注入规则逐个参数核对，
// 规则从宽：引用类型允许窄化（补丁作者对实际传入类型负责），只报确定会被拒绝或类型不可能对上的写法。
internal static partial class Program
{
	private static readonly Regex HarmonyIndexedParameter = new(@"^__(\d+)$", RegexOptions.CultureInvariant);

	[HextechTest]
	private static void PatchMethodSignaturesMatchTargets()
	{
		List<string> problems = [];
		int checkedParameters = CollectPatchSignatureProblems(
			typeof(ModEntry).Assembly,
			static type => type.GetCustomAttribute<HextechPatchAttribute>()?.Id,
			problems);

		// 防止解析逻辑失效后整条测试空转通过：本体有上百个属性式补丁，参数总数远多于这个下限。
		Expect(checkedParameters > 200, $"only {checkedParameters} patch parameters were checked; target resolution is probably broken");
		Expect(problems.Count == 0, "patch signature problems:\n  " + string.Join("\n  ", problems));
	}

	// 逐个核对 assembly 里属性式补丁方法的注入参数，返回核对过的参数个数；问题追加到 problems，补丁 id 由 patchIdOf 给出。
	// 本体与拓展包共用这一套规则（拓展包的入口见 SponsorPatchMethodSignaturesMatchTargets）。
	private static int CollectPatchSignatureProblems(Assembly assembly, Func<Type, string?> patchIdOf, List<string> problems)
	{
		int checkedParameters = 0;
		foreach (Type type in assembly.GetTypes())
		{
			List<HarmonyMethod> harmonyAttributes = HarmonyMethodExtensions.GetFromType(type);
			if (harmonyAttributes.Count == 0)
			{
				continue;
			}

			MethodInfo[] methods = type.GetMethods(PatchMemberFlags);
			// 运行时枚举目标的类不在测试进程里执行（可能触碰 Godot 原生层），只能由 headless 加载兜底。
			if (methods.Any(static method => method.GetCustomAttribute<HarmonyTargetMethod>() != null
				|| method.GetCustomAttribute<HarmonyTargetMethods>() != null))
			{
				continue;
			}

			HarmonyMethod merged = HarmonyMethod.Merge(harmonyAttributes);
			// 解析不到的目标由 PatchDeclarationsResolveToRealTargets 报告，这里不重复。
			if (ResolveDeclaredPatchTarget(merged) is not MethodBase target)
			{
				continue;
			}

			string label = patchIdOf(type) ?? type.FullName ?? type.Name;
			Type? stateType = null;
			string? stateMethod = null;
			foreach (MethodInfo method in methods)
			{
				string? kind = DescribeHarmonyPatchKind(method);
				if (kind == null)
				{
					continue;
				}

				foreach (ParameterInfo parameter in method.GetParameters())
				{
					checkedParameters++;
					string where = $"{label}: {type.Name}.{method.Name} parameter '{parameter.Name}'";
					if (parameter.Name == "__state")
					{
						Type parameterStateType = StripByRef(parameter.ParameterType);
						if (stateType == null)
						{
							stateType = parameterStateType;
							stateMethod = method.Name;
						}
						else if (stateType != parameterStateType)
						{
							problems.Add($"{where}: __state is {parameterStateType.Name} but {stateMethod} declares {stateType.Name}");
						}

						continue;
					}

					string? problem = DescribeInjectedParameterProblem(parameter, kind, target);
					if (problem != null)
					{
						problems.Add($"{where}: {problem}");
					}
				}
			}
		}

		return checkedParameters;
	}

	// Harmony 按特性识别补丁方法，也按方法名 Prefix/Postfix/Finalizer 识别。转译器的参数不是注入参数，不检查。
	private static string? DescribeHarmonyPatchKind(MethodInfo method)
	{
		if (method.GetCustomAttribute<HarmonyTranspiler>() != null || method.Name == "Transpiler")
		{
			return null;
		}

		if (method.GetCustomAttribute<HarmonyPrefix>() != null || method.Name == "Prefix")
		{
			return "prefix";
		}

		if (method.GetCustomAttribute<HarmonyPostfix>() != null || method.Name == "Postfix")
		{
			return "postfix";
		}

		if (method.GetCustomAttribute<HarmonyFinalizer>() != null || method.Name == "Finalizer")
		{
			return "finalizer";
		}

		return null;
	}

	private static MethodBase? ResolveDeclaredPatchTarget(HarmonyMethod merged)
	{
		if (merged.declaringType == null)
		{
			return null;
		}

		Type declaringType = merged.declaringType;
		return (merged.methodType ?? MethodType.Normal) switch
		{
			MethodType.Normal => merged.methodName == null ? null : AccessTools.Method(declaringType, merged.methodName, merged.argumentTypes),
			MethodType.Getter => merged.methodName == null ? null : AccessTools.PropertyGetter(declaringType, merged.methodName),
			MethodType.Setter => merged.methodName == null ? null : AccessTools.PropertySetter(declaringType, merged.methodName),
			MethodType.Constructor => AccessTools.Constructor(declaringType, merged.argumentTypes),
			// 静态构造、异步/迭代器状态机等目标的注入规则不同，不在这里核对。
			_ => null
		};
	}

	private static string? DescribeInjectedParameterProblem(ParameterInfo parameter, string kind, MethodBase target)
	{
		string name = parameter.Name ?? string.Empty;
		Type parameterType = parameter.ParameterType;
		bool byRef = parameterType.IsByRef;
		Type elementType = StripByRef(parameterType);
		switch (name)
		{
			case "__instance":
			{
				// 静态目标的 __instance 由 Harmony 传 null，不算错误。
				if (target.IsStatic)
				{
					return null;
				}

				Type declaring = target.DeclaringType!;
				return elementType == typeof(object) || elementType.IsAssignableFrom(declaring) || declaring.IsAssignableFrom(elementType)
					? null
					: $"__instance type {elementType.Name} is unrelated to target declaring type {declaring.Name}";
			}
			case "__result":
			{
				Type returnType = target is MethodInfo info ? info.ReturnType : typeof(void);
				if (returnType == typeof(void))
				{
					return "__result on a target that returns void";
				}

				return IsInjectedTypeCompatible(elementType, returnType, byRef)
					? null
					: $"__result is {(byRef ? "ref " : string.Empty)}{elementType.Name} but target returns {returnType.Name}";
			}
			case "__exception":
				return elementType.IsAssignableFrom(typeof(Exception)) ? null : $"__exception must accept Exception, got {elementType.Name}";
			case "__args":
				return elementType == typeof(object[]) ? null : $"__args must be object[], got {elementType.Name}";
			case "__originalMethod":
				return elementType.IsAssignableFrom(typeof(MethodBase)) || elementType.IsAssignableFrom(target.GetType())
					? null
					: $"__originalMethod must accept MethodBase, got {elementType.Name}";
			case "__runOriginal":
				return elementType == typeof(bool) && !byRef ? null : $"__runOriginal must be a plain bool, got {parameterType.Name}";
			case "__resultRef":
				return null;
		}

		if (name.StartsWith("___", StringComparison.Ordinal))
		{
			string fieldName = name[3..];
			FieldInfo? field = AccessTools.Field(target.DeclaringType, fieldName);
			if (field == null)
			{
				return $"field '{fieldName}' does not exist on {target.DeclaringType?.Name}";
			}

			return IsInjectedTypeCompatible(elementType, field.FieldType, byRef)
				? null
				: $"field '{fieldName}' is {field.FieldType.Name}, patch declares {(byRef ? "ref " : string.Empty)}{elementType.Name}";
		}

		ParameterInfo[] targetParameters = target.GetParameters();
		ParameterInfo? targetParameter;
		Match indexed = HarmonyIndexedParameter.Match(name);
		if (indexed.Success)
		{
			int index = int.Parse(indexed.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
			if (index >= targetParameters.Length)
			{
				return $"index {index} is out of range (target has {targetParameters.Length} parameters)";
			}

			targetParameter = targetParameters[index];
		}
		else
		{
			targetParameter = targetParameters.FirstOrDefault(candidate => candidate.Name == name);
			if (targetParameter == null)
			{
				string available = string.Join(", ", targetParameters.Select(static candidate => candidate.Name));
				return $"no target parameter with this name (target {target.DeclaringType?.Name}.{target.Name}({available}), {kind})";
			}
		}

		Type targetType = StripByRef(targetParameter.ParameterType);
		return IsInjectedTypeCompatible(elementType, targetType, byRef)
			? null
			: $"type {(byRef ? "ref " : string.Empty)}{elementType.Name} does not match target parameter type {targetType.Name}";
	}

	// ref 注入要求类型一致（写回会落到原版的局部变量/参数上）；只读注入允许放宽到基类或接口，引用类型也允许窄化。
	private static bool IsInjectedTypeCompatible(Type declared, Type actual, bool byRef)
	{
		if (declared == actual)
		{
			return true;
		}

		if (byRef)
		{
			return false;
		}

		if (declared == typeof(object) || declared.IsAssignableFrom(actual))
		{
			return true;
		}

		return !declared.IsValueType && !actual.IsValueType && actual.IsAssignableFrom(declared);
	}

	private static Type StripByRef(Type type) => type.IsByRef ? type.GetElementType()! : type;
}
