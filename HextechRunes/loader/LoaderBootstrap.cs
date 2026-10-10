using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

// 本体与拓展包共用这一份加载器源码:拓展包 loader 工程以链接方式编译本文件,并定义 HEXTECH_SPONSOR_LOADER
// 让类型保持在各自原来的命名空间里。两边只在 LoaderBootstrap.Identity.cs 的 ModId / 清单名 / 元数据键上不同。
#if HEXTECH_SPONSOR_LOADER
namespace HextechRunesSponsorPack.Loader;
#else
namespace HextechRunes.Loader;
#endif

[ModInitializer(nameof(Initialize))]
public static partial class LoaderBootstrap
{
	internal const string LogPrefix = "[" + ModId + ".Loader] ";
	private const string RealDllName = ModId + ".dll";
	private const string ReflectionBridgeHarmonyId = "Natsuki." + ModId + ".Loader.ReflectionBridge";
	private const string CompatTargetMarkerName = "compat-target.txt";
	private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly MethodInfo? AssociateAssemblyWithModMethod =
		typeof(ModManager).GetMethod(
			"AssociateAssemblyWithMod",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			[typeof(string), typeof(Assembly)],
			modifiers: null);

	// 加载器只针对 0.107.1 编译,下面两个字段按名字读取:Mod.assemblies 与 AssociateAssemblyWithMod 同在 0.108+,
	// 0.107.x 两者都没有,只有单个 Mod.assembly。
	private static readonly FieldInfo? ModAssembliesField = typeof(Mod).GetField("assemblies", InstanceMembers);

	private static readonly FieldInfo? LegacyModAssemblyField = typeof(Mod).GetField("assembly", InstanceMembers);

	private static VariantCandidate? _deferredVariant;
	private static Assembly? _selectedVariantAssembly;
	private static Type[] _selectedVariantTypes = [];
	private static bool _reflectionBridgeInstalled;
	private static bool _legacyAssociationCallbackInstalled;

	public static void Initialize()
	{
		LinuxNativeDependencyBootstrap.EnsureHarmonyRuntimeDependenciesVisible();

		string? loaderDirectory = Path.GetDirectoryName(typeof(LoaderBootstrap).Assembly.Location);
		if (string.IsNullOrWhiteSpace(loaderDirectory))
		{
			Log.Error($"{LogPrefix}Could not resolve loader directory.");
			ReportLoadFailure(LoadFailureKind.Other);
			return;
		}

		string libRoot = Path.Combine(loaderDirectory, "lib");
		if (!Directory.Exists(libRoot))
		{
			Log.Error($"{LogPrefix}Missing lib directory: {libRoot}");
			ReportLoadFailure(LoadFailureKind.Other);
			return;
		}

		HostVersionSnapshot host = ResolveHostVersion();
		if (host.Numeric == null)
		{
			Log.Warn(
				$"{LogPrefix}Host version is unknown; " +
				"using the newest bundled variant.");
		}

		VariantCandidate? variant = PickVariant(loaderDirectory, libRoot, host.Numeric, out bool hostOlderThanAllVariants);
		if (variant == null)
		{
			// 变体全部无效,或已知宿主没有不高于它的有效变体(对应变体缺失/哈希不符/宿主早于最低支持版本):显式停止。
			Log.Error(
				$"{LogPrefix}No valid variant under {libRoot} compatible with host " +
				$"{host.ReleaseLabel ?? host.Numeric?.ToString() ?? "unknown"}; refusing to load a newer variant.");
			_hostVersionLabel = host.ReleaseLabel ?? host.Numeric?.ToString();
			ReportLoadFailure(hostOlderThanAllVariants ? LoadFailureKind.UnsupportedGameVersion : LoadFailureKind.Other);
			return;
		}

		Log.Info(
			$"{LogPrefix}Host version label={host.ReleaseLabel ?? "<none>"} " +
			$"numeric={host.Numeric?.ToString() ?? "<none>"}; picked variant {variant.CompatTarget}.");

		// 拓展包的变体引用本体程序集:本体没加载就把它交给游戏,游戏扫描类型时会因找不到本体类型而崩溃。
		// 所以等本体程序集出现再加载;本体始终没出现时拓展包只是不加载,并在主菜单提示。
		if (RequiredAssemblyName != null && !IsAssemblyLoaded(RequiredAssemblyName))
		{
			Log.Info($"{LogPrefix}{RequiredAssemblyName} is not loaded yet; deferring variant load until it loads.");
			_deferredVariant = variant;
			AppDomain.CurrentDomain.AssemblyLoad += OnRequiredAssemblyLoaded;
			ReportLoadFailure(LoadFailureKind.RequiredModMissing);
			return;
		}

		LoadVariant(variant);
	}

	private static void LoadVariant(VariantCandidate variant)
	{
		try
		{
			AssemblyLoadContext context =
				AssemblyLoadContext.GetLoadContext(typeof(LoaderBootstrap).Assembly)
				?? AssemblyLoadContext.Default;
			Assembly realAssembly = context.LoadFromAssemblyPath(variant.DllPath);
			ValidateVariantAssembly(realAssembly, variant);

			AssociateVariantAssemblyWithGame(realAssembly);
			InvokeRealInitializer(realAssembly);
			ClearLoadFailure();
		}
		catch (Exception exception)
		{
			LoadFailureKind kind = ClassifyLoadFailure(exception);
			if (kind == LoadFailureKind.ApplicationControlBlocked)
			{
				Log.Error(
					$"{LogPrefix}Windows Application Control (Smart App Control) blocked " +
					$"{variant.DllPath}. The mod is not loaded; disable Smart App Control to use it.");
			}

			Log.Error(
				$"{LogPrefix}Failed to load or initialize " +
				$"{variant.DllPath}: {exception}");
			ReportLoadFailure(kind);
		}
	}

	private static void OnRequiredAssemblyLoaded(object? sender, AssemblyLoadEventArgs args)
	{
		if (!string.Equals(args.LoadedAssembly.GetName().Name, RequiredAssemblyName, StringComparison.Ordinal))
		{
			return;
		}

		AppDomain.CurrentDomain.AssemblyLoad -= OnRequiredAssemblyLoaded;
		VariantCandidate? variant = _deferredVariant;
		_deferredVariant = null;
		if (variant == null)
		{
			return;
		}

		// 初始化全部结束后再交给游戏,类型不会进入模型注册;保持未加载并照常提示。
		if (ModManager.State != ModManagerState.None)
		{
			Log.Error($"{LogPrefix}{RequiredAssemblyName} loaded after mod initialization finished; variant not loaded.");
			return;
		}

		LoadVariant(variant);
	}

	private static bool IsAssemblyLoaded(string name)
	{
		return AppDomain.CurrentDomain.GetAssemblies()
			.Any(assembly => string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal));
	}

	private static void ValidateVariantAssembly(
		Assembly assembly,
		VariantCandidate variant)
	{
		if (!string.Equals(
			assembly.GetName().Name,
			Path.GetFileNameWithoutExtension(RealDllName),
			StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant assembly identity is {assembly.GetName().Name}, expected {ModId}.");
		}

		string? embeddedTarget = assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute =>
				string.Equals(
					attribute.Key,
					CompatTargetMetadataKey,
					StringComparison.Ordinal))
			?.Value;
		if (!string.Equals(
			embeddedTarget,
			variant.CompatTarget,
			StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant compatibility metadata is {embeddedTarget ?? "<missing>"}, " +
				$"expected {variant.CompatTarget}.");
		}
	}

	private static void InstallReflectionBridge()
	{
		if (_reflectionBridgeInstalled)
		{
			return;
		}

		if (_selectedVariantAssembly != null)
		{
			_selectedVariantTypes = GetLoadableTypes(_selectedVariantAssembly).ToArray();
		}

		MethodInfo? getter =
			AccessTools.PropertyGetter(typeof(ReflectionHelper), nameof(ReflectionHelper.ModTypes));
		if (getter == null)
		{
			throw new MissingMethodException("ReflectionHelper.ModTypes getter was not found.");
		}

		new Harmony(ReflectionBridgeHarmonyId).Patch(
			getter,
			postfix: new HarmonyMethod(
				typeof(LoaderBootstrap),
				nameof(ReflectionHelperModTypesPostfix)));
		_reflectionBridgeInstalled = true;
	}

	private static void ReflectionHelperModTypesPostfix(ref Type[] __result)
	{
		if (_selectedVariantTypes.Length > 0)
		{
			__result = __result.Concat(_selectedVariantTypes).Distinct().ToArray();
		}
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			Log.Warn(
				$"{LogPrefix}Partial type load for " +
				$"{assembly.FullName}: {exception.Message}");
			return exception.Types.OfType<Type>();
		}
	}

	// 两条路径,只走一条:
	//   ① 0.108+ ModManager.AssociateAssemblyWithMod → 游戏自己把变体程序集并入 Mod.assemblies,类型发现随之生效。
	//      初始化器运行时本模组状态必为 None,原版在这一状态下总会登记成功,所以不再另写反射追加的回退。
	//   ② 0.107.x 只有单个 Mod.assembly 字段,且 ModManager 在初始化器返回后会用 loader 覆盖它,
	//      只能在 OnModDetected 里替换,并补 ReflectionHelper.ModTypes 后缀让类型发现看到变体。
	// ① 成功后绝不再装 ReflectionHelper.ModTypes 后缀:两条路径同时贡献类型会让 ModelDb 把同一个类型
	// 看成两个,进而触发 "Two AbstractModels X and X share an ID" 的自比较告警。
	private static void AssociateVariantAssemblyWithGame(Assembly assembly)
	{
		_selectedVariantAssembly = assembly;

		if (AssociateAssemblyWithModMethod != null)
		{
			try
			{
				AssociateAssemblyWithModMethod.Invoke(null, [ModId, assembly]);
				if (IsAssemblyAssociatedWithMod(assembly))
				{
					Log.Info($"{LogPrefix}Variant associated via ModManager.AssociateAssemblyWithMod.");
					return;
				}
			}
			catch (Exception exception)
			{
				Log.Warn(
					$"{LogPrefix}AssociateAssemblyWithMod failed: " +
					$"{exception.GetBaseException().Message}");
			}
		}

		InstallReflectionBridge();
		// 拓展包延迟加载时,自己的 OnModDetected 早已触发,直接替换记录里的程序集。
		if (LegacyModAssemblyField != null && TryFindMod(out Mod? detectedMod) && detectedMod.state != ModLoadState.None)
		{
			OnLegacyModDetected(detectedMod);
			return;
		}

		if (LegacyModAssemblyField != null && !_legacyAssociationCallbackInstalled)
		{
			ModManager.OnModDetected += OnLegacyModDetected;
			_legacyAssociationCallbackInstalled = true;
			return;
		}

		Log.Warn(
			$"{LogPrefix}Could not associate the selected variant " +
			"with ModManager; type discovery will rely on the reflection bridge.");
	}

	private static void OnLegacyModDetected(Mod mod)
	{
		if (_selectedVariantAssembly == null
			|| !string.Equals(mod.manifest?.id, ModId, StringComparison.Ordinal))
		{
			return;
		}

		LegacyModAssemblyField?.SetValue(mod, _selectedVariantAssembly);
		ModManager.OnModDetected -= OnLegacyModDetected;
		_legacyAssociationCallbackInstalled = false;
		Log.Info(
			$"{LogPrefix}Associated variant " +
			$"{_selectedVariantAssembly.GetName().Name} with the STS2 0.107.x mod record.");
	}

	private static bool IsAssemblyAssociatedWithMod(Assembly assembly)
	{
		return TryFindMod(out Mod? mod)
			&& ModAssembliesField?.GetValue(mod) is IList assemblies
			&& assemblies.Cast<object>().Any(item => ReferenceEquals(item, assembly));
	}

	private static bool TryFindMod([NotNullWhen(true)] out Mod? mod)
	{
		mod = ModManager.Mods.FirstOrDefault(candidate =>
			string.Equals(candidate.manifest?.id, ModId, StringComparison.Ordinal));
		return mod != null;
	}

	private static void InvokeRealInitializer(Assembly assembly)
	{
		foreach (Type type in GetLoadableTypes(assembly))
		{
			ModInitializerAttribute? attribute =
				type.GetCustomAttribute<ModInitializerAttribute>();
			if (attribute == null)
			{
				continue;
			}

			MethodInfo? initializer = type.GetMethod(
				attribute.initializerMethod,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (initializer == null)
			{
				throw new MissingMethodException(
					$"{type.FullName}.{attribute.initializerMethod} was not found.");
			}

			initializer.Invoke(null, null);
			return;
		}

		throw new MissingMethodException(
			$"No {nameof(ModInitializerAttribute)} was found in {assembly.FullName}.");
	}

	internal static VariantCandidate? PickVariant(
		string loaderDirectory,
		string libRoot,
		Version? host)
	{
		return PickVariant(loaderDirectory, libRoot, host, out _);
	}

	/// <param name="hostOlderThanAllVariants">宿主已知且早于清单里所有有效变体,即游戏版本太旧;用于给玩家正确的提示。</param>
	internal static VariantCandidate? PickVariant(
		string loaderDirectory,
		string libRoot,
		Version? host,
		out bool hostOlderThanAllVariants)
	{
		List<VariantCandidate> variants =
			LoadVariantManifest(loaderDirectory, libRoot)
				.OrderBy(candidate => candidate.Version)
				.ToList();
		hostOlderThanAllVariants = host != null
			&& variants.Count > 0
			&& variants.All(candidate => candidate.Version > host);

		// 只对选中的变体算 SHA256;不符就剔除后重选,结果与"先剔除所有不符的变体再选"相同。
		while (SelectVariant(variants, host) is { } selected)
		{
			if (MatchesExpectedHash(selected.DllPath, selected.Sha256))
			{
				return selected;
			}

			Log.Error($"{LogPrefix}Ignoring hash-mismatched variant: {selected.DllPath}");
			variants.Remove(selected);
		}

		return null;
	}

	/// <summary>
	/// 纯选择规则(不写日志,测试直接调用):宿主未知时用最新变体;宿主已知时取不高于宿主的最新变体,
	/// 没有就返回 null 让调用方停止加载。回退到更新的变体只会把"局部文件不可用"
	/// 扩大成"错误版本程序集进入模型注册与补丁系统",所以不再兜底。
	/// </summary>
	internal static VariantCandidate? SelectVariant(
		IReadOnlyList<VariantCandidate> sortedVariants,
		Version? host)
	{
		if (sortedVariants.Count == 0)
		{
			return null;
		}

		if (host == null)
		{
			return sortedVariants[^1];
		}

		return sortedVariants.LastOrDefault(candidate => candidate.Version <= host);
	}

	private static List<VariantCandidate> LoadVariantManifest(
		string loaderDirectory,
		string libRoot)
	{
		string path = Path.Combine(loaderDirectory, VariantManifestName);
		if (!File.Exists(path))
		{
			Log.Error($"{LogPrefix}Missing variant manifest: {path}");
			return [];
		}

		BundleVariantManifest? manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<BundleVariantManifest>(
				File.ReadAllText(path),
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		}
		catch (Exception exception)
		{
			Log.Error(
				$"{LogPrefix}Failed to read variant manifest: {exception}");
			return [];
		}

		if (manifest?.Variants == null || manifest.Variants.Count == 0)
		{
			Log.Error(
				$"{LogPrefix}Variant manifest contains no variants: {path}");
			return [];
		}

		string fullLibRoot = Path.GetFullPath(libRoot);
		return manifest.Variants
			.Select(entry => TryCreateVariantCandidate(
				loaderDirectory,
				fullLibRoot,
				entry))
			.OfType<VariantCandidate>()
			.ToList();
	}

	private static VariantCandidate? TryCreateVariantCandidate(
		string loaderDirectory,
		string fullLibRoot,
		BundleVariantEntry entry)
	{
		string compatTarget = entry.CompatTarget?.Trim() ?? string.Empty;
		if (!TryParseVersion(compatTarget, out Version version))
		{
			Log.Error(
				$"{LogPrefix}Ignoring invalid target " +
				$"'{entry.CompatTarget}'.");
			return null;
		}

		string relativeDirectory = string.IsNullOrWhiteSpace(entry.Directory)
			? Path.Combine("lib", compatTarget)
			: entry.Directory.Trim();
		string variantDirectory =
			Path.GetFullPath(Path.Combine(loaderDirectory, relativeDirectory));
		if (!IsUnderDirectory(variantDirectory, fullLibRoot)
			|| !string.Equals(
				Path.GetFileName(variantDirectory),
				compatTarget,
				StringComparison.Ordinal))
		{
			Log.Error(
				$"{LogPrefix}Ignoring invalid variant directory " +
				$"'{relativeDirectory}'.");
			return null;
		}

		string markerPath = Path.Combine(variantDirectory, CompatTargetMarkerName);
		if (!File.Exists(markerPath)
			|| !string.Equals(
				File.ReadAllText(markerPath).Trim(),
				compatTarget,
				StringComparison.Ordinal))
		{
			Log.Error(
				$"{LogPrefix}Ignoring variant with missing or " +
				$"mismatched marker: {markerPath}");
			return null;
		}

		string assemblyName = string.IsNullOrWhiteSpace(entry.Assembly)
			? RealDllName
			: entry.Assembly.Trim();
		if (!string.Equals(assemblyName, RealDllName, StringComparison.Ordinal))
		{
			Log.Error(
				$"{LogPrefix}Ignoring unexpected assembly " +
				$"'{assemblyName}'.");
			return null;
		}

		string dllPath = Path.Combine(variantDirectory, assemblyName);
		if (!File.Exists(dllPath))
		{
			Log.Error($"{LogPrefix}Ignoring missing variant: {dllPath}");
			return null;
		}

		return new VariantCandidate(compatTarget, version, dllPath, entry.Sha256);
	}

	private static bool IsUnderDirectory(string path, string root)
	{
		string normalizedRoot =
			root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		string normalizedPath =
			path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		return normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal);
	}

	private static bool MatchesExpectedHash(string path, string? expected)
	{
		if (string.IsNullOrWhiteSpace(expected))
		{
			return false;
		}

		using FileStream stream = File.OpenRead(path);
		string actual = Convert.ToHexString(SHA256.HashData(stream));
		return string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static HostVersionSnapshot ResolveHostVersion()
	{
		// 原版懒单例:构造时按可执行文件目录查找 release_info.json(macOS 先找 ../Resources),
		// 读不到或解析失败都在内部记日志并返回 null,不抛异常。0.107.1–0.111.0 的查找路径一致。
		string? label = ReleaseInfoManager.Instance.ReleaseInfo?.Version;
		if (string.IsNullOrWhiteSpace(label))
		{
			return new HostVersionSnapshot(null, null);
		}

		// 解析不了就当版本未知,交给 SelectVariant 用最新变体。不回退到 sts2.dll 的程序集版本:
		// 各版本都是 0.1.0.0,会被当成已知的旧宿主而拒绝加载。
		return new HostVersionSnapshot(TryParseVersion(label, out Version version) ? version : null, label);
	}

	private static bool TryParseVersion(string text, out Version version)
	{
		string value = text.Trim();
		int suffixIndex = value.IndexOfAny(['-', '+']);
		if (suffixIndex >= 0)
		{
			value = value[..suffixIndex].Trim();
		}
		if (value.Length >= 2
			&& (value[0] == 'v' || value[0] == 'V')
			&& char.IsDigit(value[1]))
		{
			value = value[1..];
		}

		if (Version.TryParse(value, out Version? parsed))
		{
			version = parsed;
			return true;
		}

		version = new Version(0, 0);
		return false;
	}

	internal sealed record VariantCandidate(
		string CompatTarget,
		Version Version,
		string DllPath,
		string? Sha256 = null);

	private readonly record struct HostVersionSnapshot(
		Version? Numeric,
		string? ReleaseLabel);

	private sealed class BundleVariantManifest
	{
		public List<BundleVariantEntry>? Variants { get; set; }
	}

	private sealed class BundleVariantEntry
	{
		public string? CompatTarget { get; set; }
		public string? Directory { get; set; }
		public string? Assembly { get; set; }
		public string? Sha256 { get; set; }
	}
}
