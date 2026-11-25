using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using MegaCrit.Sts2.Core.Debug;
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
	private const string CompatTargetMarkerName = "compat-target.txt";

	private static VariantCandidate? _deferredVariant;

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

		VariantCandidate? variant = PickVariant(loaderDirectory, libRoot, host.Numeric, out bool hostUnsupported, out Version? minimumTarget);
		if (variant == null)
		{
			// 变体全部无效,或已知宿主没有不高于它的有效变体(对应变体缺失/哈希不符/宿主早于最低支持版本):显式停止。
			string hostLabel = host.ReleaseLabel ?? host.Numeric?.ToString() ?? "unknown";
			if (IsOlderThanMinimumTarget(host.Numeric, minimumTarget))
			{
				// 清单的 min_game_version 通常已让游戏拒绝加载本模组;这里兜底,不拿最低目标去跑更旧的游戏。
				Log.Error(
					$"{LogPrefix}Host version {hostLabel} is older than the minimum supported game version " +
					$"{minimumTarget}; no variant is loaded.");
			}
			else
			{
				Log.Error(
					$"{LogPrefix}No valid variant under {libRoot} compatible with host " +
					$"{hostLabel}; refusing to load a newer variant.");
			}
			_hostVersionLabel = host.ReleaseLabel ?? host.Numeric?.ToString();
			ReportLoadFailure(hostUnsupported ? LoadFailureKind.UnsupportedGameVersion : LoadFailureKind.Other);
			return;
		}

		Log.Info(
			$"{LogPrefix}Host version label={host.ReleaseLabel ?? "<none>"} " +
			$"numeric={host.Numeric?.ToString() ?? "<none>"}; picked variant {variant.CompatTarget}.");

		// 拓展包的变体引用本体程序集,注册内容时还要用本体已初始化好的登记表:本体没初始化完就交给游戏,
		// 游戏扫描类型会因找不到本体类型而崩溃,或者拓展包内容登记失败。所以等本体加载器报告初始化成功再加载;
		// 本体失败或始终没加载时拓展包只是不加载,并在主菜单提示。
		if (RequiredAssemblyName != null)
		{
			switch (GetRequiredLoaderResult())
			{
				case RequiredLoaderResult.Failed:
					Log.Error($"{LogPrefix}{RequiredAssemblyName} failed to load; variant not loaded.");
					ReportLoadFailure(LoadFailureKind.RequiredModMissing);
					return;
				case RequiredLoaderResult.Pending:
					Log.Info($"{LogPrefix}{RequiredAssemblyName} is not initialized yet; deferring variant load until it is.");
					_deferredVariant = variant;
					AddMainLoaderWaiter(OnRequiredLoaderFinished);
					ReportLoadFailure(LoadFailureKind.RequiredModMissing);
					return;
			}
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

	internal enum RequiredLoaderResult
	{
		Ready,
		Failed,
		Pending,
	}

	internal static RequiredLoaderResult GetRequiredLoaderResult()
	{
		switch (AppDomain.CurrentDomain.GetData(MainLoaderResultDataKey))
		{
			case true:
				return RequiredLoaderResult.Ready;
			case false:
				return RequiredLoaderResult.Failed;
		}

		// 不发布结果的旧版本体加载器:模组初始化器按顺序同步执行,看到本体程序集时它的初始化已经结束。
		return RequiredAssemblyName != null && IsAssemblyLoaded(RequiredAssemblyName)
			? RequiredLoaderResult.Ready
			: RequiredLoaderResult.Pending;
	}

	// 本体加载器初始化结束(成功或失败)时调用,此时仍在游戏的模组初始化循环里。
	private static void OnRequiredLoaderFinished(bool ready)
	{
		VariantCandidate? variant = _deferredVariant;
		_deferredVariant = null;
		if (variant == null)
		{
			return;
		}

		if (!ready)
		{
			Log.Error($"{LogPrefix}{RequiredAssemblyName} failed to load; variant not loaded.");
			return;
		}

		// 初始化全部结束后再交给游戏,类型不会进入模型注册;保持未加载并照常提示。
		if (ModManager.State != ModManagerState.None)
		{
			Log.Error($"{LogPrefix}{RequiredAssemblyName} finished after mod initialization; variant not loaded.");
			return;
		}

		LoadVariant(variant);
	}

	internal static void AddMainLoaderWaiter(Action<bool> waiter)
	{
		Action<bool>? existing = AppDomain.CurrentDomain.GetData(MainLoaderWaitersDataKey) as Action<bool>;
		AppDomain.CurrentDomain.SetData(MainLoaderWaitersDataKey, existing + waiter);
	}

	/// <summary>本体加载器公布初始化结果,并通知排在它前面、正在等待的拓展包。拓展包加载器不调用。</summary>
	internal static void PublishMainLoaderResult(bool ready)
	{
		if (RequiredAssemblyName != null)
		{
			return;
		}

		AppDomain.CurrentDomain.SetData(MainLoaderResultDataKey, ready);
		if (AppDomain.CurrentDomain.GetData(MainLoaderWaitersDataKey) is not Action<bool> waiters)
		{
			return;
		}

		AppDomain.CurrentDomain.SetData(MainLoaderWaitersDataKey, null);
		foreach (Action<bool> waiter in waiters.GetInvocationList().Cast<Action<bool>>())
		{
			try
			{
				waiter(ready);
			}
			catch (Exception exception)
			{
				Log.Error($"{LogPrefix}A dependent loader failed while handling this mod's load result: {exception}");
			}
		}
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

	// ModManager.AssociateAssemblyWithMod 让游戏把变体程序集并入 Mod.assemblies,类型发现随之生效。
	// 初始化器运行时本模组状态必为 None(拓展包延迟加载时仍在初始化循环内),原版在这一状态下总会登记成功;
	// 万一没登记上就按加载失败处理,不让未进入类型发现的变体继续初始化。
	private static void AssociateVariantAssemblyWithGame(Assembly assembly)
	{
		ModManager.AssociateAssemblyWithMod(ModId, assembly);
		Mod? mod = ModManager.Mods.FirstOrDefault(candidate =>
			string.Equals(candidate.manifest?.id, ModId, StringComparison.Ordinal));
		if (mod?.assemblies.Contains(assembly) != true)
		{
			throw new InvalidOperationException(
				$"ModManager.AssociateAssemblyWithMod did not associate {assembly.GetName().Name} with mod {ModId}.");
		}

		Log.Info($"{LogPrefix}Variant associated via ModManager.AssociateAssemblyWithMod.");
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
		return PickVariant(loaderDirectory, libRoot, host, out _, out _);
	}

	/// <param name="hostUnsupported">宿主已知但没有对应的变体（早于所有变体，或落在已退役的中间版本），即游戏版本不受支持;用于给玩家正确的提示。</param>
	/// <param name="minimumTarget">清单声明的最低目标，即最低支持的游戏版本；清单不可用时为 null。</param>
	internal static VariantCandidate? PickVariant(
		string loaderDirectory,
		string libRoot,
		Version? host,
		out bool hostUnsupported,
		out Version? minimumTarget)
	{
		List<VariantCandidate> variants =
			LoadVariantManifest(loaderDirectory, libRoot, out List<Version> declaredTargets)
				.OrderBy(candidate => candidate.Version)
				.ToList();
		minimumTarget = declaredTargets.Count > 0 ? declaredTargets.Min() : null;
		// 兼容范围按清单声明的全部目标算，不看文件是否可用：最新目标的 DLL 缺失或哈希不符时，
		// 不能把较旧的变体当成“最新”去服务更新的游戏。
		Version? newestDeclared = declaredTargets.Count > 0 ? declaredTargets.Max() : null;
		hostUnsupported = host != null
			&& newestDeclared != null
			&& !declaredTargets.Any(target => IsCompatibleTarget(target, host, newestDeclared));

		// 只对选中的变体算 SHA256;不符就剔除后重选。重选仍按声明的兼容范围,不会扩大到其他小版本。
		while (SelectVariant(variants, host, newestDeclared) is { } selected)
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
	/// 纯选择规则(不写日志,测试直接调用):按 <see cref="IsCompatibleTarget"/> 取兼容的变体,
	/// 没有就返回 null 让调用方停止加载。回退到更新的变体只会把"局部文件不可用"
	/// 扩大成"错误版本程序集进入模型注册与补丁系统",所以不再兜底。
	/// </summary>
	/// <param name="newestDeclared">清单声明的最新目标（含文件不可用的）；省略时取 <paramref name="sortedVariants"/> 里最新的。</param>
	internal static VariantCandidate? SelectVariant(
		IReadOnlyList<VariantCandidate> sortedVariants,
		Version? host,
		Version? newestDeclared = null)
	{
		if (sortedVariants.Count == 0)
		{
			return null;
		}

		Version newest = newestDeclared ?? sortedVariants[^1].Version;
		return sortedVariants.LastOrDefault(candidate => IsCompatibleTarget(candidate.Version, host, newest));
	}

	/// <summary>
	/// 只有声明的最新目标向后兼容更新（或未知版本）的游戏；较旧的目标只服务同一小版本（0.111.x 用 0.111.0）。
	/// 没有对应目标的中间版本（如 0.112）和早于最低目标的版本（如 0.107、0.110）都不能套用别的变体。
	/// </summary>
	internal static bool IsCompatibleTarget(Version target, Version? host, Version newestDeclared)
	{
		if (host == null || host >= newestDeclared)
		{
			return target == newestDeclared;
		}

		return target <= host
			&& target.Major == host.Major
			&& target.Minor == host.Minor;
	}

	/// <summary>已知宿主早于清单声明的最低目标（最低支持的游戏版本）。</summary>
	internal static bool IsOlderThanMinimumTarget(Version? host, Version? minimumTarget)
	{
		return host != null && minimumTarget != null && host < minimumTarget;
	}

	private static List<VariantCandidate> LoadVariantManifest(
		string loaderDirectory,
		string libRoot,
		out List<Version> declaredTargets)
	{
		declaredTargets = [];
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

		foreach (BundleVariantEntry entry in manifest.Variants)
		{
			if (TryParseVersion(entry.CompatTarget?.Trim() ?? string.Empty, out Version declared))
			{
				declaredTargets.Add(declared);
			}
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
		// 读不到或解析失败都在内部记日志并返回 null,不抛异常。0.111.0–0.113.0 的查找路径一致。
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
