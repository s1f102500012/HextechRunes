using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

/// <summary>
/// 弑神的终局演出：建筑师事件里玩家出手之后、原版胜利结算之前播放（见 HextechArchitectGodslayerHooks）。
/// 只在本机显示，不改结算与记录；点击或确认推进下一句，停着不动也会按时推进。
/// </summary>
internal static class HextechGodslayerFinale
{
	internal const int LineCount = 6;

	private const string LogTag = "GodslayerFinale";
	private const string LineKeyPrefix = "HEXTECH_GODSLAYER_FINALE_";
	private const int CanvasLayerIndex = 121;
	private const int LineFontSize = 30;
	private const int WhisperFontSize = 22;
	private const float IconSize = 200f;
	private const float IconAnchorY = 0.36f;
	private const double LineFadeInSeconds = 0.6;
	private const double LineFadeOutSeconds = 0.35;
	private const double LineAutoAdvanceSeconds = 3.6;
	private const double WhisperAutoAdvanceSeconds = 4.5;
	private const string NoiseGlyphs = "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐└┴┬├─┼╞╟╚╔╩╦╠═╬╧╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀αßΓπΣσµτΦΘΩδ∞φε∩≡±≥≤⌠⌡÷≈°∙·√ⁿ²■";

	private static readonly Random LocalRandom = new();
	private static bool _playing;

	internal static async Task PlayAsync()
	{
		if (_playing || NGame.Instance?.GetTree() is not SceneTree tree || tree.Root is not Window root)
		{
			return;
		}

		_playing = true;
		try
		{
			await new Sequence(tree, root).RunAsync();
		}
		catch (Exception exception)
		{
			HextechLog.Warn(LogTag, $"Finale failed: {exception}");
		}
		finally
		{
			_playing = false;
		}
	}

	private sealed class Sequence
	{
		private readonly SceneTree _tree;
		private readonly Window _root;
		private readonly CanvasLayer _layer;
		private readonly Control _stage;
		private readonly VBoxContainer _lineBox;
		private readonly Vector2 _viewport;
		private readonly Font? _glyphFont;
		private TaskCompletionSource<bool>? _advance;

		internal Sequence(SceneTree tree, Window root)
		{
			_tree = tree;
			_root = root;
			_viewport = root.GetVisibleRect().Size;
			_glyphFont = ResourceLoader.Exists(HextechSignalNoise.FragmentFontPath)
				? ResourceLoader.Load<Font>(HextechSignalNoise.FragmentFontPath)
				: null;
			_layer = new CanvasLayer { Name = "HextechGodslayerFinale", Layer = CanvasLayerIndex };
			_stage = new Control
			{
				Name = "Stage",
				MouseFilter = Control.MouseFilterEnum.Stop,
				FocusMode = Control.FocusModeEnum.All
			};
			_stage.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_stage.GuiInput += OnInput;
			_lineBox = new VBoxContainer
			{
				Name = "Lines",
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Alignment = BoxContainer.AlignmentMode.Center,
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = 0.62f,
				AnchorBottom = 0.62f,
				OffsetLeft = -640f,
				OffsetRight = 640f,
				GrowVertical = Control.GrowDirection.Both
			};
		}

		internal async Task RunAsync()
		{
			ColorRect dim = new()
			{
				Name = "Dim",
				Color = new Color(0.02f, 0.01f, 0.03f, 0f),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_stage.AddChild(dim);
			_stage.AddChild(_lineBox);
			_layer.AddChild(_stage);
			_root.AddChild(_layer);
			_stage.CallDeferred(Control.MethodName.GrabFocus);
			HextechLog.Info(LogTag, "Finale started.");

			// 命中：白闪，场景压暗；随后几道噪声带撕过画面。
			Flash();
			dim.CreateTween().TweenProperty(dim, "color:a", 0.9f, 0.5);
			await Wait(0.3);
			SfxCmd.Play(HextechSignalNoise.ClickSfx);
			TearNoise(4);
			await Wait(1.0);

			for (int i = 1; i <= 4; i++)
			{
				await ShowLine(i);
			}

			TextureRect? icon = ShowIcon();
			await Wait(0.9);
			await ShowLine(5);
			await Wait(0.5);

			MegaRichTextLabel? whisper = AddLine(LineText(6), WhisperFontSize, new Color(0.78f, 0.74f, 0.7f, 1f));
			if (whisper != null)
			{
				whisper.CreateTween().TweenProperty(whisper, "modulate:a", 1f, LineFadeInSeconds);
				await Wait(LineFadeInSeconds);
				await WaitForAdvance(WhisperAutoAdvanceSeconds);
				whisper.CreateTween().TweenProperty(whisper, "modulate:a", 0f, LineFadeOutSeconds);
			}

			// 弑神碎成噪声字符散开，随后撤掉覆盖层，交回原版结算。
			if (icon != null)
			{
				Shatter(icon);
			}

			SfxCmd.Play(HextechSignalNoise.RestoreSfx);
			await Wait(0.9);
			Tween fadeOut = _stage.CreateTween();
			fadeOut.TweenProperty(_stage, "modulate:a", 0f, 0.6);
			fadeOut.TweenCallback(Callable.From(_layer.QueueFree));
			_stage.MouseFilter = Control.MouseFilterEnum.Ignore;
			HextechLog.Info(LogTag, "Finale finished.");
		}

		private void OnInput(InputEvent inputEvent)
		{
			if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
				|| inputEvent.IsActionPressed("ui_accept"))
			{
				_advance?.TrySetResult(true);
				_stage.AcceptEvent();
			}
		}

		private async Task ShowLine(int index)
		{
			if (AddLine(LineText(index), LineFontSize, new Color(0.93f, 0.9f, 0.82f, 1f)) is not { } label)
			{
				return;
			}

			label.CreateTween().TweenProperty(label, "modulate:a", 1f, LineFadeInSeconds);
			await Wait(LineFadeInSeconds);
			await WaitForAdvance(LineAutoAdvanceSeconds);
			label.CreateTween().TweenProperty(label, "modulate:a", 0f, LineFadeOutSeconds);
			await Wait(LineFadeOutSeconds);
			label.QueueFree();
		}

		private static string? LineText(int index)
		{
			return HextechRuneLabels.TryGetText($"{LineKeyPrefix}{index}");
		}

		private MegaRichTextLabel? AddLine(string? text, int fontSize, Color color)
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}

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
			if (label.GetThemeDefaultFont() is { } font)
			{
				label.AddThemeFontOverride("normal_font", font);
			}

			label.AddThemeFontSizeOverride("normal_font_size", fontSize);
			label.AddThemeColorOverride("default_color", color);
			label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
			label.AddThemeConstantOverride("outline_size", 8);
			_lineBox.AddChild(label);
			label.Text = text;
			return label;
		}

		private TextureRect? ShowIcon()
		{
			RelicModel relic = ModelDb.Relic<GodslayerRune>();
			Texture2D? texture = HextechTextures.IsTextureUsable(relic.BigIcon) ? relic.BigIcon : relic.Icon;
			if (!HextechTextures.IsTextureUsable(texture))
			{
				return null;
			}

			TextureRect icon = new()
			{
				Name = "Godslayer",
				Texture = texture,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = IconAnchorY,
				AnchorBottom = IconAnchorY,
				OffsetLeft = -IconSize / 2f,
				OffsetRight = IconSize / 2f,
				OffsetTop = -IconSize / 2f,
				OffsetBottom = IconSize / 2f,
				PivotOffset = new Vector2(IconSize / 2f, IconSize / 2f),
				Scale = new Vector2(0.85f, 0.85f),
				Modulate = HextechUiTheme.TransparentWhite
			};
			_stage.AddChild(icon);
			Tween appear = icon.CreateTween().SetParallel();
			appear.TweenProperty(icon, "modulate:a", 1f, 0.8);
			appear.TweenProperty(icon, "scale", Vector2.One, 0.8).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
			Tween glow = icon.CreateTween().SetLoops();
			glow.TweenProperty(icon, "self_modulate", new Color(1.3f, 1.25f, 1.4f, 1f), 0.9);
			glow.TweenProperty(icon, "self_modulate", Colors.White, 0.9);
			return icon;
		}

		private void Shatter(TextureRect icon)
		{
			Vector2 center = new(_viewport.X * 0.5f, _viewport.Y * IconAnchorY);
			Tween burst = icon.CreateTween().SetParallel();
			burst.TweenProperty(icon, "scale", new Vector2(1.3f, 1.3f), 0.35);
			burst.TweenProperty(icon, "modulate:a", 0f, 0.35);
			for (int i = 0; i < 28; i++)
			{
				Label shard = CreateGlyphLabel(RandomGlyphs(1), LocalRandom.Next(20, 32));
				shard.Position = center - new Vector2(10f, 16f);
				shard.Modulate = new Color(0.85f, 0.82f, 0.95f, 0.9f);
				_stage.AddChild(shard);
				float angle = (float)(LocalRandom.NextDouble() * Math.Tau);
				float distance = 140f + (float)LocalRandom.NextDouble() * 220f;
				Tween fly = shard.CreateTween().SetParallel();
				fly.TweenProperty(shard, "position", shard.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance, 0.8)
					.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
				fly.TweenProperty(shard, "rotation", (float)(LocalRandom.NextDouble() - 0.5) * 3f, 0.8);
				fly.TweenProperty(shard, "modulate:a", 0f, 0.8);
			}
		}

		private void Flash()
		{
			ColorRect flash = new()
			{
				Color = new Color(1f, 1f, 1f, 0.85f),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_stage.AddChild(flash);
			Tween tween = flash.CreateTween();
			tween.TweenProperty(flash, "color:a", 0f, 0.18);
			tween.TweenCallback(Callable.From(flash.QueueFree));
		}

		private void TearNoise(int bands)
		{
			for (int i = 0; i < bands; i++)
			{
				Label band = CreateGlyphLabel(RandomGlyphs(96), LocalRandom.Next(22, 36));
				band.ClipText = true;
				band.HorizontalAlignment = HorizontalAlignment.Center;
				band.AnchorRight = 1f;
				band.OffsetTop = _viewport.Y * (0.15f + 0.7f * (float)LocalRandom.NextDouble());
				band.Modulate = new Color(0.78f, 0.8f, 0.9f, 0f);
				_stage.AddChild(band);
				Tween flicker = band.CreateTween();
				flicker.TweenInterval(i * 0.08);
				flicker.TweenProperty(band, "modulate:a", 0.7f, 0.04);
				flicker.TweenProperty(band, "modulate:a", 0.15f, 0.05);
				flicker.TweenProperty(band, "modulate:a", 0.6f, 0.04);
				flicker.TweenProperty(band, "modulate:a", 0f, 0.14);
				flicker.TweenCallback(Callable.From(band.QueueFree));
			}
		}

		private Label CreateGlyphLabel(string text, int fontSize)
		{
			Label label = new()
			{
				Text = text,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (_glyphFont != null)
			{
				label.AddThemeFontOverride("font", _glyphFont);
			}

			label.AddThemeFontSizeOverride("font_size", fontSize);
			return label;
		}

		private static string RandomGlyphs(int count)
		{
			return string.Create(count, count, static (span, _) =>
			{
				for (int i = 0; i < span.Length; i++)
				{
					span[i] = NoiseGlyphs[LocalRandom.Next(NoiseGlyphs.Length)];
				}
			});
		}

		private async Task WaitForAdvance(double timeoutSeconds)
		{
			_advance = new TaskCompletionSource<bool>();
			await Task.WhenAny(_advance.Task, Wait(timeoutSeconds));
			_advance = null;
		}

		private async Task Wait(double seconds)
		{
			await _tree.ToSignal(_tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
		}
	}
}
