using System.Collections;
using System.Collections.Concurrent;

namespace HextechRunes;

internal static partial class HextechMayhemCombatTrackingSerializer
{
	// 持久字段只有 Dictionary（走非泛型 IDictionary）与 HashSet<T>（ValidateSnapshotPropertyType 启动时强制）；
	// 临时字段另有 string/bool 标量，只需清空。HashSet<T> 没有非泛型集合接口，按集合类型缓存一次
	// Clear/Add/Count 反射句柄；ClearPhase 每个回合边界都会跑，不能每次 GetMethod。
	private sealed record HashSetAccessor(MethodInfo Clear, MethodInfo Add, PropertyInfo Count);

	private static readonly ConcurrentDictionary<Type, HashSetAccessor> HashSetAccessors = new();

	private static HashSetAccessor GetHashSetAccessor(Type setType)
	{
		return HashSetAccessors.GetOrAdd(setType, static type => new HashSetAccessor(
			type.GetMethod(nameof(HashSet<object>.Clear), Type.EmptyTypes)
				?? throw new InvalidOperationException($"Combat tracking collection {type} does not expose Clear()."),
			type.GetMethod(nameof(HashSet<object>.Add), [type.GetGenericArguments()[0]])
				?? throw new InvalidOperationException($"Combat tracking collection {type} does not expose Add()."),
			type.GetProperty(nameof(HashSet<object>.Count), BindingFlags.Instance | BindingFlags.Public)
				?? throw new InvalidOperationException($"Combat tracking collection {type} does not expose Count.")));
	}

	private static object? CopyStateValue(object? source, Type snapshotType)
	{
		if (source == null)
		{
			return null;
		}

		return source is IDictionary dictionary
			? CopyDictionary(dictionary, snapshotType)
			: CopySet(source, snapshotType);
	}

	private static object CopyDictionary(IDictionary source, Type snapshotType)
	{
		IDictionary target = (IDictionary)(Activator.CreateInstance(snapshotType)
			?? throw new InvalidOperationException($"Failed to create combat tracking dictionary {snapshotType}."));
		foreach (object key in OrderedValues(source.Keys))
		{
			target.Add(key, source[key]);
		}

		return target;
	}

	private static object CopySet(object source, Type snapshotType)
	{
		IList target = (IList)(Activator.CreateInstance(snapshotType)
			?? throw new InvalidOperationException($"Failed to create combat tracking list {snapshotType}."));
		foreach (object value in OrderedValues((IEnumerable)source))
		{
			target.Add(value);
		}

		return target;
	}

	private static void RestoreStateField(FieldInfo field, HextechMayhemCombatTrackingState state, object? snapshotValue)
	{
		object? target = field.GetValue(state);
		if (target is IDictionary targetDictionary)
		{
			targetDictionary.Clear();
			if (snapshotValue is IDictionary sourceDictionary)
			{
				foreach (DictionaryEntry entry in sourceDictionary)
				{
					targetDictionary[entry.Key] = entry.Value;
				}
			}

			return;
		}

		HashSetAccessor accessor = GetHashSetAccessor(target!.GetType());
		accessor.Clear.Invoke(target, null);
		if (snapshotValue is IEnumerable sourceValues)
		{
			foreach (object value in sourceValues)
			{
				accessor.Add.Invoke(target, [value]);
			}
		}
	}

	private static void ClearStateField(FieldInfo field, HextechMayhemCombatTrackingState state)
	{
		object? target = field.GetValue(state);
		if (target is IDictionary dictionary)
		{
			dictionary.Clear();
			return;
		}

		if (target != null && IsHashSet(target.GetType()))
		{
			GetHashSetAccessor(target.GetType()).Clear.Invoke(target, null);
			return;
		}

		if (field.FieldType == typeof(string))
		{
			field.SetValue(state, null);
		}
		else if (field.FieldType == typeof(bool))
		{
			field.SetValue(state, false);
		}
	}

	private static bool HasNonDefaultValue(object? value)
	{
		return value switch
		{
			null => false,
			IDictionary dictionary => dictionary.Count > 0,
			_ => GetHashSetAccessor(value.GetType()).Count.GetValue(value) is int count && count > 0
		};
	}

	private static bool IsHashSet(Type type)
	{
		return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);
	}

	private static IEnumerable<object> OrderedValues(IEnumerable values)
	{
		return values.Cast<object>().OrderBy(static value => value, Comparer<object>.Create(CompareValues));
	}

	private static int CompareValues(object? left, object? right)
	{
		if (ReferenceEquals(left, right))
		{
			return 0;
		}

		if (left == null)
		{
			return -1;
		}

		if (right == null)
		{
			return 1;
		}

		// string 的 IComparable 实现是 culture-sensitive 的，两端 locale 不同会把同一组键排出不同序，
		// 序列化结果须两端一致（不参与原版联机校验，靠本模组保证），必须走 ordinal。
		if (left is string leftText && right is string rightText)
		{
			return string.CompareOrdinal(leftText, rightText);
		}

		return left is IComparable comparable
			? comparable.CompareTo(right)
			: string.CompareOrdinal(left.ToString(), right.ToString());
	}
}
