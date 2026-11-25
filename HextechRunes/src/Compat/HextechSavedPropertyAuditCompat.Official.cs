namespace HextechRunes;

/// <summary>跑局开始/读档时的 SavedProperty 载体自检入口。</summary>
internal static class HextechSavedPropertyAuditCompat
{
	// 0.109 起 net-id 表由游戏启动状态机填写，自检推迟到首个跑局开始或读档时跑一次。
	internal static void RunAuditOnRunStartOnce()
	{
		HextechSavedPropertyBootstrap.RunOfficialCacheAuditOnce();
	}
}
