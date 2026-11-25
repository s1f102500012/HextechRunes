using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

// 0.109+:ModelIdSerializationCache.Init 从 ModelDb.All 统一排序、编号并散列 SavedProperty,
// 模组只需保证载体能被 ModelDb 发现;Init 前调 CacheSavedPropertiesForTypeDebug 会提前写表且绕过散列,
// Init 后追加会破坏已发布的 wire 布局,因此这里绝不主动注入。
internal static partial class HextechSavedPropertyBootstrap
{
	private const string RegistrationFreezePointName = "ModelIdSerializationCache.Init";

	private static readonly FieldInfo? OfficialCacheInitializedField = TryGetField(
		typeof(SavedPropertiesTypeCache),
		"_initialized",
		BindingFlags.NonPublic | BindingFlags.Static,
		warnIfMissing: false);

	private static bool _officialCacheAudited;

	private static bool IsRegistrationWindowClosed()
	{
		if (OfficialCacheInitializedField?.GetValue(null) is bool initialized)
		{
			return initialized;
		}

		throw new InvalidOperationException(
			$"[{ModInfo.Id}] Could not read ModelIdSerializationCache._initialized; external model registration was rejected to avoid corrupting the SavedProperty net-id map.");
	}

	private static void InjectModelTypeCore(Type type)
	{
		// 只做窗口校验(已在 InjectModelType 里完成),不注入。
	}

	internal static void InjectCaches()
	{
		HextechLog.Info("Mayhem", "SavedProperty injection skipped: on 0.109+ ModelIdSerializationCache.Init collects ModelDb carriers itself.");
	}

	/// <summary>
	/// 载体自检:官方 Init 在启动状态机里填表,启动期自检必误报全量,推迟到首个跑局开始/读档时跑一次。
	/// 此时拓展包的延迟注册也已完成,能抓到"包侧新增 [SavedProperty] 载体却忘了走 API 注册"的漏项。
	/// </summary>
	internal static void RunOfficialCacheAuditOnce()
	{
		if (_officialCacheAudited)
		{
			return;
		}

		_officialCacheAudited = true;
		try
		{
			WarnOnUninjectedSavedPropertyCarriers();
			HextechLog.Info("MultiplayerCompat", $"SavedProperty net-id map is game-canonical: bitSize={SavedPropertiesTypeCache.PropertyIdBitSize} hash={SavedPropertiesTypeCache.Hash:X8}.");
		}
		catch (Exception ex)
		{
			HextechLog.Warn("MultiplayerCompat", $"SavedProperty post-init audit failed: {ex.GetType().Name}: {ex.Message}");
		}
	}
}
