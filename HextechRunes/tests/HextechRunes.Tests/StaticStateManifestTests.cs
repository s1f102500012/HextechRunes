using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HextechRunes;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private static string StaticStateManifestFileName => $"static_state_manifest.{ModInfo.TargetGameVersion}.txt";

	/// <summary>
	/// 可变静态字段清单快照:进程级可变状态是联机分叉与读档串状态的温床,新增一个就必须在这里显形并说明作用域。
	/// 列出非 readonly、非 const 的静态字段,以及类型为可变集合的 static readonly 字段(引用不变、内容可变,同样跨对局存活);
	/// 其余 readonly 字段(反射句柄、数组常量表、不可变/只读集合)不在此列。readonly 可变集合行末带 <c> [readonly]</c>。
	/// 用 HEXTECH_WRITE_PATCH_MANIFEST=1 重生成。
	/// </summary>
	[HextechTest]
	private static void StaticStateManifestMatchesCheckedInList()
	{
		string manifestPath = Path.Combine(AppContext.BaseDirectory, StaticStateManifestFileName);
		string[] actual = BuildStaticStateManifest();
		if (Environment.GetEnvironmentVariable("HEXTECH_WRITE_PATCH_MANIFEST") == "1")
		{
			string sourcePath = Path.Combine(FindTestsSourceDirectory(), StaticStateManifestFileName);
			File.WriteAllText(sourcePath, "# 由 HEXTECH_WRITE_PATCH_MANIFEST=1 生成;每行 = 类型.字段 : 字段类型(可变静态字段);行末 [readonly] = static readonly 的可变集合(规则见 IsMutableCollectionType)。\n" + string.Join("\n", actual) + "\n", Encoding.UTF8);
			Console.WriteLine($"static state manifest written: {sourcePath}");
		}

		Expect(File.Exists(manifestPath), $"{StaticStateManifestFileName} should exist at {manifestPath} (run with HEXTECH_WRITE_PATCH_MANIFEST=1 to generate)");
		string[] expected = File.ReadAllLines(manifestPath)
			.Select(static line => line.Trim())
			.Where(static line => line.Length > 0 && !line.StartsWith('#'))
			.ToArray();

		string[] missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
		string[] unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
		Expect(
			missing.Length == 0 && unexpected.Length == 0,
			$"static state manifest drift; removed: [{string.Join("; ", missing)}] new mutable statics: [{string.Join("; ", unexpected)}]");
	}

	internal static string[] BuildStaticStateManifest()
	{
		List<string> lines = [];
		foreach (Type type in typeof(ModEntry).Assembly.GetTypes())
		{
			if (type.Name.StartsWith('<'))
			{
				continue;
			}

			foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				if (field.IsLiteral || field.Name.StartsWith('<'))
				{
					continue;
				}

				if (field.IsInitOnly)
				{
					if (IsMutableCollectionType(field.FieldType))
					{
						lines.Add($"{type.FullName}.{field.Name} : {field.FieldType.Name} [readonly]");
					}

					continue;
				}

				lines.Add($"{type.FullName}.{field.Name} : {field.FieldType.Name}");
			}
		}

		lines.Sort(StringComparer.Ordinal);
		return lines.ToArray();
	}

	// 内容可变的泛型集合定义（含并发版本与 ConditionalWeakTable），以及声明为可变集合接口的字段。
	private static readonly Type[] MutableCollectionDefinitions =
	[
		typeof(Dictionary<,>),
		typeof(SortedDictionary<,>),
		typeof(SortedList<,>),
		typeof(HashSet<>),
		typeof(SortedSet<>),
		typeof(List<>),
		typeof(LinkedList<>),
		typeof(Queue<>),
		typeof(PriorityQueue<,>),
		typeof(Stack<>),
		typeof(ConcurrentDictionary<,>),
		typeof(ConcurrentQueue<>),
		typeof(ConcurrentStack<>),
		typeof(ConcurrentBag<>),
		typeof(ConditionalWeakTable<,>),
		typeof(IDictionary<,>),
		typeof(ISet<>),
		typeof(IList<>),
		typeof(ICollection<>),
	];

	/// <summary>
	/// static readonly 字段是否算跨对局的可变静态状态：字段类型（或其基类）是 <see cref="MutableCollectionDefinitions"/> 里的泛型集合。
	/// 数组不算（多是常量表）；不可变集合、<c>IReadOnly*</c>、<c>Frozen*</c>、<c>Immutable*</c> 不在表内，也不算。
	/// </summary>
	private static bool IsMutableCollectionType(Type fieldType)
	{
		for (Type? current = fieldType; current != null && current != typeof(object); current = current.BaseType)
		{
			if (current.IsGenericType && MutableCollectionDefinitions.Contains(current.GetGenericTypeDefinition()))
			{
				return true;
			}
		}

		return false;
	}
}
