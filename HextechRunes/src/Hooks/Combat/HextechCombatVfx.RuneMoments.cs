using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace HextechRunes;

// 固态时间、万用瞄准镜系列、珠光护手、收集者的触发特效，以及不动如山、基石猎人、霸符兄弟的开战演出。纯表现层：只新建可视节点，不读写战斗状态、不用共享 RNG；
// 节点在主线程读写，取不到卡牌或生物节点时安全跳过。挂在卡牌上的特效作为 NCard 的子节点，卡牌移动时跟着走。
internal static partial class HextechCombatVfx
{
	private static readonly Color JewelSparkColor = new(1f, 0.96f, 0.86f);
	private static readonly Color ScopeColor = new(1f, 0.3f, 0.2f);
	private static readonly Color ScopeFlashColor = new(1f, 0.72f, 0.6f);
	private static readonly Color CollectorGold = new(1f, 0.78f, 0.28f);
	private static readonly Color CollectorGoldCore = new(1f, 0.95f, 0.72f);
	private static readonly Color SolidTimeFrost = new(0.6f, 0.88f, 1f);
	private static readonly Color SolidTimeHand = new(1f, 0.86f, 0.45f);

	// 一次最多展示最近存入的这么多张能力牌；更早的照常结算，只是不单独飞入。
	private const int SolidTimeMaxShownCards = 7;
	private const float SolidTimeCardSpacing = 190f;

	/// <summary>珠光护手判定重放：彩虹镜片光环与宝石星芒在这张牌上闪一下。</summary>
	internal static void JeweledGauntletSparkle(CardModel card)
	{
		RunOnMainThread("jeweled-gauntlet", () =>
		{
			Node2D? anchor = AttachToCard(card, "HextechRunes_JeweledGauntletVfx");
			if (anchor == null)
			{
				return;
			}

			Texture2D? lens = HextechTextures.LoadUiTexture(HextechAssets.JeweledGauntletLensPath);
			Texture2D? star = HextechTextures.LoadUiTexture(HextechAssets.StarSparkPath);
			AddLocalFlash(anchor, Vector2.Zero, 300f, JewelSparkColor, 0.3f, 0.32f);
			if (lens != null)
			{
				Sprite2D ring = AddLocalSprite(anchor, lens, Vector2.Zero, 220f, Colors.White with { A = 0.95f });
				Tween tween = ring.CreateTween().SetParallel(true);
				tween.TweenProperty(ring, "scale", ring.Scale * 2.3f, 0.5f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
				tween.TweenProperty(ring, "rotation", 0.6f, 0.5f);
				tween.TweenProperty(ring, "modulate:a", 0f, 0.5f).SetEase(Tween.EaseType.In);
			}

			if (star != null)
			{
				PopStar(anchor, star, new Vector2(70f, -120f), 360f, 0f, 0.42f);
				PopStar(anchor, star, new Vector2(-110f, -30f), 150f, 0.07f, 0.32f);
				PopStar(anchor, star, new Vector2(95f, 85f), 120f, 0.13f, 0.3f);
				PopStar(anchor, star, new Vector2(-55f, 150f), 100f, 0.19f, 0.28f);
			}

			FreeAfter(anchor, 0.8f);
		});
	}

	/// <summary>瞄准镜返还：准星锁定这张牌后爆闪，随牌一起飞回手牌并淡出。</summary>
	internal static void ScopeLockOn(CardModel card)
	{
		RunOnMainThread("universal-scope", () =>
		{
			Node2D? anchor = AttachToCard(card, "HextechRunes_UniversalScopeVfx");
			if (anchor == null)
			{
				return;
			}

			const float Radius = 150f;
			Node2D reticle = new() { Name = "Reticle" };
			anchor.AddChildSafely(reticle);
			if (HextechTextures.LoadUiTexture(HextechAssets.SoftRingEffectPath) is { } ringTexture)
			{
				AddLocalSprite(reticle, ringTexture, Vector2.Zero, Radius * 2f, ScopeColor with { A = 0.9f });
			}

			for (int i = 0; i < 4; i++)
			{
				Vector2 direction = Vector2.Right.Rotated(Mathf.Pi * 0.5f * i);
				reticle.AddChildSafely(MakeLocalLine(direction * Radius * 0.6f, direction * Radius * 1.12f, 8f, ScopeColor));
			}

			AddLocalSprite(reticle, GetGlowTexture(), Vector2.Zero, 30f, ScopeColor);
			reticle.Scale = Vector2.One * 1.9f;
			reticle.Rotation = 0.8f;
			reticle.Modulate = Colors.White with { A = 0f };

			Tween tween = reticle.CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(reticle, "scale", Vector2.One, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
			tween.TweenProperty(reticle, "rotation", 0f, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
			tween.TweenProperty(reticle, "modulate:a", 1f, 0.08f);
			tween.Chain().TweenCallback(Callable.From(() =>
			{
				if (GodotObject.IsInstanceValid(anchor))
				{
					AddLocalFlash(anchor, Vector2.Zero, 280f, ScopeFlashColor, 0.28f, 0.55f);
				}
			}));
			tween.Chain().TweenProperty(reticle, "modulate:a", 0f, 0.28f).SetEase(Tween.EaseType.In);
			tween.Parallel().TweenProperty(reticle, "scale", Vector2.One * 0.85f, 0.28f);
			FreeAfter(anchor, 0.8f);
		});
	}

	/// <summary>收集者处决：目标身上一道金色斩痕、金光与星芒；金币另由 <see cref="CollectorCoins"/> 在记账时爆出。</summary>
	internal static void CollectorExecute(Creature target)
	{
		RunOnMainThread("collector-execute", () =>
		{
			NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
			Node? parent = node?.GetParent();
			if (node == null || parent == null || !node.IsInsideTree())
			{
				return;
			}

			Vector2 center = CreatureCenter(node);
			float width = CreatureWidth(node);
			Vector2 half = new Vector2(width, width * 1.1f) * 0.5f;
			SpawnSlash(parent, center - half, center + half, width * 0.11f, CollectorGold, CollectorGoldCore);
			SpawnFlash(parent, center, width * 1.4f, CollectorGold, 0.35f, 0.6f, aboveCreaturesOnly: true);
			SpawnRing(parent, center, width * 0.35f, width * 1.3f, 0.4f, 0.85f, CollectorGold, aboveCreaturesOnly: true);
			if (HextechTextures.LoadUiTexture(HextechAssets.StarSparkPath) is { } star)
			{
				SpawnWorldStar(parent, star, center, width * 1.2f, CollectorGoldCore);
			}
		});
	}

	/// <summary>收集者记账：金币从目标身上爆开（原版小鬼佣兵的金币特效）并闪一下金色星芒。</summary>
	internal static void CollectorCoins(Creature target)
	{
		CoinBurst(target);
		RunOnMainThread("collector-coins", () =>
		{
			NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
			Node? parent = node?.GetParent();
			if (node == null || parent == null || !node.IsInsideTree()
				|| HextechTextures.LoadUiTexture(HextechAssets.StarSparkPath) is not { } star)
			{
				return;
			}

			SpawnWorldStar(parent, star, CreatureCenter(node), CreatureWidth(node) * 0.8f, CollectorGold);
		});
	}

	/// <summary>
	/// 固态时间开战：角色身边的表盘指针走完一圈，最近存入的能力牌在出牌区展开，随后时间恢复，
	/// 能力牌按原版打出能力牌的动画一起飞向角色。等到卡牌基本到位后返回，调用方再依次结算。
	/// 只在主线程播放；取不到节点（测试、无界面）时立即返回。
	/// </summary>
	internal static async Task SolidTimeResume(Creature owner, IReadOnlyList<CardModel> cards)
	{
		try
		{
			if (cards.Count == 0
				|| NCombatRoom.Instance == null
				|| !NGame.IsMainThread()
				|| HextechCreatureNodeRegistry.TryGet(owner) is not { } ownerNode
				|| !ownerNode.IsInsideTree()
				|| ownerNode.GetParent() is not { } parent
				|| NCombatRoom.Instance is not { } room)
			{
				return;
			}

			Vector2 center = CreatureCenter(ownerNode);
			float width = CreatureWidth(ownerNode);
			Node2D clock = SpawnClockFace(parent, center, width);

			List<NCard> shownCards = [];
			int shownCount = Math.Min(cards.Count, SolidTimeMaxShownCards);
			int firstShown = cards.Count - shownCount;
			for (int i = 0; i < shownCount; i++)
			{
				NCard? cardNode = NCard.Create(cards[firstShown + i]);
				if (cardNode == null)
				{
					continue;
				}

				room.CombatVfxContainer.AddChildSafely(cardNode);
				float offsetX = (i - (shownCount - 1) * 0.5f) * SolidTimeCardSpacing;
				cardNode.GlobalPosition = PileType.Play.GetTargetPosition(cardNode) + new Vector2(offsetX, 0f);
				HextechCombatVfxHooks.RegisterPowerFlyFanOffset(cardNode, offsetX);
				cardNode.UpdateVisuals(PileType.Play, CardPreviewMode.Normal);
				Tween pop = cardNode.CreateTween();
				pop.TweenProperty(cardNode, "scale", cardNode.Scale, 0.14f).From(Vector2.Zero)
					.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
				shownCards.Add(cardNode);
			}

			await Cmd.CustomScaledWait(0.35f, 0.6f);
			if (!GodotObject.IsInstanceValid(parent) || !GodotObject.IsInstanceValid(room))
			{
				return;
			}

			// 时间恢复：表盘碎开，卡牌飞向角色。
			SpawnRing(parent, center, width * 0.5f, width * 2.4f, 0.45f, 0.9f, SolidTimeFrost, aboveCreaturesOnly: true);
			SpawnFlash(parent, center, width * 1.7f, SolidTimeFrost, 0.35f, 0.6f, aboveCreaturesOnly: true);
			if (GodotObject.IsInstanceValid(clock))
			{
				Tween fade = clock.CreateTween().SetParallel(true);
				fade.TweenProperty(clock, "scale", clock.Scale * 1.35f, 0.25f).SetEase(Tween.EaseType.Out);
				fade.TweenProperty(clock, "modulate:a", 0f, 0.25f);
				fade.Chain().TweenCallback(Callable.From(() => FreeNode(clock)));
			}

			float flightDuration = 0f;
			foreach (NCard cardNode in shownCards)
			{
				if (!GodotObject.IsInstanceValid(cardNode))
				{
					continue;
				}

				if (NCardFlyPowerVfx.Create(cardNode) is not { } fly)
				{
					FreeNode(cardNode);
					continue;
				}

				room.CombatVfxContainer.AddChildSafely(fly);
				TaskHelper.RunSafely(fly.PlayAnim());
				flightDuration = Math.Max(flightDuration, fly.GetDuration());
			}

			if (flightDuration > 0f)
			{
				await Cmd.CustomScaledWait(flightDuration * 0.2f, flightDuration * 0.75f);
			}
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure("solid-time", ex);
		}
	}

	private static Node2D SpawnClockFace(Node parent, Vector2 center, float width)
	{
		Node2D clock = new() { Name = "HextechRunes_SolidTimeClock" };
		parent.AddChildSafely(clock);
		PlaceAboveCreatures(parent, clock);
		clock.GlobalPosition = center;

		float radius = width * 0.62f;
		if (HextechTextures.LoadUiTexture(HextechAssets.SoftRingEffectPath) is { } ringTexture)
		{
			AddLocalSprite(clock, ringTexture, Vector2.Zero, radius * 2.15f, SolidTimeFrost with { A = 0.85f });
		}

		for (int i = 0; i < 12; i++)
		{
			Vector2 direction = Vector2.Up.Rotated(Mathf.Tau * i / 12f);
			float inner = i % 3 == 0 ? 0.74f : 0.84f;
			clock.AddChildSafely(MakeLocalLine(direction * radius * inner, direction * radius * 0.97f, i % 3 == 0 ? 5f : 3f, SolidTimeFrost));
		}

		Node2D hand = new() { Name = "Hand" };
		clock.AddChildSafely(hand);
		hand.AddChildSafely(MakeLocalLine(Vector2.Zero, Vector2.Up * radius * 0.82f, 6f, SolidTimeHand));
		AddLocalSprite(clock, GetGlowTexture(), Vector2.Zero, radius * 0.22f, SolidTimeHand);

		clock.Modulate = Colors.White with { A = 0f };
		clock.Scale = Vector2.One * 0.8f;
		Tween appear = clock.CreateTween().SetParallel(true);
		appear.TweenProperty(clock, "modulate:a", 1f, 0.12f);
		appear.TweenProperty(clock, "scale", Vector2.One, 0.18f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		Tween sweep = hand.CreateTween();
		sweep.TweenProperty(hand, "rotation", Mathf.Tau, 0.5f).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
		return clock;
	}

	private static void SpawnSlash(Node parent, Vector2 fromGlobal, Vector2 toGlobal, float width, Color edge, Color core)
	{
		foreach ((float scale, Color color) in new[] { (1f, edge), (0.4f, core) })
		{
			Line2D slash = new()
			{
				Name = "HextechRunes_VfxSlash",
				TopLevel = true,
				Width = 0f,
				DefaultColor = color,
				BeginCapMode = Line2D.LineCapMode.Round,
				EndCapMode = Line2D.LineCapMode.Round,
				Points = [fromGlobal, toGlobal],
				WidthCurve = MakeSlashWidthCurve(),
				Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
			};
			parent.AddChildSafely(slash);
			PlaceAboveCreatures(parent, slash);
			Tween tween = slash.CreateTween();
			tween.TweenProperty(slash, "width", width * scale, 0.07f).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(slash, "width", 0f, 0.3f).SetEase(Tween.EaseType.In);
			tween.Parallel().TweenProperty(slash, "modulate:a", 0f, 0.3f).SetEase(Tween.EaseType.In);
			tween.TweenCallback(Callable.From(() => FreeNode(slash)));
		}
	}

	// 斩痕两端收尖、中间最宽。
	private static Curve MakeSlashWidthCurve()
	{
		Curve curve = new();
		curve.AddPoint(new Vector2(0f, 0.1f));
		curve.AddPoint(new Vector2(0.5f, 1f));
		curve.AddPoint(new Vector2(1f, 0.1f));
		return curve;
	}

	private static void SpawnWorldStar(Node parent, Texture2D star, Vector2 globalPos, float diameter, Color color)
	{
		Sprite2D sprite = MakeSprite(star, color with { A = 0f });
		parent.AddChildSafely(sprite);
		PlaceAboveCreatures(parent, sprite);
		sprite.GlobalPosition = globalPos;
		SetSpriteDiameter(sprite, diameter * 0.3f);
		Vector2 peakScale = sprite.Scale / 0.3f;
		Tween tween = sprite.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(sprite, "scale", peakScale, 0.12f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		tween.TweenProperty(sprite, "modulate:a", 1f, 0.08f);
		tween.TweenProperty(sprite, "rotation", 0.5f, 0.45f);
		tween.Chain().TweenProperty(sprite, "modulate:a", 0f, 0.3f).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() => FreeNode(sprite)));
	}

	// 星芒：先放大弹出，再旋转淡出。delay 用于错落出现。
	private static void PopStar(Node2D parent, Texture2D star, Vector2 localPos, float diameter, float delay, float duration)
	{
		Sprite2D sprite = AddLocalSprite(parent, star, localPos, diameter * 0.2f, JewelSparkColor with { A = 0f });
		Vector2 peakScale = sprite.Scale * 5f;
		Tween tween = sprite.CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(sprite, "modulate:a", 1f, 0.06f);
		tween.Parallel().TweenProperty(sprite, "scale", peakScale, duration * 0.35f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		tween.TweenProperty(sprite, "modulate:a", 0f, duration * 0.65f).SetEase(Tween.EaseType.In);
		tween.Parallel().TweenProperty(sprite, "rotation", 0.7f, duration * 0.65f);
	}

	/// <summary>在牌面中心挂一个跟随卡牌移动的锚点；卡牌不在桌面上时返回 null。</summary>
	private static Node2D? AttachToCard(CardModel card, string name)
	{
		NCard? cardNode = NCard.FindOnTable(card);
		if (cardNode == null || !GodotObject.IsInstanceValid(cardNode) || !cardNode.IsInsideTree())
		{
			return null;
		}

		Node2D anchor = new() { Name = name };
		cardNode.AddChildSafely(anchor);
		Control body = cardNode.Body;
		// 用牌面的全局变换求中心：Control 的全局矩形不含缩放，牌在出牌区通常是缩放过的。
		anchor.GlobalPosition = GodotObject.IsInstanceValid(body)
			? body.GetGlobalTransform() * (body.Size * 0.5f)
			: cardNode.GlobalPosition;
		return anchor;
	}

	private static Sprite2D AddLocalSprite(Node2D parent, Texture2D texture, Vector2 localPos, float diameter, Color modulate)
	{
		Sprite2D sprite = MakeSprite(texture, modulate);
		parent.AddChildSafely(sprite);
		sprite.Position = localPos;
		SetSpriteDiameter(sprite, diameter);
		return sprite;
	}

	private static void AddLocalFlash(Node2D parent, Vector2 localPos, float diameter, Color color, float duration, float peakAlpha)
	{
		Sprite2D flash = AddLocalSprite(parent, GetGlowTexture(), localPos, diameter, color with { A = 0f });
		Tween tween = flash.CreateTween();
		tween.TweenProperty(flash, "modulate:a", peakAlpha, duration * 0.3f).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(flash, "modulate:a", 0f, duration * 0.7f).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(() => FreeNode(flash)));
	}

	private static Line2D MakeLocalLine(Vector2 from, Vector2 to, float width, Color color)
	{
		return new Line2D
		{
			Width = width,
			DefaultColor = color,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Points = [from, to],
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
	}

	private static void FreeAfter(Node node, float seconds)
	{
		if (node.GetTree() is { } tree)
		{
			tree.CreateTimer(seconds).Timeout += () => FreeNode(node);
		}
	}

	private static void RunOnMainThread(string label, Action action)
	{
		void Guarded()
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				LogRuneVfxFailure(label, ex);
			}
		}

		try
		{
			// 测试进程与战斗外没有战斗房间：直接跳过，不触碰任何 Godot 单例。
			if (NCombatRoom.Instance == null)
			{
				return;
			}

			if (NGame.IsMainThread())
			{
				Guarded();
			}
			else
			{
				Callable.From(Guarded).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			LogRuneVfxFailure(label, ex);
		}
	}

	private static void LogRuneVfxFailure(string label, Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("visual." + label, 3))
		{
			HextechLog.Warn("CombatVfx", $"{label} vfx failed: {ex.GetType().Name}: {ex.Message}");
		}
	}
}
