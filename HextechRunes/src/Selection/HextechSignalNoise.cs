using System.Security.Cryptography;
using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;

namespace HextechRunes;

/// <summary>
/// 选择界面的信号噪声：打开我方海克斯三选一时，以极低概率把其中一个选项的外观换成噪声卡，点该位置的刷新即还原（不消耗次数）。
/// 纯本地表现：用本机随机数决定，不碰共享随机流，不进存档，不发联机消息，底下的真实选项始终不变。
/// 中文游戏语言给汉字片段，其余语言给拉丁片段；片段文本在密文里给出，这里不存原文；导入配置时的口令只存哈希。
/// </summary>
internal static class HextechSignalNoise
{
	private const string LogTag = "SignalNoise";
	private const string ProgressFileName = "signal_noise.json";
	private static readonly string[] HanLanguages = ["zhs", "zht"];
	private const double TriggerChance = 0.001;

	/// <summary>本机调试：设为 1 时每次打开都出噪声，片段按顺序轮换，不受每局一次的限制。</summary>
	private const string DebugEnvironmentVariable = "HEXTECH_SIGNAL_NOISE_DEBUG";

	/// <summary>噪声描述用原版自带的等宽字体：中文字体缺其中几个符号，缺字时 Godot 回退到各平台不同的系统字体。</summary>
	internal const string FragmentFontPath = "res://fonts/source_code_pro_medium.ttf";

	internal const string ClickSfx = "event:/sfx/ui/timeline/ui_timeline_hover_locked";
	internal const string RestoreSfx = "event:/sfx/ui/cards/card_transform";

	private const int EchoLineCount = 4;
	private const int CanvasLayerIndex = 120;
	private const int EchoFontSize = 26;
	private const double EchoLineDelaySeconds = 1.6;
	private const double EchoFadeInSeconds = 0.6;
	private const double EchoHoldSeconds = 6.0;
	private const double EchoFadeOutSeconds = 1.2;

	internal const int HanFragmentFontSize = 20;
	internal const int LatinFragmentFontSize = 18;

	internal static readonly string[] HanFragments =
	[
		"Φ╝Öµ▒╛ΦÜ┐Ω¡╜µåªΦèìµ▓ü",
		"ΩòƒΘå▓Θ╜┴Φ▒öτÄÿτÅ│Φ₧╝",
		"Φ₧┴ΦÉ║∞èÉΦ┐âΦÉåδ└└Φôæ",
		"δ▒òφ«¼ΩîöΘèƒΩÜ┤Θ⌐òδ»Å",
		"Ω╛─φåƒΩ¬î∞öÆφ£ö∞¼¥Ω│ó",
		"δö¼δå─εïûδ┼ëδÆ£δ╢₧∞╣½",
		"∞╛«∞çª≡╣┐∞ùª∩Æûφ┤┬∞╞₧",
		"∩│ôφÉïε░▓φÉêε₧▒≡òÄε½á",
		"≥┤íε¡ìε╕¢≥¼ùεÖ┬∩á░ε╞║",
		"≡»╞∩Ö║≥½è≡⌐¥≡á╖≤╖î≡¼╣",
		"±ú║⌠¿⌐±ô¢≤┐░≤╚╣≡ª⌐≤╚╖",
		"⌠╝ì⌠║╩≥╣░±╜î≡╔¿±╚ö≥»ù"
	];

	internal static readonly string[] LatinFragments =
	[
		"╕╙┬╩╒╔╘ü├╞┌╨╧┼ü─╨╓╧╒╩╧╚ü╤┬╩╧╒╞┼ü╨╧ü╒╔╞ü┼╞─╠ì",
		"¬╟┌╚╬├╧╟é╤╚é─╬╫╟é├╨╞é╪╟╨╤╧é╔╘╟╟╨é╦╨é┼╩╟┼═Ä",
		"▓╧╟â╓╞╒╥╧╧╓â╪╤╒╥╧╧╚╟â─â╫╦╥╪╓─╤╟â▄╚─╒╓â╫╥╥â╧─╫╚Å",
		"ª╙╥╔╫ä╞┘═╨╪ä╪╙ä┼╓╤═╔╫Éä╞╙┘╥╚ä╞▌ä┼ä╥╔█ä╘┼╟╪ï╫ä█╔═╦╠╪É",
		"║╙┘╘╤╔à╫╞╫╩à┘╫╩╞╪┌╫╩╪à═╘╞╫╔╩╔à╘┌┘à╘╦à╪╬╠═┘æ",
		"»╘å╪█╧╘å┌╬╦å╪╒┌┌╦╘å╬╦╟╪┌å╔╪█╙╚╥╦┘å┌╒å╚╥╧═╬┌Æ",
		"│╠╒█ç╚ç▐╧╚╙╠Ä┌ç┌╓╒╬ôç█╧╠ç╦╠╚╦ç┘╠╬╚╨╒ç█╧╠╨┘ç▐╨╙╙ô",
		"╝╨╫┌╓█ê╬╔╘╘ê▄╫ê╩╘╔╠═█ê▄╨╔▄ê█▄╔╓╠ê▌╓╒╫▐╤╓╧ê█▄╤╘╘ö",
		"╜╤╬ë╤╥╨╤ë╠╪╒═ëÇ╥╫═ë╦╒╪Ç▄ë▌╤█╪▐╨╤ë▌╤╬ë▄╤╩═╪Ç▄ë╪╒═ò",
		"▓╧▄╧è┘╪è▐╥╧è▌▐╦╙▄ûè▐╥╧è▌┘▀╓ù▌▐╦╙╪╧╬è╠╓┘┘╬è▄▀╪▌è═┘╓╬û",
		"┤┘ï╨┘╧╫╨▐▐ï╬╘▌╬╫╨▐ïé╘┘ï╠┘╧ï╫┌▐▐ï▌╨ü┌╫ü╨ù",
		"┐█îâ╘█î▀╤Çîü▄îÇ╘╤î╙═┘╤îà█üî╧═┘╤îÇ█î▀█╪é╤½"
	];

	private static readonly string[] Numerals = ["Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ", "Ⅶ", "Ⅷ", "Ⅸ", "Ⅹ", "Ⅺ", "Ⅻ"];

	private static readonly string[] EchoHashes =
	[
		"f2e40fc1edb72ee9ed58fd7076934ed03eefc0116b9d90b32c0bcf381b2e788f",
		"379917c784f311ada5171d439c2a8331d87c72f1dc09ade2f7c99d334c1437ec",
		"13d0f8ac56dda7188cc147dbdb3cd65894b97397069e81f1a71144e7b492cb78"
	];

	private static readonly Random LocalRandom = new();
	private static readonly HashSet<string> RunsWithNoise = new(StringComparer.Ordinal);
	private static int _debugCursor;
	private static DebugRequest? _pendingDebug;
	private static bool? _slain;
	private static WeakReference<HextechRuneSelectionScreen>? _currentScreen;

	/// <summary>调试指令的请求：句序（0 起）与语言为空时分别按轮换、按游戏语言决定。</summary>
	internal readonly record struct DebugRequest(int? Fragment, bool? Latin);

	internal readonly record struct Noise(int Slot, bool Latin, int Fragment)
	{
		internal string Title => $"▓▒ {Numerals[Fragment]} ▒▓";

		internal string Text => (Latin ? LatinFragments : HanFragments)[Fragment];

		internal int FontSize => Latin ? LatinFragmentFontSize : HanFragmentFontSize;

		// 点噪声卡时复制到剪贴板：仿配置码的样子（真配置码以 HEXCFG1: 开头），带上片段序号，免得玩家手抄 CP437 符号。
		internal string ClipboardText => $"HEXCFG?:{Numerals[Fragment]}:{Text}";
	}

	/// <summary>每局最多一次。</summary>
	internal static Noise? TryRoll(int optionCount)
	{
		if (optionCount <= 0)
		{
			return null;
		}

		if (_pendingDebug is { } pending)
		{
			_pendingDebug = null;
			return Resolve(optionCount, pending);
		}

		bool debug = IsDebugForced();
		if (!debug)
		{
			// 弑神之后信号断了，不再出现。
			if (HasSlain()
				|| GetRunKey() is not string runKey
				|| RunsWithNoise.Contains(runKey)
				|| LocalRandom.NextDouble() >= TriggerChance)
			{
				return null;
			}

			RunsWithNoise.Add(runKey);
		}

		bool latin = IsLatinLanguage();
		int count = latin ? LatinFragments.Length : HanFragments.Length;
		int fragment = debug ? _debugCursor++ % count : PickFragment(latin, count);
		MarkSeen(latin, fragment);
		return new Noise(LocalRandom.Next(optionCount), latin, fragment);
	}

	internal static Noise Resolve(int optionCount, DebugRequest request)
	{
		bool latin = request.Latin ?? IsLatinLanguage();
		int count = latin ? LatinFragments.Length : HanFragments.Length;
		int fragment = request.Fragment is int requested ? Math.Clamp(requested, 0, count - 1) : _debugCursor++ % count;
		MarkSeen(latin, fragment);
		return new Noise(LocalRandom.Next(optionCount), latin, fragment);
	}

	internal static void RegisterScreen(HextechRuneSelectionScreen screen)
	{
		_currentScreen = new WeakReference<HextechRuneSelectionScreen>(screen);
	}

	internal static void UnregisterScreen(HextechRuneSelectionScreen screen)
	{
		if (_currentScreen != null
			&& _currentScreen.TryGetTarget(out HextechRuneSelectionScreen? current)
			&& ReferenceEquals(current, screen))
		{
			_currentScreen = null;
		}
	}

	/// <summary>当前开着可出噪声的选择界面就立即应用，否则留给下一次打开的选择界面。</summary>
	internal static bool ForceForDebug(DebugRequest request)
	{
		if (_currentScreen != null
			&& _currentScreen.TryGetTarget(out HextechRuneSelectionScreen? screen)
			&& screen.ApplySignalNoiseForDebug(request))
		{
			return true;
		}

		_pendingDebug = request;
		return false;
	}

	internal static void ClearPendingDebug()
	{
		_pendingDebug = null;
	}

	internal static void ResetProgressForDebug()
	{
		SaveProgress(new SignalNoiseProgress());
		RunsWithNoise.Clear();
		_debugCursor = 0;
		_slain = null;
	}

	internal static void MarkSlain()
	{
		_slain = true;
		SignalNoiseProgress progress = LoadProgress();
		if (!progress.Slain)
		{
			progress.Slain = true;
			SaveProgress(progress);
		}
	}

	/// <summary>本机是否已经弑神：之后信号噪声不再出现，弑神作为纪念在图鉴里显示。</summary>
	internal static bool HasSlain()
	{
		return _slain ??= LoadProgress().Slain;
	}

	internal static string DescribeProgress()
	{
		SignalNoiseProgress progress = LoadProgress();
		return $"han={string.Join(",", progress.Seen.Select(static i => i + 1))} latin={string.Join(",", progress.SeenLatin.Select(static i => i + 1))} echoed={progress.Echoed} slain={progress.Slain} trial={HextechRuneConfiguration.IsGodslayerTrialEnabled()} pending={_pendingDebug != null}";
	}

	/// <summary>导入配置读到剪贴板内容时先过这里；不是口令就交回原导入流程。</summary>
	internal static bool TryEcho(string? input)
	{
		if (string.IsNullOrWhiteSpace(input) || input.Length > 64)
		{
			return false;
		}

		string normalized = string.Join(' ', input.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
		if (!EchoHashes.Contains(hash, StringComparer.Ordinal))
		{
			return false;
		}

		SignalNoiseProgress progress = LoadProgress();
		if (!progress.Echoed)
		{
			progress.Echoed = true;
			SaveProgress(progress);
		}

		HextechLog.Info(LogTag, "Echo received.");
		ShowEcho();
		return true;
	}

	private static int PickFragment(bool latin, int count)
	{
		HashSet<int> seen = SeenList(LoadProgress(), latin).ToHashSet();
		int[] unseen = Enumerable.Range(0, count).Where(index => !seen.Contains(index)).ToArray();
		return unseen.Length > 0
			? unseen[LocalRandom.Next(unseen.Length)]
			: LocalRandom.Next(count);
	}

	private static void MarkSeen(bool latin, int fragment)
	{
		SignalNoiseProgress progress = LoadProgress();
		List<int> seen = SeenList(progress, latin);
		if (seen.Contains(fragment))
		{
			return;
		}

		seen.Add(fragment);
		seen.Sort();
		SaveProgress(progress);
	}

	private static List<int> SeenList(SignalNoiseProgress progress, bool latin)
	{
		return latin ? progress.SeenLatin : progress.Seen;
	}

	private static SignalNoiseProgress LoadProgress()
	{
		return JsonConfigFile.Load(ProgressFileName, LogTag, static () => new SignalNoiseProgress(), static parsed =>
		{
			parsed.Seen = Normalize(parsed.Seen, HanFragments.Length);
			parsed.SeenLatin = Normalize(parsed.SeenLatin, LatinFragments.Length);
			return (parsed, false);
		});
	}

	private static List<int> Normalize(List<int>? seen, int count)
	{
		return (seen ?? [])
			.Where(index => index >= 0 && index < count)
			.Distinct()
			.Order()
			.ToList();
	}

	private static void SaveProgress(SignalNoiseProgress progress)
	{
		JsonConfigFile.Save(ProgressFileName, LogTag, progress);
	}

	private static bool IsLatinLanguage()
	{
		return !HanLanguages.Contains(GetLanguage() ?? string.Empty, StringComparer.Ordinal);
	}

	private static bool IsDebugForced()
	{
		return System.Environment.GetEnvironmentVariable(DebugEnvironmentVariable) == "1";
	}

	private static string? GetLanguage()
	{
		try
		{
			return LocManager.Instance?.Language;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static string? GetRunKey()
	{
		return RunManager.Instance?.DebugOnlyGetState() is RunState runState ? runState.Rng.StringSeed : null;
	}

	private static void ShowEcho()
	{
		if (NGame.Instance?.GetTree()?.Root is not Window root)
		{
			return;
		}

		HextechPresentation.TryRun(LogTag, "Echo display failed", () =>
		{
			CanvasLayer layer = new()
			{
				Name = "HextechSignalEchoLayer",
				Layer = CanvasLayerIndex
			};
			Control frame = new()
			{
				Name = "Frame",
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			VBoxContainer lines = new()
			{
				Name = "Lines",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Alignment = BoxContainer.AlignmentMode.Center,
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = 0.42f,
				AnchorBottom = 0.42f,
				OffsetLeft = -760f,
				OffsetRight = 760f,
				GrowVertical = Control.GrowDirection.Both
			};
			lines.AddThemeConstantOverride("separation", 10);
			frame.AddChild(lines);
			layer.AddChild(frame);
			root.AddChild(layer);

			double lastLineAppears = 0;
			for (int i = 1; i <= EchoLineCount; i++)
			{
				if (HextechRuneLabels.TryGetText($"HEXTECH_SIGNAL_ECHO_{i}") is not { Length: > 0 } text)
				{
					continue;
				}

				double delay = (i - 1) * EchoLineDelaySeconds;
				lastLineAppears = delay;
				AddEchoLine(lines, text, delay);
			}

			Tween cleanup = layer.CreateTween();
			cleanup.TweenInterval(lastLineAppears + EchoFadeInSeconds + EchoHoldSeconds + EchoFadeOutSeconds + 0.2);
			cleanup.TweenCallback(Callable.From(layer.QueueFree));
		});
	}

	private static void AddEchoLine(VBoxContainer lines, string text, double delay)
	{
		MegaRichTextLabel label = new()
		{
			BbcodeEnabled = true,
			FitContent = true,
			AutoSizeEnabled = false,
			ScrollActive = false,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = HextechUiTheme.TransparentWhite
		};
		Font font = label.GetThemeDefaultFont();
		if (font != null)
		{
			label.AddThemeFontOverride("normal_font", font);
		}

		label.AddThemeFontSizeOverride("normal_font_size", EchoFontSize);
		label.AddThemeColorOverride("default_color", new Color(0.93f, 0.9f, 0.82f, 1f));
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
		label.AddThemeConstantOverride("outline_size", 8);
		lines.AddChild(label);
		label.Text = text;

		Tween tween = label.CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(label, "modulate:a", 1f, EchoFadeInSeconds);
		tween.TweenInterval(EchoHoldSeconds);
		tween.TweenProperty(label, "modulate:a", 0f, EchoFadeOutSeconds);
	}
}

internal sealed class SignalNoiseProgress
{
	public bool Slain { get; set; }

	public List<int> Seen { get; set; } = [];

	public List<int> SeenLatin { get; set; } = [];

	public bool Echoed { get; set; }
}
