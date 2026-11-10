using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace HextechRunes;

// 开战直接获得原版能力的海克斯：以角色为中心的专属演出，每段播到关键帧后返回，调用方再施加对应能力，
// 原版获得能力的闪光与画面对上。纯表现层：只新建可视节点；测试进程、战斗外或非主线程时立即返回。
internal static partial class HextechCombatVfx
{
	private static readonly Color StoneColor = new(0.6f, 0.55f, 0.5f);
	private static readonly Color StoneEdgeColor = new(1f, 0.78f, 0.45f);
	private static readonly Color DustColor = new(0.75f, 0.68f, 0.58f);
	private static readonly Color AfterimageColor = new(0.62f, 0.95f, 1f);
	private static readonly Color BlueprintColor = new(0.35f, 0.75f, 1f);
	private static readonly Color GearColor = new(1f, 0.78f, 0.32f);
	private static readonly Color GearShadeColor = new(0.55f, 0.36f, 0.1f);
	private static readonly Color BadgeRimColor = new(1f, 0.82f, 0.38f);
	private static readonly Color BadgeFaceColor = new(0.22f, 0.16f, 0.1f);

	/// <summary>角色在画面上的取景：父节点、碰撞框中心、头顶、脚底与宽高。</summary>
	private readonly record struct CreatureFrame(Node Parent, Vector2 Center, Vector2 Top, Vector2 Feet, float Width, float Height);

	private static bool TryGetStageFrame(Creature owner, out CreatureFrame frame)
	{
		frame = default;
		if (NCombatRoom.Instance == null
			|| !NGame.IsMainThread()
			|| HextechCreatureNodeRegistry.TryGet(owner) is not { } node
			|| !node.IsInsideTree()
			|| node.GetParent() is not { } parent)
		{
			return false;
		}

		Vector2 top = node.GetTopOfHitbox();
		Vector2 feet = node.GetBottomOfHitbox();
		frame = new CreatureFrame(parent, top.Lerp(feet, 0.5f), top, feet, CreatureWidth(node), Mathf.Max(feet.Y - top.Y, 120f));
		return true;
	}

	/// <summary>不动如山·壁垒：脚下震开尘环与碎石，六边形石壳自下而上合拢后碎开。石壳碎开时返回。</summary>
	internal static async Task UnmovableMountainShell(Creature owner)
	{
		try
		{
			if (!TryGetStageFrame(owner, out CreatureFrame f))
			{
				return;
			}

			SpawnGroundRing(f.Parent, f.Feet, f.Width * 0.4f, f.Width * 2.2f, 0.55f, DustColor);
			for (int i = 0; i < 9; i++)
			{
				SpawnDebris(f.Parent, f.Feet + new Vector2((float)(VisualRng.NextDouble() - 0.5) * f.Width * 1.2f, 0f), f.Width, i * 0.02f);
			}

			Node2D shell = SpawnStoneShell(f);
			await Cmd.CustomScaledWait(0.35f, 0.55f);
			if (!GodotObject.IsInstanceValid(f.Parent))
			{
				return;
			}

			// 碎开：闪白、石壳膨胀淡出、碎片四散。
			SpawnFlash(f.Parent, f.Center, f.Width * 1.8f, StoneEdgeColor, 0.3f, 0.55f, aboveCreaturesOnly: true);
			if (GodotObject.IsInstanceValid(shell))
			{
				Tween fade = shell.CreateTween().SetParallel(true);
				fade.TweenProperty(shell, "scale", shell.Scale * 1.12f, 0.2f).SetEase(Tween.EaseType.Out);
				fade.TweenProperty(shell, "modulate:a", 0f, 0.2f);
				fade.Chain().TweenCallback(Callable.From(() => FreeNode(shell)));
			}

			for (int i = 0; i < 8; i++)
			{
				float angle = Mathf.Tau * i / 8f + (float)VisualRng.NextDouble() * 0.4f;
				SpawnShard(f.Parent, f.Center, Vector2.Right.Rotated(angle) * f.Width * (0.9f + (float)VisualRng.NextDouble() * 0.5f), f.Width * 0.12f);
			}
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure("unmovable-mountain-shell", ex);
		}
	}

	/// <summary>不动如山·余像：两道淡青残影光晕从角色左右滑出，伴随横向速度线。</summary>
	internal static async Task UnmovableMountainAfterimage(Creature owner)
	{
		try
		{
			if (!TryGetStageFrame(owner, out CreatureFrame f))
			{
				return;
			}

			foreach (float side in new[] { -1f, 1f })
			{
				Sprite2D ghost = MakeSprite(GetGlowTexture(), AfterimageColor with { A = 0.55f });
				f.Parent.AddChildSafely(ghost);
				PlaceAboveCreatures(f.Parent, ghost);
				ghost.GlobalPosition = f.Center;
				float textureSize = Math.Max(GetGlowTexture().GetWidth(), 1);
				ghost.Scale = new Vector2(f.Width * 0.9f, f.Height * 1.05f) / textureSize;
				Tween slide = ghost.CreateTween().SetParallel(true);
				slide.TweenProperty(ghost, "global_position", f.Center + new Vector2(side * f.Width * 0.75f, 0f), 0.35f)
					.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
				slide.TweenProperty(ghost, "modulate:a", 0f, 0.35f).SetEase(Tween.EaseType.In);
				slide.Chain().TweenCallback(Callable.From(() => FreeNode(ghost)));
			}

			for (int i = 0; i < 7; i++)
			{
				float y = Mathf.Lerp(f.Top.Y, f.Feet.Y, 0.12f + 0.76f * (float)VisualRng.NextDouble());
				float side = i % 2 == 0 ? -1f : 1f;
				SpawnSpeedLine(f.Parent, new Vector2(f.Center.X, y), side, f.Width, i * 0.025f);
			}

			await Cmd.CustomScaledWait(0.15f, 0.25f);
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure("unmovable-mountain-afterimage", ex);
		}
	}

	/// <summary>基石猎人：角色周围展开蓝图网格，左右两枚金色齿轮转动咬合后收进角色。齿轮收拢时返回。</summary>
	internal static async Task KeystoneHunterBlueprint(Creature owner)
	{
		try
		{
			if (!TryGetStageFrame(owner, out CreatureFrame f))
			{
				return;
			}

			Node2D grid = SpawnBlueprintGrid(f);
			float gearRadius = f.Width * 0.22f;
			Node2D[] gears =
			[
				SpawnGear(f.Parent, f.Center + new Vector2(-f.Width * 0.62f, -f.Height * 0.08f), gearRadius, 12, 1f),
				SpawnGear(f.Parent, f.Center + new Vector2(f.Width * 0.62f, f.Height * 0.12f), gearRadius * 0.8f, 10, -1f)
			];
			await Cmd.CustomScaledWait(0.4f, 0.6f);
			if (!GodotObject.IsInstanceValid(f.Parent))
			{
				return;
			}

			// 咬合：一声闪光后齿轮飞入角色，蓝图收起。
			foreach (Node2D gear in gears)
			{
				if (!GodotObject.IsInstanceValid(gear))
				{
					continue;
				}

				SpawnFlash(f.Parent, gear.GlobalPosition, gearRadius * 2.6f, GearColor, 0.25f, 0.7f, aboveCreaturesOnly: true);
				Tween absorb = gear.CreateTween().SetParallel(true);
				absorb.TweenProperty(gear, "global_position", f.Center, 0.25f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
				absorb.TweenProperty(gear, "scale", gear.Scale * 0.3f, 0.25f).SetEase(Tween.EaseType.In);
				absorb.Chain().TweenCallback(Callable.From(() => FreeNode(gear)));
			}

			if (GodotObject.IsInstanceValid(grid))
			{
				Tween fold = grid.CreateTween().SetParallel(true);
				fold.TweenProperty(grid, "scale", new Vector2(1f, 0f), 0.25f).SetEase(Tween.EaseType.In);
				fold.TweenProperty(grid, "modulate:a", 0f, 0.25f);
				fold.Chain().TweenCallback(Callable.From(() => FreeNode(grid)));
			}

			await Cmd.CustomScaledWait(0.15f, 0.25f);
			if (GodotObject.IsInstanceValid(f.Parent))
			{
				SpawnFlash(f.Parent, f.Center, f.Width * 1.4f, GearColor, 0.3f, 0.55f, aboveCreaturesOnly: true);
				SpawnRing(f.Parent, f.Center, f.Width * 0.35f, f.Width * 1.4f, 0.4f, 0.8f, BlueprintColor, aboveCreaturesOnly: true);
			}
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure("keystone-hunter", ex);
		}
	}

	/// <summary>霸符兄弟：一枚印着能力图标的徽章从上方砸到角色肩头（side=-1 左肩、1 右肩），震出冲击波。落定时返回。</summary>
	internal static Task BadgeBrothersSlam<TPower>(Creature owner, float side)
		where TPower : PowerModel
	{
		return NCombatRoom.Instance == null
			? Task.CompletedTask
			: PlayBadgeSlam(owner, side, static () => ModelDb.Power<TPower>().Icon);
	}

	private static async Task PlayBadgeSlam(Creature owner, float side, Func<Texture2D?> iconFactory)
	{
		try
		{
			if (!TryGetStageFrame(owner, out CreatureFrame f))
			{
				return;
			}

			Vector2 shoulder = f.Top + new Vector2(side * f.Width * 0.32f, f.Height * 0.18f);
			float diameter = f.Width * 0.42f;
			Node2D badge = BuildBadge(f.Parent, iconFactory(), diameter);
			badge.GlobalPosition = shoulder + new Vector2(side * f.Width * 0.15f, -f.Height * 0.65f);
			badge.Scale = Vector2.One * 1.8f;
			badge.Rotation = side * 0.5f;
			badge.Modulate = Colors.White with { A = 0f };
			Tween drop = badge.CreateTween().SetParallel(true);
			drop.TweenProperty(badge, "modulate:a", 1f, 0.08f);
			drop.TweenProperty(badge, "global_position", shoulder, 0.22f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
			drop.TweenProperty(badge, "scale", Vector2.One, 0.22f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
			drop.TweenProperty(badge, "rotation", 0f, 0.22f).SetEase(Tween.EaseType.Out);
			await Cmd.CustomScaledWait(0.16f, 0.22f);
			if (!GodotObject.IsInstanceValid(f.Parent))
			{
				return;
			}

			// 落定：冲击波、闪光、轻微震屏，徽章停留片刻后沉入角色。
			SpawnRing(f.Parent, shoulder, diameter * 0.6f, diameter * 2.6f, 0.4f, 0.9f, BadgeRimColor, aboveCreaturesOnly: true);
			SpawnFlash(f.Parent, shoulder, diameter * 2f, BadgeRimColor, 0.28f, 0.7f, aboveCreaturesOnly: true);
			NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
			if (GodotObject.IsInstanceValid(badge))
			{
				Tween sink = badge.CreateTween();
				sink.TweenInterval(0.25f);
				sink.TweenProperty(badge, "global_position", f.Center, 0.22f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
				sink.Parallel().TweenProperty(badge, "scale", Vector2.One * 0.4f, 0.22f).SetEase(Tween.EaseType.In);
				sink.Parallel().TweenProperty(badge, "modulate:a", 0f, 0.22f).SetEase(Tween.EaseType.In);
				sink.TweenCallback(Callable.From(() => FreeNode(badge)));
			}
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure("badge-brothers", ex);
		}
	}

	// ---- 不动如山的部件 ----

	// 地面环：压扁成椭圆的光环，模拟贴地扩散。
	private static void SpawnGroundRing(Node parent, Vector2 feet, float startWidth, float endWidth, float duration, Color color)
	{
		Texture2D texture = HextechTextures.LoadUiTexture(HextechAssets.SoftRingEffectPath) ?? GetRingTexture();
		Sprite2D ring = MakeSprite(texture, color with { A = 0.9f });
		ring.Material = null;
		parent.AddChildSafely(ring);
		PlaceAboveCreatures(parent, ring);
		ring.GlobalPosition = feet;
		float size = Math.Max(texture.GetWidth(), 1);
		ring.Scale = new Vector2(startWidth / size, startWidth * 0.28f / size);
		Tween tween = ring.CreateTween().SetParallel(true);
		tween.TweenProperty(ring, "scale", new Vector2(endWidth / size, endWidth * 0.28f / size), duration).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.TweenProperty(ring, "modulate:a", 0f, duration).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() => FreeNode(ring)));
	}

	// 碎石：从地面弹起再落下。
	private static void SpawnDebris(Node parent, Vector2 origin, float scaleRef, float delay)
	{
		float size = scaleRef * (0.04f + (float)VisualRng.NextDouble() * 0.04f);
		Polygon2D rock = new()
		{
			Polygon = [new Vector2(-size, size * 0.6f), new Vector2(size * 0.2f, -size), new Vector2(size, size * 0.4f)],
			Color = StoneColor.Darkened((float)VisualRng.NextDouble() * 0.3f)
		};
		parent.AddChildSafely(rock);
		PlaceAboveCreatures(parent, rock);
		rock.GlobalPosition = origin;
		rock.Modulate = Colors.White with { A = 0f };
		float rise = scaleRef * (0.25f + (float)VisualRng.NextDouble() * 0.35f);
		float drift = (float)(VisualRng.NextDouble() - 0.5) * scaleRef * 0.5f;
		Tween tween = rock.CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(rock, "modulate:a", 1f, 0.03f);
		tween.TweenProperty(rock, "global_position", origin + new Vector2(drift * 0.6f, -rise), 0.22f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
		tween.Parallel().TweenProperty(rock, "rotation", (float)(VisualRng.NextDouble() - 0.5) * 6f, 0.5f);
		tween.TweenProperty(rock, "global_position", origin + new Vector2(drift, 0f), 0.24f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
		tween.TweenProperty(rock, "modulate:a", 0f, 0.15f);
		tween.TweenCallback(Callable.From(() => FreeNode(rock)));
	}

	// 六边形石壳：以脚底为轴，纵向从 0 长到完整高度。
	private static Node2D SpawnStoneShell(CreatureFrame f)
	{
		Node2D shell = new() { Name = "HextechRunes_StoneShell" };
		f.Parent.AddChildSafely(shell);
		PlaceAboveCreatures(f.Parent, shell);
		shell.GlobalPosition = f.Feet;

		float rx = f.Width * 0.78f;
		float ry = f.Height * 0.6f;
		Vector2 center = new(0f, -f.Height * 0.5f);
		Vector2[] hexagon = new Vector2[6];
		for (int i = 0; i < 6; i++)
		{
			float angle = Mathf.Tau * i / 6f;
			hexagon[i] = center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
		}

		shell.AddChildSafely(new Polygon2D { Polygon = hexagon, Color = StoneColor with { A = 0.28f } });
		Line2D edge = new()
		{
			Points = [.. hexagon, hexagon[0]],
			Width = 9f,
			DefaultColor = StoneEdgeColor with { A = 0.9f },
			JointMode = Line2D.LineJointMode.Round,
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
		shell.AddChildSafely(edge);
		for (int i = 0; i < 6; i++)
		{
			shell.AddChildSafely(MakeLocalLine(center, hexagon[i], 3f, StoneEdgeColor with { A = 0.35f }));
		}

		shell.Scale = new Vector2(1f, 0f);
		Tween grow = shell.CreateTween();
		grow.TweenProperty(shell, "scale", Vector2.One, 0.28f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		return shell;
	}

	// 碎片：从石壳中心向外飞散、旋转淡出。
	private static void SpawnShard(Node parent, Vector2 origin, Vector2 travel, float size)
	{
		Polygon2D shard = new()
		{
			Polygon = [new Vector2(-size, -size * 0.4f), new Vector2(size, -size * 0.2f), new Vector2(size * 0.1f, size)],
			Color = StoneColor.Lightened((float)VisualRng.NextDouble() * 0.2f)
		};
		parent.AddChildSafely(shard);
		PlaceAboveCreatures(parent, shard);
		shard.GlobalPosition = origin;
		Tween tween = shard.CreateTween().SetParallel(true);
		tween.TweenProperty(shard, "global_position", origin + travel, 0.4f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.TweenProperty(shard, "rotation", (float)(VisualRng.NextDouble() - 0.5) * 8f, 0.4f);
		tween.TweenProperty(shard, "modulate:a", 0f, 0.4f).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() => FreeNode(shard)));
	}

	// 速度线：从角色身上向一侧拉出、变细淡出。
	private static void SpawnSpeedLine(Node parent, Vector2 origin, float side, float scaleRef, float delay)
	{
		float length = scaleRef * (0.5f + (float)VisualRng.NextDouble() * 0.4f);
		Line2D line = new()
		{
			TopLevel = true,
			Points = [origin, origin + new Vector2(side * length, 0f)],
			Width = 5f,
			DefaultColor = AfterimageColor with { A = 0f },
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
		parent.AddChildSafely(line);
		PlaceAboveCreatures(parent, line);
		Tween tween = line.CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(line, "default_color:a", 0.85f, 0.04f);
		tween.TweenProperty(line, "position", new Vector2(side * scaleRef * 0.8f, 0f), 0.3f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.Parallel().TweenProperty(line, "width", 0.5f, 0.3f);
		tween.Parallel().TweenProperty(line, "default_color:a", 0f, 0.3f).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(() => FreeNode(line)));
	}

	// ---- 基石猎人的部件 ----

	// 蓝图：角色周围的网格与四角框线，从中线横向展开。
	private static Node2D SpawnBlueprintGrid(CreatureFrame f)
	{
		Node2D grid = new() { Name = "HextechRunes_Blueprint" };
		f.Parent.AddChildSafely(grid);
		PlaceAboveCreatures(f.Parent, grid);
		grid.GlobalPosition = f.Center;

		float halfWidth = f.Width * 0.85f;
		float halfHeight = f.Height * 0.62f;
		float step = Mathf.Max(f.Width * 0.16f, 24f);
		Color faint = BlueprintColor with { A = 0.28f };
		for (float x = -halfWidth; x <= halfWidth + 0.5f; x += step)
		{
			grid.AddChildSafely(MakeLocalLine(new Vector2(x, -halfHeight), new Vector2(x, halfHeight), 1.5f, faint));
		}

		for (float y = -halfHeight; y <= halfHeight + 0.5f; y += step)
		{
			grid.AddChildSafely(MakeLocalLine(new Vector2(-halfWidth, y), new Vector2(halfWidth, y), 1.5f, faint));
		}

		float bracket = step * 1.4f;
		foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
		{
			Vector2 point = new(corner.X * halfWidth, corner.Y * halfHeight);
			grid.AddChildSafely(MakeLocalLine(point, point - new Vector2(corner.X * bracket, 0f), 4f, BlueprintColor));
			grid.AddChildSafely(MakeLocalLine(point, point - new Vector2(0f, corner.Y * bracket), 4f, BlueprintColor));
		}

		grid.Scale = new Vector2(0f, 1f);
		grid.Modulate = Colors.White with { A = 0f };
		Tween open = grid.CreateTween().SetParallel(true);
		open.TweenProperty(grid, "scale", Vector2.One, 0.25f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		open.TweenProperty(grid, "modulate:a", 1f, 0.15f);
		return grid;
	}

	// 齿轮：齿形多边形 + 轴心，弹出后持续转动（direction 为转向）。
	private static Node2D SpawnGear(Node parent, Vector2 globalPos, float radius, int teeth, float direction)
	{
		Node2D gear = new() { Name = "HextechRunes_Gear" };
		parent.AddChildSafely(gear);
		PlaceAboveCreatures(parent, gear);
		gear.GlobalPosition = globalPos;

		List<Vector2> outline = [];
		float inner = radius * 0.78f;
		for (int i = 0; i < teeth; i++)
		{
			float baseAngle = Mathf.Tau * i / teeth;
			float toothWidth = Mathf.Tau / teeth;
			outline.Add(Vector2.Right.Rotated(baseAngle) * inner);
			outline.Add(Vector2.Right.Rotated(baseAngle + toothWidth * 0.15f) * radius);
			outline.Add(Vector2.Right.Rotated(baseAngle + toothWidth * 0.5f) * radius);
			outline.Add(Vector2.Right.Rotated(baseAngle + toothWidth * 0.65f) * inner);
		}

		gear.AddChildSafely(new Polygon2D { Polygon = [.. outline], Color = GearColor });
		Vector2[] hub = new Vector2[16];
		for (int i = 0; i < hub.Length; i++)
		{
			hub[i] = Vector2.Right.Rotated(Mathf.Tau * i / hub.Length) * radius * 0.3f;
		}

		gear.AddChildSafely(new Polygon2D { Polygon = hub, Color = GearShadeColor });
		gear.Scale = Vector2.Zero;
		Tween pop = gear.CreateTween();
		pop.TweenProperty(gear, "scale", Vector2.One, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		Tween spin = gear.CreateTween();
		spin.TweenProperty(gear, "rotation", direction * Mathf.Pi * 1.5f, 0.55f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		return gear;
	}

	// ---- 霸符兄弟的部件 ----

	// 徽章：金色外圈、深色底面，中间印能力图标。
	private static Node2D BuildBadge(Node parent, Texture2D? icon, float diameter)
	{
		Node2D badge = new() { Name = "HextechRunes_Badge" };
		parent.AddChildSafely(badge);
		PlaceAboveCreatures(parent, badge);
		Sprite2D glow = MakeSprite(GetGlowTexture(), BadgeRimColor with { A = 0.55f });
		badge.AddChildSafely(glow);
		SetSpriteDiameter(glow, diameter * 1.8f);
		badge.AddChildSafely(new Polygon2D { Polygon = Circle(diameter * 0.5f, 28), Color = BadgeRimColor });
		badge.AddChildSafely(new Polygon2D { Polygon = Circle(diameter * 0.42f, 28), Color = BadgeFaceColor });
		if (icon != null)
		{
			Sprite2D sprite = new() { Texture = icon, Centered = true };
			badge.AddChildSafely(sprite);
			SetSpriteDiameter(sprite, diameter * 0.72f);
		}

		return badge;
	}

	private static Vector2[] Circle(float radius, int segments)
	{
		Vector2[] points = new Vector2[segments];
		for (int i = 0; i < segments; i++)
		{
			points[i] = Vector2.Right.Rotated(Mathf.Tau * i / segments) * radius;
		}

		return points;
	}
}
