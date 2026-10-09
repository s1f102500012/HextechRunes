using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace HextechRunes;

/// <summary>
/// 事件驱动的战斗特效派发器。符文在其触发点(战斗逻辑、各端一致执行)调用这里的方法,
/// 由本类把可视节点延迟挂到对应 <see cref="NCreature"/> 上。纯表现层:只新建可视节点、不读写任何
/// gameplay/同步状态;取不到节点时安全跳过。<see cref="HextechCreatureNodeRegistry"/> 提供 entity→node 桥。
/// 延迟入口经 Callable.CallDeferred 推到主线程后不再等待（纯表现层，不能拖住战斗命令），
/// 对应 Run* 方法必须在顶层捕获并记录异常。
/// </summary>
internal static partial class HextechCombatVfx
{
	internal const float MagicMissileLaunchIntervalSeconds = 0.055f;
	internal const float MagicMissileBaseFlightSeconds = 0.28f;
	internal const float MagicMissileFlightStepSeconds = 0.025f;

	// 死亡之环使用幽绿色调。
	private static readonly Color DeathRingColor = new(0.24f, 0.96f, 0.45f);
	private static readonly Color DeathFlashColor = new(0.62f, 1f, 0.64f);
	private static readonly Color DivineRingColor = new(1f, 0.9f, 0.55f);
	private static readonly Color DivineFlashColor = new(1f, 0.97f, 0.82f);
	// 吞噬灵魂:幽青色亡魂。
	private static readonly Color SoulColor = new(0.42f, 0.95f, 0.82f);
	// 神圣干预:天降光柱与光尘。
	private static readonly Color DivineShaftColor = new(1f, 0.93f, 0.62f);
	private static readonly Color DivineDustColor = new(1f, 0.95f, 0.75f);

	// 仅表现层随机(路径弧度/粒子错落),不触碰联机决定论。
	private static readonly Random VisualRng = new();

	private static Texture2D? _glowTexture;
	private static Texture2D? _ringTexture;

	private static Vector2 Bezier(Vector2 from, Vector2 control, Vector2 to, float t)
	{
		return from.Lerp(control, t).Lerp(control.Lerp(to, t), t);
	}

	/// <summary>同色相提满明度:比原色亮一档但不发白,保持色彩纯度。</summary>
	private static Color Brighten(Color color)
	{
		color.ToHsv(out float hue, out float saturation, out float value);
		return Color.FromHsv(hue, saturation, 1f) with { A = color.A };
	}

	/// <summary>
	/// 魂的缕数:小怪 1-2、精英 3-4、BOSS 5-6;召唤物/随从(非主要敌人)按小怪算。
	/// 必须在死亡瞬间调用——死者被移出战斗后 CombatState 为 null,只会按小怪兜底。
	/// </summary>
	internal static int GetSoulWispCount(Creature source)
	{
		RoomType roomType = source.CombatState?.Encounter?.RoomType ?? RoomType.Monster;
		return roomType switch
		{
			RoomType.Boss when source.IsPrimaryEnemy => 5 + VisualRng.Next(2),
			RoomType.Elite when source.IsPrimaryEnemy => 3 + VisualRng.Next(2),
			_ => 1 + VisualRng.Next(2)
		};
	}

	/// <summary>
	/// 把特效节点插到父容器中最后一个 <see cref="NCreature"/> 之后:画在所有角色之上、
	/// 但不盖住同容器后续的战斗结算等 UI 节点(追加在末尾会盖过它们)。
	/// </summary>
	private static void PlaceAboveCreatures(Node parent, Node node)
	{
		int lastCreatureIndex = -1;
		int count = parent.GetChildCount();
		for (int i = 0; i < count; i++)
		{
			if (parent.GetChild(i) is NCreature)
			{
				lastCreatureIndex = i;
			}
		}

		if (lastCreatureIndex >= 0)
		{
			parent.MoveChild(node, Math.Min(lastCreatureIndex + 1, parent.GetChildCount() - 1));
		}
	}

	// 死者立绘平均色缓存(按怪物类型;节点暂不可用时不缓存失败)。
	private static readonly Dictionary<Type, Color?> MonsterTintCache = [];

	/// <summary>魂色=死者 Spine 立绘贴图的 alpha 加权平均色(抬亮压灰,魂要发光);失败回退幽青。</summary>
	private static Color GetSoulTint(Creature source)
	{
		try
		{
			if (source.Monster is not { } monster)
			{
				return SoulColor;
			}

			Type type = monster.GetType();
			if (!MonsterTintCache.TryGetValue(type, out Color? tint))
			{
				tint = ComputeMonsterAverageColor(source);
				if (tint.HasValue)
				{
					MonsterTintCache[type] = tint;
				}
			}

			return tint ?? SoulColor;
		}
		catch
		{
			return SoulColor;
		}
	}

	private static Color? ComputeMonsterAverageColor(Creature source)
	{
		NCreatureVisuals? visuals = HextechCreatureNodeRegistry.TryGet(source)?.Visuals;
		if (!GodotObject.IsInstanceValid(visuals))
		{
			return null;
		}

		// 图集文件名不由怪物类名决定(SoulNexus 实际使用 soulnexus.png)。
		// 沿当前立绘的 Spine 资源引用读取已加载纹理,同时支持多页图集。
		GodotObject? sprite = visuals!.SpineBody?.BoundObject;
		if (!GodotObject.IsInstanceValid(sprite) || !sprite!.HasMethod("get_skeleton_data_res")
			|| sprite.Call("get_skeleton_data_res").AsGodotObject() is not { } skeletonData
			|| !skeletonData.HasMethod("get_atlas_res")
			|| skeletonData.Call("get_atlas_res").AsGodotObject() is not { } atlas
			|| !atlas.HasMethod("get_textures"))
		{
			return null;
		}

		const int SampleSize = 32;
		float r = 0f, g = 0f, b = 0f, weight = 0f;
		foreach (Variant textureVariant in atlas.Call("get_textures").AsGodotArray())
		{
			if (textureVariant.AsGodotObject() is not Texture2D texture || !GodotObject.IsInstanceValid(texture))
			{
				continue;
			}

			using Image? image = texture.GetImage();
			if (image == null || image.IsEmpty() || (image.IsCompressed() && image.Decompress() != Error.Ok))
			{
				continue;
			}

			image.Resize(SampleSize, SampleSize, Image.Interpolation.Bilinear);
			for (int y = 0; y < SampleSize; y++)
			{
				for (int x = 0; x < SampleSize; x++)
				{
					Color pixel = image.GetPixel(x, y);
					r += pixel.R * pixel.A;
					g += pixel.G * pixel.A;
					b += pixel.B * pixel.A;
					weight += pixel.A;
				}
			}
		}

		if (weight < 1f)
		{
			return null;
		}

		new Color(r / weight, g / weight, b / weight).ToHsv(out float hue, out float saturation, out float value);
		return Color.FromHsv(hue, Mathf.Clamp(saturation, 0.3f, 0.8f), Mathf.Max(value, 0.8f));
	}

	/// <summary>死亡之环：幽绿光束连接施法者与目标，目标处播放光环与闪光。</summary>
	internal static void DeathRingLash(Creature source, Creature target)
	{
		Callable.From(() => RunDeathRingLash(source, target)).CallDeferred();
	}

	/// <summary>神圣干预：为受益玩家播放金色光柱、上升光尘与落地光环。</summary>
	internal static void DivinePulse(IReadOnlyList<Creature> allies)
	{
		Creature[] snapshot = [.. allies];
		Callable.From(() => RunDivinePulse(snapshot)).CallDeferred();
	}

	/// <summary>吞噬灵魂:幽青色亡魂从死亡的敌人身上被抽离、飘向并汇入施法者。</summary>
	internal static void SoulDrain(Creature source, Creature destination, int wispCount)
	{
		Callable.From(() => RunSoulDrain(source, destination, wispCount)).CallDeferred();
	}

	/// <summary>
	/// 红黑飞弹从玩家飞向目标;返回值只表示弹道是否真实抵达。取不到节点时视为已抵达，
	/// 让 headless/测试和纯逻辑环境继续结算;场景退出导致节点销毁时返回 false，阻止旧战斗补伤害。
	/// </summary>
	internal static Task<bool> PlayMagicMissile(Creature source, Creature target, int missileIndex)
	{
		return PlayMissile(source, target, missileIndex, MagicMissilePalette, "Magic missile");
	}

	/// <summary>蓝黄双生火焰沿相反弧线飞向同一随机目标，结算时序与魔法飞弹一致。</summary>
	internal static Task<bool> PlayTwinFlamesMissile(Creature source, Creature target, int missileIndex)
	{
		return PlayMissile(source, target, missileIndex, TwinFlamesPalette, "Twin Flames missile");
	}

	private static Task<bool> PlayMissile(Creature source, Creature target, int missileIndex, MissilePalette palette, string label)
	{
		try
		{
			NCreature? sourceNode = HextechCreatureNodeRegistry.TryGet(source);
			NCreature? targetNode = HextechCreatureNodeRegistry.TryGet(target);
			if (sourceNode == null || targetNode == null)
			{
				return Task.FromResult(true);
			}

			Node? parent = targetNode.GetParent();
			if (!GodotObject.IsInstanceValid(parent))
			{
				return Task.FromResult(true);
			}

			return SpawnMissile(
				parent!,
				CreatureCenter(sourceNode),
				CreatureCenter(targetNode),
				Mathf.Min(CreatureWidth(sourceNode), CreatureWidth(targetNode)),
				missileIndex,
				missileIndex * MagicMissileLaunchIntervalSeconds,
				palette);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("CombatVfx", $"{label} failed: {ex.Message}");
			return Task.FromResult(true);
		}
	}

	// ---- 回力OK镖:镖沿弧线依次扫过所有敌人再飞回 ----
	// 每敌 0.2s 与 CreatureCmd.Damage 内置的每次结算 0.2s 标准尾巴对齐:
	// 逻辑侧只需在首击前等待 FirstArrival,之后连续结算即可与镖同步。
	internal const float BoomerangFirstArrivalSeconds = 0.22f;
	internal const float BoomerangPerTargetSeconds = 0.2f;

	/// <summary>
	/// 回力OK镖:镖体(符文图标)自施法者掷出,弧线依次命中各敌人后飞回。
	/// <paramref name="roundTrip"/> 为 true 时回程逆序再次扫过每个敌人
	/// (最远处打个转折返),供"一来一回各结算一次伤害"的卡牌版对齐节奏。
	/// </summary>
	internal static void BoomerangSweep(Creature owner, IReadOnlyList<Creature> targets, Texture2D? boomerangTexture, bool roundTrip = false)
	{
		Creature[] snapshot = [.. targets];
		Callable.From(() => RunBoomerangSweep(owner, snapshot, boomerangTexture, roundTrip)).CallDeferred();
	}

	/// <summary>欧米伽:全场红色预警后,天降赤红审判光柱依次轰击每个敌人。</summary>
	internal static void OmegaJudgment(IReadOnlyList<Creature> targets)
	{
		Creature[] snapshot = [.. targets];
		Callable.From(() => RunOmegaJudgment(snapshot)).CallDeferred();
	}

	/// <summary>
	/// 飞身踢:处决瞬间的斩击冲击(原版 BigSlash 节点)+目标本体色爆闪;
	/// 约半秒后一缕绿色治疗光从击杀点弧线流回施法者(与尸体横飞的
	/// <see cref="FlyingKickCorpseLaunchDriver"/> 时序互补:踢击在前、横飞居中、光流殿后)。
	/// </summary>
	internal static void FlyingKickStrike(Creature target, Creature owner)
	{
		Callable.From(() => RunFlyingKickStrike(target, owner)).CallDeferred();
	}

	/// <summary>
	/// 尸爆术:尸体位置毒绿脓爆,飞溅的毒液弧线泼向每个存活敌人,命中处小型毒溅。
	/// 位置在调用当下快照(死亡链上节点随时被移除),取不到就退化为目标群中心上方起爆。
	/// </summary>
	internal static void CorpseBloomBurst(Creature source, IReadOnlyList<Creature> targets)
	{
		NCreature? sourceNode = HextechCreatureNodeRegistry.TryGet(source);
		Vector2? sourcePos = sourceNode != null && GodotObject.IsInstanceValid(sourceNode)
			? CreatureCenter(sourceNode)
			: null;
		Creature[] snapshot = [.. targets];
		Callable.From(() => RunCorpseBloomBurst(sourcePos, snapshot)).CallDeferred();
	}

	/// <summary>
	/// 量子计算:蓝紫预警环后量子光柱依次贯穿每个敌人(节拍与逐敌伤害结算对齐),
	/// 随后每个敌人放出一缕青绿数据流汇回施法者(对应吸血治疗)。
	/// </summary>
	internal static void QuantumPulse(Creature owner, IReadOnlyList<Creature> targets)
	{
		Creature[] snapshot = [.. targets];
		Callable.From(() => RunQuantumPulse(owner, snapshot)).CallDeferred();
	}

	// 原版小鬼佣兵偷钱时的金币爆炸场景;根节点是 NVfxParticleSystem,进树即播放、到时自行销毁。
	private const string CoinExplosionScenePath = "res://scenes/vfx/vfx_coin_explosion_regular.tscn";

	/// <summary>
	/// 夺金:被命中的敌人身上爆出原版小鬼佣兵偷钱时的金币特效(同一资源)。
	/// 命中当下实例化该场景,挂到目标生物的父节点并定位到碰撞框中心。
	/// 不用原版 PlayOnCreatureCenter:它会跳过已死目标,击杀那一击就没有金币。
	/// 读取节点坐标必须在主线程:Godot 在其他线程读全局坐标会返回原点,特效就跑到屏幕左上角;
	/// 不在主线程时把"读坐标 + 播放"整体推到主线程执行。
	/// </summary>
	internal static void CoinBurst(Creature target)
	{
		try
		{
			// 先查注册表:测试进程与非战斗场景没有节点,直接返回,不触碰任何 Godot 单例。
			if (HextechCreatureNodeRegistry.TryGet(target) == null)
			{
				return;
			}

			if (NGame.IsMainThread())
			{
				PlayCoinBurstNow(target);
			}
			else
			{
				Callable.From(() => PlayCoinBurstNow(target)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			LogCoinBurstFailure(ex);
		}
	}

	private static void PlayCoinBurstNow(Creature target)
	{
		try
		{
			// 与本模组其他实机验证过的特效同一挂法:挂到生物节点的父节点(角色所在的画布坐标系),
			// 定位到碰撞框中心,再调到所有角色之上。不挂 CombatVfxContainer、不用 Visuals 的 VfxSpawnPosition
			// 标记:这两者组合在实机上把金币放到了屏幕左上角。
			NCreature? node = HextechCreatureNodeRegistry.TryGet(target);
			Node? parent = node?.GetParent();
			if (node == null || parent == null || !node.IsInsideTree())
			{
				return;
			}

			PackedScene? scene = ResourceLoader.Load<PackedScene>(CoinExplosionScenePath, cacheMode: ResourceLoader.CacheMode.Reuse);
			if (scene?.Instantiate() is not Node2D burst)
			{
				return;
			}

			parent.AddChildSafely(burst);
			burst.GlobalPosition = CreatureCenter(node);
			PlaceAboveCreatures(parent, burst);
		}
		catch (Exception ex)
		{
			LogCoinBurstFailure(ex);
		}
	}

	private static void LogCoinBurstFailure(Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("visual.goldrend-coin-burst", 3))
		{
			HextechLog.Warn("Vfx", $"Goldrend coin burst failed: {ex.GetType().Name}: {ex.Message}");
		}
	}
}
