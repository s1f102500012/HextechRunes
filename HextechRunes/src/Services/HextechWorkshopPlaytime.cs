using MegaCrit.Sts2.Core.Platform.Steam;
using Steamworks;

namespace HextechRunes;

/// <summary>
/// 向 Steam 上报创意工坊物品的游玩时长与会话数(工坊统计页的 Playtime / Sessions)。
/// 游戏在加载模组之前就完成了 Steam 初始化(NGame.InitializePlatform 先于 ModManager.Initialize)，
/// 所以在模组入口调用即可；游戏退出时 Steam 自动结束计时，不需要 Stop。
/// </summary>
internal static class HextechWorkshopPlaytime
{
	internal const ulong HextechRunesItemId = 3747501308UL;

	private static readonly object TrackLock = new();
	private static readonly HashSet<ulong> StartedItemIds = [];
	// CallResult 只在被引用期间接收回调，回调到达前不能被回收。
	private static readonly List<IDisposable> PendingResults = [];

	internal static void Start(ulong itemId)
	{
		lock (TrackLock)
		{
			if (!StartedItemIds.Add(itemId))
			{
				return;
			}
		}

		try
		{
			if (!SteamInitializer.Initialized)
			{
				HextechLog.Info("Playtime", $"Steam is not initialized; workshop playtime for {itemId} is not reported.");
				return;
			}

			PublishedFileId_t[] items = [new PublishedFileId_t(itemId)];
			SteamAPICall_t call = SteamUGC.StartPlaytimeTracking(items, (uint)items.Length);
			if (call == SteamAPICall_t.Invalid)
			{
				HextechLog.Warn("Playtime", $"Steam rejected workshop playtime tracking for {itemId}.");
				return;
			}

			CallResult<StartPlaytimeTrackingResult_t>? callResult = null;
			callResult = CallResult<StartPlaytimeTrackingResult_t>.Create((result, ioFailure) =>
			{
				lock (TrackLock)
				{
					PendingResults.Remove(callResult!);
				}

				if (ioFailure || result.m_eResult != EResult.k_EResultOK)
				{
					HextechLog.Warn("Playtime", $"Workshop playtime tracking failed for {itemId}: {(ioFailure ? "IO failure" : result.m_eResult.ToString())}");
				}
				else
				{
					HextechLog.Info("Playtime", $"Steam is counting workshop playtime for {itemId}.");
				}
			});
			lock (TrackLock)
			{
				PendingResults.Add(callResult);
			}

			callResult.Set(call);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Playtime", $"Workshop playtime for {itemId} could not be reported: {ex.Message}");
		}
	}
}
