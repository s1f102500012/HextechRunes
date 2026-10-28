using System.Runtime.CompilerServices;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Modding;

namespace HextechRunesSponsorPack;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string PrerequisiteAssemblyName = "HextechRunes";
	private const string HarmonyId = "Natsuki.HextechRunesSponsorPack";
	private const string LogTag = "Init";
	private const ulong WorkshopItemId = 3749708876UL;

	private static readonly object InitializeLock = new();
	private static bool _waitingForPrerequisite;

	// "已尝试"与"已成功"分开记:初始化只尝试一次(重复调用直接返回),
	// 但只有补丁确实全部装上才算成功,失败时不能再输出"Loaded and registered"掩盖问题。
	private static bool _initializationAttempted;
	private static bool _patchesApplied;

	public static void Initialize()
	{
		lock (InitializeLock)
		{
			if (_initializationAttempted)
			{
				SponsorLog.Info(LogTag, $"Initialization already attempted (patches applied: {_patchesApplied}); skipping duplicate call.");
				return;
			}

			// 前置检测按「程序集名 HextechRunes」而非 manifest 的 mod id —— 本体与二创/synergy 版都打包了同名
			// HextechRunes.dll(含 HextechRunesApi),所以两者都能识别(manifest 里已去掉对具体 mod id 的硬依赖)。
			if (IsHextechRunesAssemblyPresent())
			{
				RegisterAll();
				return;
			}

			// 本体可能晚于拓展包载入；通过 AssemblyLoad 延迟注册，但仍须赶在模型注册窗口关闭前完成。
			if (!_waitingForPrerequisite)
			{
				_waitingForPrerequisite = true;
				SponsorLog.Info(LogTag, "HextechRunes assembly not loaded yet; deferring registration until it loads.");
				AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
			}
		}
	}

	private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
	{
		if (!string.Equals(args.LoadedAssembly.GetName().Name, PrerequisiteAssemblyName, StringComparison.Ordinal))
		{
			return;
		}

		lock (InitializeLock)
		{
			AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
			_waitingForPrerequisite = false;

			// 初始化窗口关闭后不再登记内容，避免改变已经冻结的模型与 SavedProperty 布局。
			if (IsModelRegistrationWindowClosed())
			{
				SponsorLog.Warn(LogTag, "HextechRunes 加载过晚(模型注册窗口已关闭),拓展包内容未注册。");
				return;
			}

			RegisterAll();
		}
	}

	// ModManager.State 在全部 mod 的 initializer 跑完之后才置 Initialized / Skipped
	// (public static,0.107.1 第 500/527 行、0.111.0 第 523/550 行),所以"仍是 None"等价于"注册窗口还开着"。
	private static bool IsModelRegistrationWindowClosed()
	{
		return ModManager.State != ModManagerState.None;
	}

	private static void RegisterAll()
	{
		if (_initializationAttempted)
		{
			return;
		}

		_initializationAttempted = true;

		// 注册按功能组隔离(SponsorCatalog.RegisterAll:先依赖后可获得内容,依赖失败的功能整组不入池),失败条目已各自 Warn。
		// 补丁无条件照装:注册不是事务,失败时前面的内容已经入池,此时跳过补丁反而会留下
		// "符文抽得到、依赖的补丁没装"的半初始化状态;每个补丁都以持有对应符文为前提,内容缺席只是空转。
		int failures = SponsorCatalog.RegisterAll();
		if (failures > 0)
		{
			SponsorLog.Warn(LogTag, $"{failures} content registration(s) failed or were skipped; remaining content stays registered and patches are still applied.");
		}

		try
		{
			Harmony harmony = new(HarmonyId);
			SponsorPatcher.ApplyAll(harmony, typeof(ModEntry).Assembly);
			SponsorPatcher.LogSummary();
			_patchesApplied = SponsorPatcher.RequiredFailureCount == 0;
		}
		catch (Exception ex)
		{
			SponsorLog.Error(LogTag, $"Patch application aborted: {ex}");
		}

		StartWorkshopPlaytimeTracking();

		if (_patchesApplied)
		{
			SponsorLog.Info(LogTag, "Loaded and registered HextechRunes sponsor-pack content.");
		}
		else
		{
			SponsorLog.Error(LogTag, "Sponsor-pack content is registered, but required patches did not all apply; affected features stay inactive (see the patch summary above).");
		}
	}

	// 让 Steam 统计拓展包创意工坊物品的游玩时长。本体旧版没有这个 API 时 JIT 会在进入方法时抛
	// MissingMethodException,所以调用单独放在不内联的方法里,由外层捕获,不影响内容注册。
	private static void StartWorkshopPlaytimeTracking()
	{
		try
		{
			StartWorkshopPlaytimeTrackingCore();
		}
		catch (Exception ex)
		{
			SponsorLog.Warn(LogTag, $"Workshop playtime is not reported: {ex.GetType().Name}: {ex.Message}");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StartWorkshopPlaytimeTrackingCore()
	{
		HextechRunesApi.StartWorkshopPlaytimeTracking(WorkshopItemId);
	}

	// 兼容本体与二创(synergy)版:两者都打包了程序集名为 "HextechRunes" 的 dll(暴露同样的 HextechRunesApi)。
	private static bool IsHextechRunesAssemblyPresent()
	{
		return AppDomain.CurrentDomain.GetAssemblies()
			.Any(assembly => string.Equals(assembly.GetName().Name, PrerequisiteAssemblyName, StringComparison.Ordinal));
	}
}
