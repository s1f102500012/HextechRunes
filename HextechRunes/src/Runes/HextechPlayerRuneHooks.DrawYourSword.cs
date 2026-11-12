using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

// 亮出你的剑的充能球激发替换：补丁安装在 DrawYourSwordRune.DrawYourSwordEvokePatch，这里只放目标枚举与前缀本体。
internal static partial class HextechPlayerRuneHooks
{
	private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

	/// <summary>
	/// 只补原版程序集里的充能球 Evoke 覆写,不给第三方模组的充能球类打补丁:
	/// 那等于替别人的类型做决定;第三方充能球保持原版激发,亮剑不替换它们。
	/// 基类 OrbModel.Evoke(PlayerChoiceContext) 缺失时抛异常，由 HextechPatcher 把亮剑标为本运行时不可用。
	/// </summary>
	internal static IReadOnlyList<MethodInfo> FindOrbEvokeMethods()
	{
		MethodInfo baseEvoke = TryGetMethod(typeof(OrbModel), nameof(OrbModel.Evoke), PublicInstance, typeof(PlayerChoiceContext))
			?? throw new MissingMethodException(typeof(OrbModel).FullName, nameof(OrbModel.Evoke));
		HashSet<MethodInfo> methods = [baseEvoke];
		foreach (Type type in GetLoadableTypes(typeof(OrbModel).Assembly))
		{
			if (type == typeof(OrbModel) || !typeof(OrbModel).IsAssignableFrom(type))
			{
				continue;
			}

			// 大多数充能球不覆写 Evoke，逐类型查找缺失属于正常情况，不进缺失成员摘要。
			MethodInfo? evoke = TryGetMethod(
				type,
				nameof(OrbModel.Evoke),
				PublicInstance | BindingFlags.DeclaredOnly,
				warnIfMissing: false,
				typeof(PlayerChoiceContext));
			if (evoke is { IsAbstract: false } && evoke.ReturnType == typeof(Task<IEnumerable<Creature>>))
			{
				methods.Add(evoke);
			}
		}

		return methods
			.OrderBy(static method => method.DeclaringType?.FullName, StringComparer.Ordinal)
			.ToArray();
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where(static type => type != null).Cast<Type>();
		}
	}

	internal static bool OrbEvokePrefix(OrbModel __instance, ref Task<IEnumerable<Creature>> __result)
	{
		DrawYourSwordRune? rune = __instance.Owner?.GetRelic<DrawYourSwordRune>();
		if (rune == null || !rune.ShouldReplaceOrbEvoke(__instance))
		{
			return true;
		}

		__result = rune.ReplaceOrbEvoke();
		return false;
	}
}
