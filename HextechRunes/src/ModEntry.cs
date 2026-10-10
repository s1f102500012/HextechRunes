using MegaCrit.Sts2.Core.Modding;

namespace HextechRunes;

/// <summary>
/// 模组入口,只做编排:模型注册 → 配置/遥测 → 补丁应用 → 启动摘要。
/// 功能补丁由 <see cref="HextechPatcher"/> 按元数据统一应用；需要先后关系时显式声明 Harmony 顺序约束。
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string HarmonyId = "Natsuki.HextechRunes";

	private static readonly object InitializeLock = new();
	private static Harmony? _harmony;
	private static bool _initialized;

	public static void Initialize()
	{
		lock (InitializeLock)
		{
			if (_initialized)
			{
				HextechLog.Info("Init", "Initialization already completed; skipping duplicate call.");
				return;
			}

			// 先登记模型与 SavedProperty 载体，再安装依赖这些模型的补丁；net-id 冻结在后续启动收尾进行。
			HextechSavedPropertyBootstrap.InjectCaches();
			HextechModelPoolRegistrar.RegisterModels();
			HextechRuneConfiguration.Initialize();
			HextechTelemetry.Initialize();
			HextechIntegratedStrategyEventsCompat.Install();
			// 选择界面特效的数据与贴图在后台预先解码，避免第一次打开选择界面时卡一下。
			HextechKiwiVfxPlayer.BeginWarmup();

			Harmony harmony = _harmony ??= new Harmony(HarmonyId);
			HextechPatcher.ApplyAll(harmony, typeof(ModEntry).Assembly);
			HextechPatcher.LogSummary();
			HextechPatcher.LogSharedPatchTargets(harmony);
			HextechVanillaCopyGuard.Verify(harmony.Id);
			HextechPatcher.DumpIfRequested(harmony);
			_initialized = true;
			// 加载确认行保持始终输出（headless 验证与用户排障都依赖它），不走 verbose 门控:
			// HextechLog.Info 默认关闭,所以这里直接用原版 Log.Info 输出同一前缀格式。
			Log.Info(HextechLog.Format(
				"Init", $"Loaded implementation variant for " +
				$"Slay the Spire 2 compat target {ModInfo.TargetGameVersion}."));
		}
	}
}
