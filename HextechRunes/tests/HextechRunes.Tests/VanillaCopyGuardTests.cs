using System.Reflection;
using HarmonyLib;
using HextechRunes;

namespace HextechRunes.Tests;

// 原版拷贝守卫冻结表的覆盖面检查。跳过型前缀目标的清单来自 0.111.0 的真实游戏补丁导出
// （HEXTECH_DUMP_PATCHES），其余维护版本在各自的 sts2.dll 里按同一清单解析；异步目标必须连同
// 编译器生成的 MoveNext 一起冻结。HEXTECH_WRITE_PATCH_MANIFEST=1 时只追加缺失的行，
// 已冻结却漂移的行永远不自动刷新——那需要对照原版逐条复核替换逻辑。
internal static partial class Program
{
	private const string GuardReferenceTarget = "0.111.0";

	[HextechTest]
	private static void VanillaCopyGuardFreezesEntriesAndAsyncBodies()
	{
		string testsRoot = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), ".."));
		string currentPath = Path.Combine(testsRoot, $"vanilla_copy_guard.{ModInfo.TargetGameVersion}.txt");
		string referencePath = Path.Combine(testsRoot, $"vanilla_copy_guard.{GuardReferenceTarget}.txt");
		Dictionary<string, string> current = ReadGuardTable(currentPath);
		// 目标清单 = 参考表 + 本目标表 + 本程序集里声明为跳过型前缀或 CopiesVanillaLogic 的补丁目标。
		// 只从表行取键时，新增的跳过型前缀漏登记不会被发现（启动日志只提示一次）；从补丁声明派生后会在这里失败。
		IEnumerable<string> entryKeys = ReadGuardTable(referencePath).Keys
			.Concat(current.Keys)
			.Concat(EnumerateDeclaredGuardTargets().Select(HextechVanillaCopyGuard.DescribeTarget))
			.Where(static key => !key.EndsWith("::MoveNext()", StringComparison.Ordinal))
			.Distinct(StringComparer.Ordinal);

		List<string> missing = [];
		List<string> drifted = [];
		foreach (string entryKey in entryKeys)
		{
			if (ResolveGuardKey(entryKey) is not MethodBase entry)
			{
				// 目标在本版本不存在（补丁按版本 #if 区分）；运行时守卫只校验实际打上的目标。
				continue;
			}

			foreach (MethodBase method in HextechVanillaCopyGuard.WithAsyncBody(entry))
			{
				string key = HextechVanillaCopyGuard.DescribeTarget(method);
				string hash = HextechVanillaCopyGuard.ComputeIlHash(method) ?? "<no body>";
				if (!current.TryGetValue(key, out string? frozen))
				{
					missing.Add($"{key}={hash}");
				}
				else if (!string.Equals(frozen, hash, StringComparison.Ordinal))
				{
					drifted.Add($"{key} frozen={frozen} actual={hash}");
				}
			}
		}

		if (Environment.GetEnvironmentVariable("HEXTECH_WRITE_PATCH_MANIFEST") == "1" && missing.Count > 0)
		{
			File.AppendAllLines(currentPath, missing.OrderBy(static row => row, StringComparer.Ordinal));
			missing.Clear();
		}

		Expect(drifted.Count == 0, "vanilla copy guard drift needs review:\n  " + string.Join("\n  ", drifted));
		Expect(missing.Count == 0, "vanilla copy guard is missing rows (regenerate with HEXTECH_WRITE_PATCH_MANIFEST=1):\n  " + string.Join("\n  ", missing));
	}

	/// <summary>
	/// 属性式补丁里会被运行时守卫纳入的目标：带 <c>bool</c> 返回值的 <c>[HarmonyPrefix]</c>（可跳过原方法，
	/// 与 <c>HextechPatcher.IsSkipCapable</c> 同口径），或补丁类声明 <c>CopiesVanillaLogic</c>。
	/// <c>[HarmonyTargetMethod(s)]</c> 与动态 <c>Apply</c> 的目标要在运行时枚举，不在这里派生，仍靠已有表行与启动日志。
	/// </summary>
	private static IEnumerable<MethodBase> EnumerateDeclaredGuardTargets()
	{
		const BindingFlags memberFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		foreach (Type type in typeof(ModEntry).Assembly.GetTypes())
		{
			List<HarmonyMethod> harmonyAttributes = HarmonyMethodExtensions.GetFromType(type);
			if (harmonyAttributes.Count == 0)
			{
				continue;
			}

			MethodInfo[] methods = type.GetMethods(memberFlags);
			if (methods.Any(static method => method.GetCustomAttribute<HarmonyTargetMethod>() != null
				|| method.GetCustomAttribute<HarmonyTargetMethods>() != null))
			{
				continue;
			}

			bool skipCapable = methods.Any(static method => method.GetCustomAttribute<HarmonyPrefix>() != null && method.ReturnType == typeof(bool));
			bool copiesVanilla = type.GetCustomAttribute<HextechPatchAttribute>()?.CopiesVanillaLogic == true;
			if (!skipCapable && !copiesVanilla)
			{
				continue;
			}

			HarmonyMethod merged = HarmonyMethod.Merge(harmonyAttributes);
			if (merged.declaringType == null)
			{
				continue;
			}

			MethodType methodType = merged.methodType ?? MethodType.Normal;
			MethodBase? target = methodType switch
			{
				MethodType.Constructor => AccessTools.Constructor(merged.declaringType, merged.argumentTypes),
				MethodType.Getter => merged.methodName == null ? null : AccessTools.PropertyGetter(merged.declaringType, merged.methodName),
				MethodType.Setter => merged.methodName == null ? null : AccessTools.PropertySetter(merged.declaringType, merged.methodName),
				MethodType.Normal => merged.methodName == null ? null : AccessTools.Method(merged.declaringType, merged.methodName, merged.argumentTypes),
				_ => null
			};
			if (target != null)
			{
				yield return target;
			}
		}
	}

	private static Dictionary<string, string> ReadGuardTable(string path)
	{
		Dictionary<string, string> rows = new(StringComparer.Ordinal);
		if (!File.Exists(path))
		{
			return rows;
		}

		foreach (string line in File.ReadAllLines(path))
		{
			string trimmed = line.Trim();
			int separator = trimmed.LastIndexOf('=');
			if (trimmed.Length > 0 && !trimmed.StartsWith('#') && separator > 0)
			{
				rows[trimmed[..separator]] = trimmed[(separator + 1)..];
			}
		}

		return rows;
	}

	private static MethodBase? ResolveGuardKey(string key)
	{
		int split = key.IndexOf("::", StringComparison.Ordinal);
		int open = key.IndexOf('(', split);
		if (split <= 0 || open < 0 || !key.EndsWith(')'))
		{
			throw new InvalidOperationException("malformed vanilla copy guard key: " + key);
		}

		string typeName = key[..split];
		string methodName = key[(split + 2)..open];
		string parameters = key[(open + 1)..^1];
		Type? type = AppDomain.CurrentDomain.GetAssemblies()
			.Select(assembly => assembly.GetType(typeName))
			.FirstOrDefault(static candidate => candidate != null);
		if (type == null)
		{
			return null;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		IEnumerable<MethodBase> candidates = methodName == ".ctor"
			? type.GetConstructors(flags)
			: type.GetMethods(flags).Where(method => method.Name == methodName);
		return candidates.FirstOrDefault(method => string.Join(",", method.GetParameters()
			.Select(static parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name)) == parameters);
	}
}
