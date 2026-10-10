using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

#if HEXTECH_SPONSOR_LOADER
namespace HextechRunesSponsorPack.Loader;
#else
namespace HextechRunes.Loader;
#endif

// 变体没能加载时,游戏仍把本模组记为已加载,玩家只会在联机时看到 ModelDb 哈希不一致。
// 这里在主菜单弹一次原版错误弹窗说明情况。文字写死在加载器里:变体没加载,模组自己的本地化也不可用。
public static partial class LoaderBootstrap
{
	internal enum LoadFailureKind
	{
		Other,
		ApplicationControlBlocked,
		RequiredModMissing,
	}

	// HRESULT_FROM_WIN32(ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION):"应用程序控制策略已阻止此文件",
	// 智能应用控制 / WDAC 拦截未签名 DLL 时 FileLoadException 带的就是它。
	private const int ApplicationControlBlockedHResult = unchecked((int)0x800711C7);

	// 本体失败时由本体弹窗;拓展包看到这个标记就不再重复弹。两个加载器在同一进程,用 AppDomain 数据交换。
	private const string MainLoaderFailedDataKey = "HextechRunes.Loader.Failed";

	private const string FailureNoticeHarmonyId = "Natsuki." + ModId + ".Loader.FailureNotice";

	private static LoadFailureKind? _loadFailure;
	private static bool _failureNoticePatched;
	private static bool _failureNoticeShown;

	internal static LoadFailureKind ClassifyLoadFailure(Exception exception)
	{
		for (Exception? current = exception; current != null; current = current.InnerException)
		{
			if (current.HResult == ApplicationControlBlockedHResult)
			{
				return LoadFailureKind.ApplicationControlBlocked;
			}
		}

		return LoadFailureKind.Other;
	}

	internal static (string Title, string Body) BuildFailureNotice(LoadFailureKind kind, bool chinese)
	{
		string name = chinese ? ModDisplayNameZh : ModDisplayNameEn;
		return kind switch
		{
			LoadFailureKind.ApplicationControlBlocked => chinese
				? ($"{name}未能加载",
					$"Windows 智能应用控制阻止了{name}的程序文件，本次游戏没有加载它的内容，联机时也会因数据不一致而无法加入房间。\n\n" +
					"如需使用，请在 Windows 安全中心关闭智能应用控制后重启游戏。")
				: ($"{name} was not loaded",
					$"Windows Smart App Control blocked {name}'s program file, so its content is not loaded in this session " +
					"and multiplayer lobbies will fail with a data mismatch.\n\n" +
					"To use the mod, turn off Smart App Control in Windows Security and restart the game."),
			LoadFailureKind.RequiredModMissing => chinese
				? ($"{name}未能加载",
					$"{name}需要海克斯大乱斗本体，但本体没有加载成功，拓展包内容已停用。\n\n请确认已同时启用本体。")
				: ($"{name} was not loaded",
					$"{name} requires ARAM: Mayhem, but the main mod did not load, so the expansion is disabled.\n\n" +
					"Make sure the main mod is enabled as well."),
			_ => chinese
				? ($"{name}未能加载",
					$"{name}的程序文件加载失败，本次游戏没有加载它的内容，联机时也会因数据不一致而无法加入房间。\n\n" +
					"请把游戏日志发给模组作者。")
				: ($"{name} was not loaded",
					$"{name}'s program file failed to load, so its content is not loaded in this session " +
					"and multiplayer lobbies will fail with a data mismatch.\n\n" +
					"Please send your game log to the mod author."),
		};
	}

	private static void ReportLoadFailure(LoadFailureKind kind)
	{
		_loadFailure = kind;
		if (RequiredAssemblyName == null)
		{
			AppDomain.CurrentDomain.SetData(MainLoaderFailedDataKey, true);
		}

		if (_failureNoticePatched)
		{
			return;
		}

		try
		{
			new Harmony(FailureNoticeHarmonyId).Patch(
				AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)),
				postfix: new HarmonyMethod(typeof(LoaderBootstrap), nameof(MainMenuReadyPostfix)));
			_failureNoticePatched = true;
		}
		catch (Exception exception)
		{
			Log.Warn($"{LogPrefix}Could not install the load-failure notice: {exception.GetBaseException().Message}");
		}
	}

	private static void ClearLoadFailure()
	{
		_loadFailure = null;
		if (RequiredAssemblyName == null)
		{
			AppDomain.CurrentDomain.SetData(MainLoaderFailedDataKey, null);
		}
	}

	private static void MainMenuReadyPostfix()
	{
		if (_failureNoticeShown || _loadFailure is not { } kind)
		{
			return;
		}

		// 本体已经弹过窗说明了,拓展包不再追加一个。
		if (kind == LoadFailureKind.RequiredModMissing
			&& AppDomain.CurrentDomain.GetData(MainLoaderFailedDataKey) is true)
		{
			return;
		}

		NModalContainer? container = NModalContainer.Instance;
		if (container == null || container.OpenModal != null)
		{
			// 已有别的弹窗时原版会拒绝再加;留到下次回到主菜单再试。
			return;
		}

		(string title, string body) = BuildFailureNotice(kind, LocManager.Instance?.Language == "zhs");
		NErrorPopup? popup = NErrorPopup.Create(title, body, showReportBugButton: false);
		if (popup == null)
		{
			return;
		}

		container.Add(popup);
		_failureNoticeShown = true;
		Log.Info($"{LogPrefix}Showed load-failure notice ({kind}).");
	}
}
