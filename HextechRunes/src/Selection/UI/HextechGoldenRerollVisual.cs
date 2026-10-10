using Godot;

namespace HextechRunes;

/// <summary>
/// 金色重随按钮的常驻光效：循环播放原版 <c>Augment_GoldReroll</c>（按钮外框金币流光、四边火花、镜头光斑、扫过卡面的光）。
/// 挂在重随按钮内、以按钮中心为原点；按钮之外的部分不裁剪。
/// </summary>
internal sealed partial class HextechGoldenRerollVisual : Control
{
	private static readonly Color HoverModulate = new(1.18f, 1.18f, 1.18f, 1f);

	private HextechKiwiVfxPlayer? _player;
	private bool _active;

	private HextechGoldenRerollVisual()
	{
		Name = "GoldenRerollVisual";
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = false;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
	}

	public static HextechGoldenRerollVisual? Create(float cardPixelsPerUnit, float buttonPixelsPerUnit)
	{
		HextechKiwiVfxPlayer? player = HextechKiwiVfxPlayer.Create(
			HextechKiwiVfxLibrary.GoldenRerollIdle,
			cardPixelsPerUnit,
			buttonPixelsPerUnit,
			loop: true);
		if (player == null)
		{
			return null;
		}

		HextechGoldenRerollVisual visual = new() { _player = player };
		visual.AddChild(player);
		return visual;
	}

	/// <summary>常驻光里扫过卡面的部分以这张卡定位。</summary>
	internal void AnchorCardSpaceTo(Control card)
	{
		_player?.AnchorCardSpaceTo(card);
	}

	public void SetVisualState(bool active, bool hovered, bool disabled)
	{
		bool shouldPlay = active && !disabled;
		if (shouldPlay && !_active)
		{
			_player?.Restart();
		}

		_active = shouldPlay;
		Visible = shouldPlay;
		if (_player != null)
		{
			_player.ProcessMode = shouldPlay ? ProcessModeEnum.Always : ProcessModeEnum.Disabled;
		}
		Modulate = hovered && shouldPlay ? HoverModulate : Colors.White;
	}
}
