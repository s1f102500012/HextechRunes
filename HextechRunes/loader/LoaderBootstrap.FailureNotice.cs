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
		UnsupportedGameVersion,
	}

	// HRESULT_FROM_WIN32(ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION):"应用程序控制策略已阻止此文件",
	// 智能应用控制 / WDAC 拦截未签名 DLL 时 FileLoadException 带的就是它。
	private const int ApplicationControlBlockedHResult = unchecked((int)0x800711C7);

	// 本体失败时由本体弹窗;拓展包看到这个标记就不再重复弹。两个加载器在同一进程,用 AppDomain 数据交换。
	private const string MainLoaderFailedDataKey = "HextechRunes.Loader.Failed";

	private const string FailureNoticeHarmonyId = "Natsuki." + ModId + ".Loader.FailureNotice";

	private static LoadFailureKind? _loadFailure;
	private static string? _hostVersionLabel;
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

	// 正文不重复模组名(标题已有),用"本模组"/"拓展包"指代;{0} 是游戏版本,只在版本过旧的正文里出现。
	// 缺失的语言回退英文。拓展包缺本体的正文里写的是各语言的本体名称。
	private sealed record NoticeText(
		string Title,
		string Blocked,
		string Other,
		string UnsupportedGameVersion,
		string RequiredModMissing);

	private const string FallbackLanguage = "eng";

	private static readonly NoticeText SpanishNotice = new(
		"No se pudo cargar {0}",
		"El Control inteligente de aplicaciones de Windows bloqueó el archivo del programa del mod, así que su contenido no se ha cargado en esta sesión y las partidas multijugador fallarán por datos no coincidentes.\n\nPara usar el mod, desactiva el Control inteligente de aplicaciones en Seguridad de Windows y reinicia el juego.",
		"No se pudo cargar el archivo del programa del mod, así que su contenido no se ha cargado en esta sesión y las partidas multijugador fallarán por datos no coincidentes.\n\nEnvía el registro del juego al autor del mod.",
		"Este mod no es compatible con la versión actual del juego ({0}), así que su contenido no se ha cargado en esta sesión.\n\nActualiza el juego a la versión más reciente y reinícialo.",
		"La expansión necesita el mod principal ARAM: Caos, pero no se cargó, así que la expansión está desactivada.\n\nAsegúrate de tener también activado el mod principal.");

	private static readonly Dictionary<string, NoticeText> NoticeTexts = new(StringComparer.Ordinal)
	{
		["zhs"] = new(
			"{0}未能加载",
			"Windows 智能应用控制阻止了本模组的程序文件，本次游戏没有加载它的内容，联机时也会因数据不一致而无法加入房间。\n\n如需使用，请在 Windows 安全中心关闭智能应用控制后重启游戏。",
			"本模组的程序文件加载失败，本次游戏没有加载它的内容，联机时也会因数据不一致而无法加入房间。\n\n请把游戏日志发给模组作者。",
			"本模组不支持当前的游戏版本（{0}），本次游戏没有加载它的内容。\n\n请把游戏更新到最新版本后重启。",
			"拓展包需要海克斯大乱斗本体，但本体没有加载成功，拓展包内容已停用。\n\n请确认已同时启用本体。"),
		["eng"] = new(
			"{0} was not loaded",
			"Windows Smart App Control blocked the mod's program file, so its content is not loaded in this session and multiplayer lobbies will fail with a data mismatch.\n\nTo use the mod, turn off Smart App Control in Windows Security and restart the game.",
			"The mod's program file failed to load, so its content is not loaded in this session and multiplayer lobbies will fail with a data mismatch.\n\nPlease send your game log to the mod author.",
			"This mod does not support the current game version ({0}), so its content is not loaded in this session.\n\nPlease update the game to the latest version and restart.",
			"The expansion requires the ARAM: Mayhem main mod, but the main mod did not load, so the expansion is disabled.\n\nMake sure the main mod is enabled as well."),
		["jpn"] = new(
			"{0}を読み込めませんでした",
			"Windows のスマート アプリ コントロールによって MOD のプログラム ファイルがブロックされたため、今回のゲームでは内容が読み込まれていません。マルチプレイでもデータの不一致によりロビーに参加できません。\n\n使用するには、Windows セキュリティでスマート アプリ コントロールをオフにしてから、ゲームを再起動してください。",
			"MOD のプログラム ファイルを読み込めなかったため、今回のゲームでは内容が読み込まれていません。マルチプレイでもデータの不一致によりロビーに参加できません。\n\nゲームのログを MOD の作者に送ってください。",
			"この MOD は現在のゲーム バージョン（{0}）に対応していないため、今回のゲームでは内容が読み込まれていません。\n\nゲームを最新バージョンに更新してから再起動してください。",
			"この拡張パックにはランダムミッド：メイヘム本体が必要ですが、本体が読み込まれなかったため、拡張パックの内容は無効になっています。\n\n本体も有効になっているか確認してください。"),
		["kor"] = new(
			"{0}을(를) 불러오지 못했습니다",
			"Windows 스마트 앱 컨트롤이 모드의 프로그램 파일을 차단하여 이번 게임에서는 모드 콘텐츠가 로드되지 않았습니다. 멀티플레이에서도 데이터 불일치로 로비에 참가할 수 없습니다.\n\n모드를 사용하려면 Windows 보안에서 스마트 앱 컨트롤을 끈 뒤 게임을 다시 시작하세요.",
			"모드의 프로그램 파일을 불러오지 못해 이번 게임에서는 모드 콘텐츠가 로드되지 않았습니다. 멀티플레이에서도 데이터 불일치로 로비에 참가할 수 없습니다.\n\n게임 로그를 모드 제작자에게 보내 주세요.",
			"이 모드는 현재 게임 버전({0})을 지원하지 않아 이번 게임에서는 모드 콘텐츠가 로드되지 않았습니다.\n\n게임을 최신 버전으로 업데이트한 뒤 다시 시작하세요.",
			"이 확장팩을 사용하려면 무작위 총력전: 아수라장 본체가 필요하지만, 본체가 로드되지 않아 확장팩 콘텐츠가 비활성화되었습니다.\n\n본체도 함께 활성화했는지 확인하세요."),
		["spa"] = SpanishNotice,
		["esp"] = SpanishNotice,
		["ptb"] = new(
			"Não foi possível carregar {0}",
			"O Controle Inteligente de Aplicativos do Windows bloqueou o arquivo de programa do mod, então o conteúdo dele não foi carregado nesta sessão e as partidas multijogador vão falhar por dados incompatíveis.\n\nPara usar o mod, desative o Controle Inteligente de Aplicativos em Segurança do Windows e reinicie o jogo.",
			"Não foi possível carregar o arquivo de programa do mod, então o conteúdo dele não foi carregado nesta sessão e as partidas multijogador vão falhar por dados incompatíveis.\n\nEnvie o log do jogo para o autor do mod.",
			"Este mod não é compatível com a versão atual do jogo ({0}), então o conteúdo dele não foi carregado nesta sessão.\n\nAtualize o jogo para a versão mais recente e reinicie.",
			"A expansão precisa do mod principal ARAM: Desordem, mas ele não foi carregado, então a expansão está desativada.\n\nVerifique se o mod principal também está ativado."),
		["rus"] = new(
			"Не удалось загрузить {0}",
			"Интеллектуальное управление приложениями Windows заблокировало программный файл мода, поэтому его содержимое не загружено в этой сессии, а подключение к лобби в мультиплеере завершится ошибкой из-за несовпадения данных.\n\nЧтобы использовать мод, отключите интеллектуальное управление приложениями в разделе «Безопасность Windows» и перезапустите игру.",
			"Не удалось загрузить программный файл мода, поэтому его содержимое не загружено в этой сессии, а подключение к лобби в мультиплеере завершится ошибкой из-за несовпадения данных.\n\nОтправьте журнал игры автору мода.",
			"Мод не поддерживает текущую версию игры ({0}), поэтому его содержимое не загружено в этой сессии.\n\nОбновите игру до последней версии и перезапустите её.",
			"Для расширения нужен основной мод ARAM: Mayhem, но он не загрузился, поэтому расширение отключено.\n\nУбедитесь, что основной мод тоже включён."),
		["tha"] = new(
			"ไม่สามารถโหลด {0} ได้",
			"การควบคุมแอปอัจฉริยะ (Smart App Control) ของ Windows บล็อกไฟล์โปรแกรมของม็อด เนื้อหาของม็อดจึงไม่ถูกโหลดในการเล่นครั้งนี้ และการเข้าร่วมห้องผู้เล่นหลายคนจะล้มเหลวเพราะข้อมูลไม่ตรงกัน\n\nหากต้องการใช้ม็อด ให้ปิดการควบคุมแอปอัจฉริยะในความปลอดภัยของ Windows แล้วเริ่มเกมใหม่",
			"ไม่สามารถโหลดไฟล์โปรแกรมของม็อดได้ เนื้อหาของม็อดจึงไม่ถูกโหลดในการเล่นครั้งนี้ และการเข้าร่วมห้องผู้เล่นหลายคนจะล้มเหลวเพราะข้อมูลไม่ตรงกัน\n\nโปรดส่งไฟล์บันทึกของเกมให้ผู้สร้างม็อด",
			"ม็อดนี้ไม่รองรับเกมเวอร์ชันปัจจุบัน ({0}) เนื้อหาของม็อดจึงไม่ถูกโหลดในการเล่นครั้งนี้\n\nโปรดอัปเดตเกมเป็นเวอร์ชันล่าสุดแล้วเริ่มเกมใหม่",
			"แพ็กเสริมนี้ต้องใช้ม็อดหลัก ARAM: Mayhem แต่ม็อดหลักไม่ได้ถูกโหลด แพ็กเสริมจึงถูกปิดใช้งาน\n\nโปรดตรวจสอบว่าเปิดใช้ม็อดหลักไว้ด้วย"),
	};

	internal static (string Title, string Body) BuildFailureNotice(LoadFailureKind kind, string? language, string? gameVersion)
	{
		string lang = language != null && NoticeTexts.ContainsKey(language) && ModDisplayNames.ContainsKey(language)
			? language
			: FallbackLanguage;
		NoticeText text = NoticeTexts[lang];
		string body = kind switch
		{
			LoadFailureKind.ApplicationControlBlocked => text.Blocked,
			LoadFailureKind.UnsupportedGameVersion => string.Format(text.UnsupportedGameVersion, gameVersion ?? "?"),
			LoadFailureKind.RequiredModMissing => text.RequiredModMissing,
			_ => text.Other,
		};
		return (string.Format(text.Title, ModDisplayNames[lang]), body);
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

		(string title, string body) = BuildFailureNotice(kind, LocManager.Instance?.Language, _hostVersionLabel);
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
