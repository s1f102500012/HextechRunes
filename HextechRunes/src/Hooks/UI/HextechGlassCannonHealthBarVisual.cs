using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

/// <summary>玻璃大炮:血条上封顶线以右画斜线遮罩,提示这段生命无法被回复。</summary>
internal sealed class HextechGlassCannonHealthBarVisual : HextechCreatureAttachedVisual
{
	private const string OverlayName = "HextechRunes_GlassCannonLock";
	private const string LogTag = "GlassCannon";
	private static readonly Color DimColor = new(0.05f, 0.05f, 0.07f, 0.16f);

	private static Texture2D? _hatchTexture;

	private Control? _overlay;

	private HextechGlassCannonHealthBarVisual(NCreature creature)
		: base(creature, LogTag, "Health bar lock visual")
	{
	}

	// 以生物节点本身逐帧驱动:遮罩节点按需创建,在封顶生效前可能还不存在。
	protected override Node? FrameNode => Creature;

	// 我方回复封顶在战斗外仍然生效,胜利后奖励界面期间血条还留在房间里;遮罩不读 Spine、不刷日志,
	// 跟随房间切换与节点失效结束即可。
	protected override bool EndsWithCombat => false;

	internal static void TryAttach(NCreature? creature)
	{
		TryAttach(
			creature,
			LogTag,
			"health bar lock visual",
			static node => node.Entity != null,
			static node => new HextechGlassCannonHealthBarVisual(node));
	}

	protected override bool Start()
	{
		return true;
	}

	protected override bool Tick()
	{
		if (TryGetCapRatio(Creature, out float cap))
		{
			EnsureOverlay();
			if (GodotObject.IsInstanceValid(_overlay))
			{
				_overlay.AnchorLeft = cap;
				_overlay.OffsetLeft = 0f;

				// 内缩,避开血条的圆角端帽与上下边,使斜线落在原版血条轮廓之内、而非外接矩形。
				float height = _overlay.Size.Y;
				if (height >= 4f)
				{
					_overlay.OffsetTop = height * 0.12f;
					_overlay.OffsetBottom = -height * 0.12f;
					_overlay.OffsetRight = -height * 0.42f;
				}

				_overlay.Visible = true;
			}
		}
		else if (GodotObject.IsInstanceValid(_overlay))
		{
			_overlay.Visible = false;
		}

		return true;
	}

	protected override void Release()
	{
		if (GodotObject.IsInstanceValid(_overlay))
		{
			_overlay.QueueFree();
		}
	}

	private static bool TryGetCapRatio(NCreature creatureNode, out float cap)
	{
		cap = 0f;
		Creature? creature = creatureNode.Entity;
		if (creature == null)
		{
			return false;
		}

		// 我方:玻璃大炮遗物。
		GlassCannonRune? rune = creature.Player?.GetRelic<GlassCannonRune>();
		if (rune != null)
		{
			cap = Mathf.Clamp((float)rune.HealCapPercent, 0.05f, 0.99f);
			return true;
		}

		// 敌方:玻璃大炮敌方海克斯(整场战斗对全体敌人生效,封顶比例与结算共用同一常量)。
		if (creature.Monster != null
			&& HextechMayhemModifier.FindIn(creature.CombatState?.RunState) is { } modifier
			&& modifier.HasActiveMonsterHex(MonsterHexKind.GlassCannon))
		{
			cap = (float)GlassCannonEnemyHex.HealCapPercent;
			return true;
		}

		return false;
	}

	private void EnsureOverlay()
	{
		if (GodotObject.IsInstanceValid(_overlay))
		{
			return;
		}

		// NCreature →(%HealthBar)→ NCreatureStateDisplay →(%HealthBar)→ NHealthBar →(%HpForegroundContainer)→ 满血轨道。
		if (Creature.GetNodeOrNull("%HealthBar") is not { } stateDisplay
			|| stateDisplay.GetNodeOrNull<NHealthBar>("%HealthBar") is not { } bar
			|| bar.GetNodeOrNull<Control>("%HpForegroundContainer") is not { } track)
		{
			return;
		}

		Control overlay = new()
		{
			Name = OverlayName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ClipContents = true,
			ZIndex = 3,
			ZAsRelative = true,
			Visible = false,
			AnchorTop = 0f,
			AnchorBottom = 1f,
			AnchorRight = 1f,
			AnchorLeft = (float)GlassCannonEnemyHex.HealCapPercent,
			OffsetLeft = 0f,
			OffsetRight = 0f,
			OffsetTop = 0f,
			OffsetBottom = 0f
		};

		ColorRect dim = new()
		{
			Color = DimColor,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(dim);

		TextureRect hatch = new()
		{
			Texture = GetHatchTexture(),
			StretchMode = TextureRect.StretchModeEnum.Tile,
			TextureRepeat = CanvasItem.TextureRepeatEnum.Enabled,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		hatch.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(hatch);

		track.AddChild(overlay);
		_overlay = overlay;
	}

	private static Texture2D GetHatchTexture()
	{
		if (_hatchTexture != null && GodotObject.IsInstanceValid(_hatchTexture))
		{
			return _hatchTexture;
		}

		// 程序化生成可无缝平铺的灰色斜线(45°)。tile=14、周期=7 → 更细更疏、边界处衔接连续。
		const int size = 14;
		const int period = 7;
		Color line = new(0.86f, 0.88f, 0.94f, 0.5f);
		Color clear = new(0f, 0f, 0f, 0f);
		Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				image.SetPixel(x, y, (x + y) % period < 2 ? line : clear);
			}
		}

		_hatchTexture = ImageTexture.CreateFromImage(image);
		return _hatchTexture;
	}
}
