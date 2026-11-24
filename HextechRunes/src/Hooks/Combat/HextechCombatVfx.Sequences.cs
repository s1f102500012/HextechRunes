using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace HextechRunes;

internal static partial class HextechCombatVfx
{
	private static async Task RunBoomerangSweep(Creature owner, Creature[] targets, Texture2D? boomerangTexture, bool roundTrip = false)
	{
		try
		{
			NCreature? ownerNode = HextechCreatureNodeRegistry.TryGet(owner);
			if (ownerNode == null || targets.Length == 0)
			{
				return;
			}

			Node? parent = ownerNode.GetParent();
			if (!GodotObject.IsInstanceValid(parent))
			{
				return;
			}

			// 位置全部快照:飞行途中敌人会被伤害击杀,节点随时失效。
			Vector2 ownerPos = CreatureCenter(ownerNode);
			List<Vector2> waypoints = BuildBoomerangWaypoints(ownerPos, targets, roundTrip, out int outboundSegments);
			if (waypoints.Count < 3)
			{
				return;
			}

			float width = CreatureWidth(ownerNode);
			Sprite2D boomerang = new()
			{
				Name = "HextechRunes_Boomerang",
				Texture = boomerangTexture ?? GetGlowTexture(),
				Centered = true,
				Modulate = Colors.White
			};
			parent!.AddChildSafely(boomerang);
			PlaceAboveCreatures(parent, boomerang);
			boomerang.GlobalPosition = ownerPos;
			SetSpriteDiameter(boomerang, width * 0.42f);

			Line2D trail = MakeBoomerangTrail(width);
			parent.AddChildSafely(trail);
			PlaceAboveCreatures(parent, trail);

			SceneTree? tree = boomerang.GetTree();
			if (tree == null)
			{
				FreeNode(boomerang);
				FreeNode(trail);
				return;
			}

			// 逐段贝塞尔飞行:段末命中闪光。转速恒定,拖尾逐帧跟随。
			for (int segment = 0; segment + 1 < waypoints.Count; segment++)
			{
				bool isReturn = segment == waypoints.Count - 2;
				float duration = segment == 0 ? BoomerangFirstArrivalSeconds
					: isReturn ? 0.3f
					: BoomerangPerTargetSeconds;
				// 回程段(含最远处的折返回环)走下弧,与去程的上弧区分开。
				float liftDirection = isReturn || segment >= outboundSegments ? 1f : -1f;
				if (!await FlyBoomerangSegment(tree, boomerang, trail, waypoints[segment], waypoints[segment + 1], duration, liftDirection))
				{
					FreeNode(trail);
					return;
				}

				if (!isReturn && GodotObject.IsInstanceValid(parent))
				{
					SpawnFlash(parent, waypoints[segment + 1], CreatureWidth(ownerNode) * 0.7f, new Color(0.75f, 0.92f, 1f), 0.28f, 0.55f, aboveCreaturesOnly: true);
				}
			}

			FreeNode(boomerang);
			if (GodotObject.IsInstanceValid(trail))
			{
				Tween fade = trail.CreateTween();
				fade.TweenProperty(trail, "modulate:a", 0f, 0.2f);
				fade.TweenCallback(Callable.From(() => FreeNode(trail)));
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Boomerang sweep failed: {ex.Message}");
		}
	}

	/// <summary>施法者 → 各敌人（取得到节点的）[→ 逆序再扫一遍] → 施法者；返回去程段数。</summary>
	private static List<Vector2> BuildBoomerangWaypoints(Vector2 ownerPos, Creature[] targets, bool roundTrip, out int outboundSegments)
	{
		List<Vector2> hitPoints = [];
		foreach (Creature target in targets)
		{
			NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
			if (node != null)
			{
				hitPoints.Add(CreatureCenter(node));
			}
		}

		List<Vector2> waypoints = [ownerPos, .. hitPoints];
		outboundSegments = hitPoints.Count;
		if (roundTrip)
		{
			// 回程:在最远敌人处打个转(自身回环段)后逆序再扫一遍。
			for (int i = hitPoints.Count - 1; i >= 0; i--)
			{
				waypoints.Add(hitPoints[i]);
			}
		}

		waypoints.Add(ownerPos);
		return waypoints;
	}

	private static Line2D MakeBoomerangTrail(float width)
	{
		return new Line2D
		{
			Name = "HextechRunes_BoomerangTrail",
			Width = width * 0.16f,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			JointMode = Line2D.LineJointMode.Round,
			WidthCurve = MakeTrailWidthCurve(),
			Gradient = new Gradient
			{
				Offsets = [0f, 0.6f, 1f],
				Colors = [new Color(0.62f, 0.9f, 1f, 0.55f), new Color(0.62f, 0.9f, 1f, 0.25f), new Color(0.62f, 0.9f, 1f, 0f)]
			},
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
	}

	/// <summary>沿一段上/下拱的贝塞尔弧逐帧飞行并旋转；镖体中途被销毁时返回 false。</summary>
	private static async Task<bool> FlyBoomerangSegment(
		SceneTree tree,
		Sprite2D boomerang,
		Line2D trail,
		Vector2 from,
		Vector2 to,
		float duration,
		float liftDirection)
	{
		Vector2 mid = (from + to) * 0.5f;
		Vector2 control = mid + new Vector2(0f, liftDirection * Mathf.Max(60f, from.DistanceTo(to) * 0.25f));
		float elapsed = 0f;
		while (elapsed < duration)
		{
			if (!GodotObject.IsInstanceValid(boomerang))
			{
				return false;
			}

			float dt = Mathf.Clamp((float)boomerang.GetProcessDeltaTime(), 1f / 240f, 0.05f);
			elapsed += dt;
			float t = Mathf.Clamp(elapsed / duration, 0f, 1f);
			Vector2 position = Bezier(from, control, to, t);
			boomerang.GlobalPosition = position;
			boomerang.Rotation += dt * Mathf.Tau * 2.2f;
			if (GodotObject.IsInstanceValid(trail))
			{
				trail.AddPoint(trail.ToLocal(position), 0);
				if (trail.GetPointCount() > 14)
				{
					trail.RemovePoint(trail.GetPointCount() - 1);
				}
			}

			await boomerang.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		}

		return true;
	}

	private static readonly Color OmegaWarnColor = new(1f, 0.22f, 0.16f);
	private static readonly Color OmegaBeamColor = new(1f, 0.32f, 0.2f);
	private static readonly Color OmegaFlashColor = new(1f, 0.78f, 0.62f);

	private static async Task RunOmegaJudgment(Creature[] targets)
	{
		try
		{
			List<BeamSpot> spots = CollectBeamSpots(targets);
			if (spots.Count == 0)
			{
				return;
			}

			// 预警:所有敌人脚下同时亮起红色警戒环;审判:赤红光柱依次砸下,命中爆闪+扩散环。
			SceneTree? tree = await WarnThenResolveTree(spots, OmegaWarnColor);
			if (tree == null)
			{
				return;
			}

			await StrikeBeamsInSequence(tree, spots, OmegaBeamColor, OmegaFlashColor, intervalSeconds: 0.06f);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Omega judgment failed: {ex.Message}");
		}
	}

	/// <summary>欧米伽/量子计算的逐敌光柱落点快照（节点在后续伤害中可能被移除）。</summary>
	private readonly record struct BeamSpot(Vector2 Center, Vector2 Bottom, float Width, float Height, Node Parent);

	private static List<BeamSpot> CollectBeamSpots(Creature[] targets)
	{
		List<BeamSpot> spots = [];
		foreach (Creature target in targets)
		{
			NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
			Node? parent = node?.GetParent();
			if (node == null || !GodotObject.IsInstanceValid(parent))
			{
				continue;
			}

			Vector2 bottom = node.GetBottomOfHitbox();
			float height = Mathf.Max(bottom.Y - node.GetTopOfHitbox().Y, 120f);
			spots.Add(new BeamSpot(CreatureCenter(node), bottom, CreatureWidth(node), height, parent!));
		}

		return spots;
	}

	/// <summary>所有落点同时亮起收缩的预警环，留 0.3 秒后返回用于计时的 SceneTree（取不到时返回 null）。</summary>
	private static async Task<SceneTree?> WarnThenResolveTree(List<BeamSpot> spots, Color warnColor)
	{
		foreach (BeamSpot spot in spots)
		{
			SpawnRing(spot.Parent, spot.Center, spot.Width * 1.2f, spot.Width * 0.55f, 0.34f, 0.7f, warnColor, aboveCreaturesOnly: true);
		}

		SceneTree? tree = (spots[0].Parent as Node2D)?.GetTree() ?? (Engine.GetMainLoop() as SceneTree);
		if (tree != null)
		{
			await WaitSeconds(tree, 0.3f);
		}

		return tree;
	}

	/// <summary>逐个落点砸下光柱（原版天降射线贴图）+ 爆闪 + 扩散环，每个之间间隔 <paramref name="intervalSeconds"/>。</summary>
	private static async Task StrikeBeamsInSequence(SceneTree tree, List<BeamSpot> spots, Color beamColor, Color flashColor, float intervalSeconds)
	{
		Texture2D? rayTexture = HextechTextures.LoadVanillaTexture("res://images/vfx/missile/missile_sky_ray.png");
		foreach (BeamSpot spot in spots)
		{
			if (rayTexture != null && GodotObject.IsInstanceValid(spot.Parent))
			{
				SpawnOmegaBeam(spot.Parent, spot.Bottom, spot.Width, spot.Height, rayTexture, beamColor);
			}

			if (GodotObject.IsInstanceValid(spot.Parent))
			{
				SpawnFlash(spot.Parent, spot.Center, spot.Width * 1.2f, flashColor, 0.3f, 0.75f, aboveCreaturesOnly: true);
				SpawnRing(spot.Parent, spot.Center, spot.Width * 0.3f, spot.Width * 1.35f, 0.4f, 0.85f, beamColor, aboveCreaturesOnly: true);
			}

			await WaitSeconds(tree, intervalSeconds);
		}
	}

	private static readonly Color PoisonBurstColor = new(0.5f, 0.9f, 0.3f);

	private static async Task RunCorpseBloomBurst(Vector2? sourcePos, Creature[] targets)
	{
		try
		{
			List<(Vector2 Center, float Width, Node Parent)> spots = [];
			foreach (Creature target in targets)
			{
				NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
				Node? parent = node?.GetParent();
				if (node == null || !GodotObject.IsInstanceValid(parent))
				{
					continue;
				}

				spots.Add((CreatureCenter(node), CreatureWidth(node), parent!));
			}

			if (spots.Count == 0)
			{
				return;
			}

			Node burstParent = spots[0].Parent;
			float burstWidth = spots.Max(static spot => spot.Width);
			// 尸体节点在死亡链上随时被移除:取不到就从目标群中心上方起爆。
			Vector2 origin = sourcePos ?? new Vector2(
				spots.Average(static spot => spot.Center.X),
				spots.Min(static spot => spot.Center.Y) - burstWidth * 0.4f);

			// 脓爆:毒绿爆闪+双层扩散环。
			SpawnFlash(burstParent, origin, burstWidth * 1.6f, PoisonBurstColor, 0.36f, 0.85f, aboveCreaturesOnly: true);
			SpawnRing(burstParent, origin, burstWidth * 0.4f, burstWidth * 1.8f, 0.42f, 0.85f, PoisonBurstColor, aboveCreaturesOnly: true);
			SpawnRing(burstParent, origin, burstWidth * 0.25f, burstWidth * 1.2f, 0.32f, 0.6f, Brighten(PoisonBurstColor), aboveCreaturesOnly: true);

			SceneTree? tree = (burstParent as Node2D)?.GetTree() ?? (Engine.GetMainLoop() as SceneTree);
			if (tree == null)
			{
				return;
			}

			await WaitSeconds(tree, 0.1f);

			// 毒液飞溅:弧线毒滴逐个泼向存活敌人,命中处小型毒溅。
			int index = 0;
			foreach ((Vector2 center, float width, Node parent) in spots)
			{
				if (!GodotObject.IsInstanceValid(parent))
				{
					continue;
				}

				Vector2 hitCenter = center;
				float hitWidth = width;
				Node hitParent = parent;
				SpawnSoulWisp(
					parent,
					origin,
					center,
					width * 0.3f,
					0.4f,
					index * 0.05f,
					-Mathf.Max(70f, origin.DistanceTo(center) * 0.3f),
					PoisonBurstColor,
					() =>
					{
						if (GodotObject.IsInstanceValid(hitParent))
						{
							SpawnFlash(hitParent, hitCenter, hitWidth * 0.8f, PoisonBurstColor, 0.26f, 0.65f, aboveCreaturesOnly: true);
							SpawnRing(hitParent, hitCenter, hitWidth * 0.2f, hitWidth * 0.8f, 0.3f, 0.6f, PoisonBurstColor, aboveCreaturesOnly: true);
						}
					});
				index++;
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Corpse bloom burst failed: {ex.Message}");
		}
	}

	/// <summary>欧米伽的赤红审判光柱:窄而急促(0.08s 闪现全亮,0.3s 收束消退)。</summary>
	private static void SpawnOmegaBeam(Node parent, Vector2 bottom, float width, float height, Texture2D rayTexture, Color beamColor)
	{
		float beamHeight = height * 2.4f;
		Sprite2D beam = new()
		{
			Name = "HextechRunes_OmegaBeam",
			Texture = rayTexture,
			Centered = true,
			Modulate = beamColor with { A = 0f },
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
		parent.AddChildSafely(beam);
		PlaceAboveCreatures(parent, beam);
		beam.GlobalPosition = new Vector2(bottom.X, bottom.Y - beamHeight * 0.5f);
		Vector2 fullScale = new(width * 0.8f / Math.Max(rayTexture.GetWidth(), 1), beamHeight / Math.Max(rayTexture.GetHeight(), 1));
		beam.Scale = fullScale;

		Tween tween = beam.CreateTween();
		tween.TweenProperty(beam, "modulate:a", 1f, 0.08f).SetEase(Tween.EaseType.Out);
		tween.SetParallel(true);
		tween.TweenProperty(beam, "modulate:a", 0f, 0.3f).SetEase(Tween.EaseType.In).SetDelay(0.08f);
		tween.TweenProperty(beam, "scale:x", fullScale.X * 0.25f, 0.3f).SetEase(Tween.EaseType.In).SetDelay(0.08f);
		tween.Chain().TweenCallback(Callable.From(() => FreeNode(beam)));
	}

	private static async Task RunFlyingKickStrike(Creature target, Creature owner)
	{
		try
		{
			NCreature? targetNode = HextechCreatureNodeRegistry.TryGet(target);
			NCreature? ownerNode = HextechCreatureNodeRegistry.TryGet(owner);
			if (targetNode == null)
			{
				return;
			}

			Node? parent = targetNode.GetParent();
			if (!GodotObject.IsInstanceValid(parent))
			{
				return;
			}

			// 击杀点快照:目标马上会被处决并横飞。
			Vector2 strikePos = CreatureCenter(targetNode);
			float width = CreatureWidth(targetNode);
			Color tint = GetSoulTint(target);

			// 踢击:原版大斩击节点 + 目标本体色爆闪。
			NBigSlashVfx.Create(target);
			NBigSlashImpactVfx.Create(target);
			SpawnFlash(parent!, strikePos, width * 1.3f, Brighten(tint), 0.32f, 0.7f, aboveCreaturesOnly: true);
			SpawnRing(parent!, strikePos, width * 0.35f, width * 1.2f, 0.38f, 0.8f, tint, aboveCreaturesOnly: true);

			// 光流殿后:尸体横飞展开后,一缕治疗绿光从击杀点弧线流回施法者。
			SceneTree? tree = (parent as Node2D)?.GetTree() ?? (Engine.GetMainLoop() as SceneTree);
			if (tree == null || ownerNode == null || !GodotObject.IsInstanceValid(ownerNode))
			{
				return;
			}

			await WaitSeconds(tree, 0.5f);
			if (!GodotObject.IsInstanceValid(parent) || !GodotObject.IsInstanceValid(ownerNode))
			{
				return;
			}

			Color healColor = new(0.45f, 0.95f, 0.5f);
			Vector2 ownerPos = CreatureCenter(ownerNode);
			SpawnSoulWisp(parent!, strikePos, ownerPos, width * 0.34f, 0.55f, 0f, -width * 0.7f, healColor, () =>
			{
				if (GodotObject.IsInstanceValid(parent) && GodotObject.IsInstanceValid(ownerNode))
				{
					SpawnFlash(parent!, ownerPos, width * 0.9f, healColor.Lightened(0.3f), 0.3f, 0.5f, aboveCreaturesOnly: true);
				}
			});
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Flying kick strike failed: {ex.Message}");
		}
	}

	private static async Task WaitSeconds(SceneTree tree, float seconds)
	{
		await tree.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	}

	private static void RunDeathRingLash(Creature source, Creature target)
	{
		try
		{
			NCreature? targetNode = HextechCreatureNodeRegistry.TryGet(target);
			if (targetNode == null)
			{
				return;
			}

			Node? parent = targetNode.GetParent();
			if (!GodotObject.IsInstanceValid(parent))
			{
				return;
			}

			Vector2 targetPos = CreatureCenter(targetNode);
			float width = CreatureWidth(targetNode);
			// 敌人受击爆发刻意做得比我方(施法侧)的环更小一点 —— 它只是被动触发的命中,不该盖过你自身的攻击表现。
			SpawnFlash(parent!, targetPos, width * 0.75f, DeathFlashColor, 0.40f, 0.40f);
			SpawnRing(parent!, targetPos, width * 0.3f, width * 0.8f, 0.45f, 0.95f, DeathRingColor);

			NCreature? sourceNode = HextechCreatureNodeRegistry.TryGet(source);
			if (sourceNode != null)
			{
				Vector2 sourcePos = CreatureCenter(sourceNode);
				SpawnBeam(parent!, sourcePos, targetPos, DeathRingColor, 0.30f);
				SpawnRing(parent!, sourcePos, width * 0.25f, width * 0.95f, 0.30f, 0.55f, DeathRingColor);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Death ring lash failed: {ex.Message}");
		}
	}

	private static void RunDivinePulse(Creature[] allies)
	{
		try
		{
			foreach (Creature ally in allies)
			{
				NCreature? node = HextechCreatureNodeRegistry.TryGet(ally);
				if (node == null)
				{
					continue;
				}

				Node? parent = node.GetParent();
				if (!GodotObject.IsInstanceValid(parent))
				{
					continue;
				}

				Vector2 pos = CreatureCenter(node);
				float width = CreatureWidth(node);
				Vector2 bottom = node.GetBottomOfHitbox();
				float height = Mathf.Max(bottom.Y - node.GetTopOfHitbox().Y, width);

				// 主角是从天而降的光柱;柔光与单环收敛为落地反馈,光尘自身体升起。
				Texture2D? rayTexture = HextechTextures.LoadVanillaTexture("res://images/vfx/missile/missile_sky_ray.png");
				if (rayTexture != null)
				{
					SpawnLightShaft(parent!, bottom, width, height, rayTexture);
				}

				SpawnSparkleRise(parent!, bottom, width, height);
				SpawnFlash(parent!, pos, width * 1.5f, DivineFlashColor, 0.4f, 0.4f);
				SpawnRing(parent!, pos, width * 0.4f, width * 1.5f, 0.6f, 0.7f, DivineRingColor);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Divine pulse failed: {ex.Message}");
		}
	}

	/// <summary>神圣干预:金色光柱自天顶罩下(横向展开淡入、驻留、淡出)。</summary>
	private static void SpawnLightShaft(Node parent, Vector2 bottom, float width, float height, Texture2D rayTexture)
	{
		float shaftHeight = height * 2.1f;
		float shaftWidth = width * 1.45f;
		Sprite2D shaft = new()
		{
			Name = "HextechRunes_DivineShaft",
			Texture = rayTexture,
			Centered = true,
			TopLevel = true,
			Modulate = DivineShaftColor with { A = 0f },
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
		parent.AddChildSafely(shaft);
		// 光柱底沿落在脚底:贴图光锥自顶向下渐散,顶亮端悬在头顶上空。
		shaft.GlobalPosition = new Vector2(bottom.X, bottom.Y - shaftHeight * 0.5f);
		Vector2 fullScale = new(shaftWidth / Math.Max(rayTexture.GetWidth(), 1), shaftHeight / Math.Max(rayTexture.GetHeight(), 1));
		shaft.Scale = new Vector2(fullScale.X * 0.55f, fullScale.Y);

		Tween tween = shaft.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(shaft, "modulate:a", 0.8f, 0.16f).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(shaft, "scale:x", fullScale.X, 0.24f)
			.SetEase(Tween.EaseType.Out)
			.SetTrans(Tween.TransitionType.Cubic);
		tween.Chain().TweenInterval(0.26f);
		tween.Chain().TweenProperty(shaft, "modulate:a", 0f, 0.5f).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() => FreeNode(shaft)));
	}

	/// <summary>神圣干预:金色星光尘自身体缓缓升起(一次性粒子,规定时限后自毁)。</summary>
	private static void SpawnSparkleRise(Node parent, Vector2 bottom, float width, float height)
	{
		CpuParticles2D dust = new()
		{
			Name = "HextechRunes_DivineDust",
			TopLevel = true,
			OneShot = true,
			Emitting = true,
			Amount = 12,
			Lifetime = 1.05f,
			Explosiveness = 0.2f,
			Randomness = 0.6f,
			LocalCoords = false,
			Texture = HextechTextures.LoadVanillaTexture("res://images/vfx/characters/regent_sparkle.png") ?? GetGlowTexture(),
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
			EmissionRectExtents = new Vector2(width * 0.45f, height * 0.35f),
			Direction = new Vector2(0f, -1f),
			Spread = 12f,
			InitialVelocityMin = height * 0.22f,
			InitialVelocityMax = height * 0.45f,
			Gravity = new Vector2(0f, -height * 0.1f),
			ScaleAmountMin = 0.5f,
			ScaleAmountMax = 1.1f,
			Modulate = DivineDustColor,
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
			ColorRamp = new Gradient
			{
				Offsets = [0f, 0.2f, 0.75f, 1f],
				Colors = [Colors.White with { A = 0f }, Colors.White, Colors.White, Colors.White with { A = 0f }]
			}
		};
		parent.AddChildSafely(dust);
		dust.GlobalPosition = new Vector2(bottom.X, bottom.Y - height * 0.45f);

		Tween tween = dust.CreateTween();
		tween.TweenInterval(2.2f);
		tween.TweenCallback(Callable.From(() => FreeNode(dust)));
	}

	private static void RunSoulDrain(Creature source, Creature destination, int wispCount)
	{
		try
		{
			NCreature? destNode = HextechCreatureNodeRegistry.TryGet(destination);
			if (destNode == null)
			{
				return;
			}

			Node? parent = destNode.GetParent();
			if (!GodotObject.IsInstanceValid(parent))
			{
				return;
			}

			Vector2 destPos = CreatureCenter(destNode);
			float width = CreatureWidth(destNode);
			NCreature? sourceNode = HextechCreatureNodeRegistry.TryGet(source);
			Vector2 sourcePos = sourceNode != null ? CreatureCenter(sourceNode) : destPos;

			// 魂色取死者立绘的平均色(魂是"它的"魂),失败回退幽青。全程纯敌人色,不混白:
			// 需要"更亮"的部件用同色相提满明度(Brighten),保持色彩纯度。
			Color tint = GetSoulTint(source);
			Color brightTint = Brighten(tint);

			if (sourceNode != null)
			{
				// 亡魂自敌人身上被抽离的一瞬。
				SpawnFlash(parent!, sourcePos, width * 0.85f, brightTint, 0.35f, 0.5f, aboveCreaturesOnly: true);
			}

			// 魂的缕数按死者身份分级(在死亡瞬间由调用方算好传入):小怪 1-2、精英 3-4、BOSS 5-6。
			// 主魂大而稳、带到达闪光;其余各缕尺寸/弧线/节奏随机错开,鱼贯飘入。
			SpawnSoulWisp(parent!, sourcePos, destPos, width * 0.5f, 0.62f, 0f, -width * 0.9f, tint, () =>
			{
				if (GodotObject.IsInstanceValid(parent))
				{
					SpawnFlash(parent!, destPos, width * 1.0f, brightTint, 0.35f, 0.5f, aboveCreaturesOnly: true);
					SpawnRing(parent!, destPos, width * 0.2f, width * 1.05f, 0.4f, 0.8f, tint, aboveCreaturesOnly: true);
				}
			});
			for (int i = 1; i < wispCount; i++)
			{
				float diameter = width * (0.24f + VisualRng.NextSingle() * 0.1f);
				float duration = 0.64f + VisualRng.NextSingle() * 0.16f;
				float delay = 0.03f + i * 0.05f;
				float arcLift = -width * (0.5f + VisualRng.NextSingle() * 0.9f);
				SpawnSoulWisp(parent!, sourcePos, destPos, diameter, duration, delay, arcLift, i % 2 == 0 ? tint : brightTint, null);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"Soul drain failed: {ex.Message}");
		}
	}
}
