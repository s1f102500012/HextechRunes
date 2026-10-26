using Godot;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

// 信号噪声（见 HextechSignalNoise）：只改这一格的外观，_relics 不变；点这一格的刷新只在本地还原外观，
// 不调 _rerollFunc、不计刷新次数与历史，所以联机两端的选项与刷新状态都不受影响。
internal sealed partial class HextechRuneSelectionScreen
{
	private const float NoiseFlickerMinAlpha = 0.55f;

	private HextechSignalNoise.Noise? _signalNoise;
	private static Font? _noiseFragmentFont;

	private bool CanShowSignalNoise =>
		!_enemyOnly
		&& !SelfPickMode
		&& _rerollFunc != null
		&& _metadataMode == HextechSelectionMetadataMode.PlayerRune
		&& _relics.Count > 0;

	private void RollSignalNoise()
	{
		if (!CanShowSignalNoise)
		{
			return;
		}

		HextechSignalNoise.RegisterScreen(this);
		_signalNoise = HextechSignalNoise.TryRoll(_relics.Count);
		if (_signalNoise is { } noise)
		{
			HextechLog.Info("SignalNoise", $"SelectionScreen: noise on slot={noise.Slot} latin={noise.Latin}");
		}
	}

	/// <summary>调试指令：把当前界面的一格换成指定片段（已有噪声时替换），选定之后不再生效。</summary>
	internal bool ApplySignalNoiseForDebug(HextechSignalNoise.DebugRequest request)
	{
		if (_choiceLocked || !CanShowSignalNoise || _cardsRow == null)
		{
			return false;
		}

		_signalNoise = HextechSignalNoise.Resolve(_relics.Count, request);
		RestartSelectionConfirmGuard();
		RebuildCards();
		HextechLog.Info("SignalNoise", $"SelectionScreen: debug noise on slot={_signalNoise.Value.Slot} latin={_signalNoise.Value.Latin}");
		return true;
	}

	private bool IsNoiseSlot(int slotIndex)
	{
		return _signalNoise is { } noise && noise.Slot == slotIndex;
	}

	/// <summary>点噪声格的刷新：还原成真实选项的外观并返回 true；其余情况交回正常刷新。</summary>
	private bool TryRestoreNoise(int slotIndex)
	{
		if (!IsNoiseSlot(slotIndex))
		{
			return false;
		}

		bool restoreControllerFocus = slotIndex < _rerollButtons.Count && _rerollButtons[slotIndex].HasFocus();
		_signalNoise = null;
		SfxCmd.Play(HextechSignalNoise.RestoreSfx);
		RestartSelectionConfirmGuard();
		List<CardVfxAttachment> keptVfx = DetachCardVfx(slotIndex);
		RebuildCards();
		ReattachCardVfx(keptVfx);
		PlayRerollVfx(slotIndex, goldenReroll: false);
		if (restoreControllerFocus)
		{
			RestorePlayerRerollFocus(slotIndex);
		}

		return true;
	}

	private Button CreateNoiseCardButton(RelicModel relic, int slotIndex, HextechSignalNoise.Noise noise)
	{
		string rarityKey = DetermineCardRarityKey(relic, _metadataMode);
		Texture2D? cardFrameTexture = GetCardFrameTexture(rarityKey);
		bool useImageFrame = cardFrameTexture != null;
		Color border = useImageFrame ? new Color(0f, 0f, 0f, 0f) : new Color(0.55f, 0.58f, 0.62f, 0.8f);
		Button button = new()
		{
			Name = $"Noise_{slotIndex}_Card",
			CustomMinimumSize = PlayerRuneCardSize,
			Text = string.Empty,
			FocusMode = FocusModeEnum.All,
			MouseDefaultCursorShape = CursorShape.Forbidden,
			ClipContents = useImageFrame
		};
		StyleBoxFlat style = CreateCardStyle(new Color(0.05f, 0.06f, 0.08f, 0.86f), border, useImageFrame ? 0 : 2, 0.18f);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
		{
			button.AddThemeStyleboxOverride(state, style);
		}

		if (cardFrameTexture != null)
		{
			button.AddChild(CreateCardFrameOverlay(cardFrameTexture));
		}

		MarginContainer margin = new();
		margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", useImageFrame ? 40 : 22);
		margin.AddThemeConstantOverride("margin_right", useImageFrame ? 40 : 22);
		margin.AddThemeConstantOverride("margin_top", useImageFrame ? 32 : 22);
		margin.AddThemeConstantOverride("margin_bottom", PlayerRuneCardBottomMargin);
		button.AddChild(margin);

		VBoxContainer content = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		content.AddThemeConstantOverride("separation", 14);
		margin.AddChild(content);

		// 图标位留空，保持与正常卡片相同的版面高度。
		content.AddChild(new Control
		{
			CustomMinimumSize = new Vector2(0f, 176f),
			MouseFilter = MouseFilterEnum.Ignore
		});

		MegaLabel title = new()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Off,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MaxFontSize = 28,
			MinFontSize = 18
		};
		HextechUiTheme.ApplyDefaultMegaLabelTheme(title);
		title.Modulate = new Color(0.82f, 0.84f, 0.86f, 0.95f);
		title.SetTextAutoSize(noise.Title);
		content.AddChild(title);

		MegaRichTextLabel body = CreateDescriptionLabel();
		body.HorizontalAlignment = HorizontalAlignment.Center;
		body.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
		if (GetNoiseFragmentFont() is { } font)
		{
			body.AddThemeFontOverride("normal_font", font);
		}

		SetFixedDescriptionText(body, noise.Text, noise.FontSize);
		content.AddChild(body);
		SetMouseFilterIgnoreRecursive(margin);
		StartNoiseFlicker(content);

		button.Pressed += () => OnNoiseCardPressed(button, noise);
		return button;
	}

	// 点噪声卡：把这段信号复制到剪贴板，只闪一下，不给文字提示。
	private void OnNoiseCardPressed(Button button, HextechSignalNoise.Noise noise)
	{
		if (_choiceLocked)
		{
			return;
		}

		DisplayServer.ClipboardSet(noise.ClipboardText);
		SfxCmd.Play(HextechSignalNoise.ClickSfx);
		Tween blink = button.CreateTween();
		blink.TweenProperty(button, "modulate:a", 0.35f, 0.05);
		blink.TweenProperty(button, "modulate:a", 1f, 0.12);
		GetViewport()?.SetInputAsHandled();
	}

	private static void StartNoiseFlicker(Control content)
	{
		Tween tween = content.CreateTween().SetLoops();
		tween.TweenProperty(content, "modulate:a", NoiseFlickerMinAlpha, 0.06);
		tween.TweenProperty(content, "modulate:a", 1f, 0.05);
		tween.TweenInterval(1.4);
		tween.TweenProperty(content, "modulate:a", 0.75f, 0.04);
		tween.TweenProperty(content, "modulate:a", 1f, 0.04);
		tween.TweenInterval(2.3);
	}

	private static Font? GetNoiseFragmentFont()
	{
		if (_noiseFragmentFont != null && GodotObject.IsInstanceValid(_noiseFragmentFont))
		{
			return _noiseFragmentFont;
		}

		_noiseFragmentFont = ResourceLoader.Exists(HextechSignalNoise.FragmentFontPath)
			? ResourceLoader.Load<Font>(HextechSignalNoise.FragmentFontPath)
			: null;
		if (_noiseFragmentFont == null)
		{
			HextechLog.Warn("SignalNoise", "Fragment font missing; falling back to the default font.");
		}

		return _noiseFragmentFont;
	}
}
