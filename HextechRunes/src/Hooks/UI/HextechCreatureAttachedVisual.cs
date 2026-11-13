using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

/// <summary>
/// 跟随战斗生物节点的纯表现附件:每帧驱动一次、生物或自身节点失效即停、结束时释放自己挂出的节点。
/// 常驻附件经 <see cref="TryAttach{TVisual}"/> 按"附件类型 + 生物节点"去重;一次性特效直接 <see cref="Launch"/>。
/// </summary>
/// <remarks>
/// 驱动方式:在 <see cref="FrameNode"/> 上逐帧等待 <c>ProcessFrame</c>,首帧在挂载调用中同步执行。
/// 每帧先按 <see cref="HextechAttachmentLifetime"/> 判定是否继续:生物节点失效或离开场景树、
/// 所属战斗房间不再是当前房间、(常驻附件)本场战斗已结束,任一成立即结束并释放。
/// 战败后原版的结算界面盖在战斗房间之上,房间与存活的生物节点会一直留在场景树里,只靠节点有效性无法结束循环。
/// 异常只记日志,绝不外抛(附件由战斗构建与召唤同步链触发)。
/// </remarks>
internal abstract class HextechCreatureAttachedVisual
{
	private static readonly HashSet<(Type Visual, ulong CreatureNode)> ActiveAttachments = [];

	private readonly string _logTag;
	private readonly string _displayName;
	private readonly NCombatRoom? _ownerRoom;
	private HextechAttachmentLifetime _lifetime;

	protected HextechCreatureAttachedVisual(NCreature creature, string logTag, string displayName)
	{
		Creature = creature;
		_logTag = logTag;
		_displayName = displayName;
		_ownerRoom = FindOwnerRoom(creature);
	}

	protected NCreature Creature { get; }

	/// <summary>逐帧等待所依附的节点;为 null 或离开场景树即停止。</summary>
	protected abstract Node? FrameNode { get; }

	/// <summary>附件是否仍应继续运行;默认要求生物节点与 <see cref="FrameNode"/> 都有效。</summary>
	protected virtual bool IsAlive => GodotObject.IsInstanceValid(Creature) && GodotObject.IsInstanceValid(FrameNode);

	/// <summary>常驻附件是否在本场战斗结束(胜利或战败结算)时结束;房间切换与节点失效始终结束。</summary>
	protected virtual bool EndsWithCombat => true;

	/// <summary>创建节点;返回 false 表示本次不挂(资源缺失等),不会进入逐帧循环。</summary>
	protected abstract bool Start();

	/// <summary>每帧逻辑;返回 false 结束附件。</summary>
	protected abstract bool Tick();

	/// <summary>释放附件挂出的节点;循环结束(含异常)时调用一次。</summary>
	protected abstract void Release();

	/// <summary>各附件共用的逐帧步长:按 120fps ~ 20fps 夹取,避免卡顿后动画跳变。</summary>
	protected static float ClampedFrameDelta(Node node)
	{
		return Mathf.Clamp((float)node.GetProcessDeltaTime(), 1f / 120f, 0.05f);
	}

	/// <summary>
	/// 常驻附件的挂载入口:节点就绪且 <paramref name="accepts"/> 通过时,每个生物节点每种附件只挂一份。
	/// 宿主会在多个时机重复调用,这里自行去重并容忍失效节点。
	/// </summary>
	protected static void TryAttach<TVisual>(
		NCreature? creature,
		string logTag,
		string displayName,
		Func<NCreature, bool> accepts,
		Func<NCreature, TVisual> create)
		where TVisual : HextechCreatureAttachedVisual
	{
		try
		{
			if (!GodotObject.IsInstanceValid(creature) || !creature.IsNodeReady() || !accepts(creature))
			{
				return;
			}

			(Type, ulong) key = (typeof(TVisual), creature.GetInstanceId());
			if (!ActiveAttachments.Add(key))
			{
				return;
			}

			TVisual visual = create(creature);
			if (!visual.Start())
			{
				// 资源缺失等半途失败:已挂出的节点一并带走,下次时机还能重试。
				visual.Release();
				ActiveAttachments.Remove(key);
				return;
			}

			TaskHelper.RunSafely(visual.RunAsync(key));
		}
		catch (Exception ex)
		{
			HextechLog.Warn(logTag, $"Could not attach {displayName}: {ex.Message}");
		}
	}

	/// <summary>一次性特效的入口:不去重,<see cref="Start"/> 成功后逐帧运行到 <see cref="Tick"/> 返回 false。</summary>
	protected static void Launch(HextechCreatureAttachedVisual visual)
	{
		if (!visual.Start())
		{
			visual.Release();
			return;
		}

		TaskHelper.RunSafely(visual.RunAsync(key: null));
	}

	private async Task RunAsync((Type, ulong)? key)
	{
		try
		{
			// 一次性特效(无去重键)自带时长,不随战斗结束提前收掉。
			bool endsWithCombat = key != null && EndsWithCombat;
			while (ShouldContinue(endsWithCombat) && Tick())
			{
				if (FrameNode is not { } frameNode || !await HextechGodotAsync.AwaitProcessFrameAsync(frameNode))
				{
					return;
				}
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn(_logTag, $"{_displayName} stopped after runtime error: {ex.Message}");
		}
		finally
		{
			Release();
			if (key is { } activeKey)
			{
				ActiveAttachments.Remove(activeKey);
			}
		}
	}

	private bool ShouldContinue(bool endsWithCombat)
	{
		if (!IsAlive)
		{
			return false;
		}

		bool? roomIsCurrent = _ownerRoom == null
			? null
			: GodotObject.IsInstanceValid(_ownerRoom) && _ownerRoom.IsInsideTree() && ReferenceEquals(NCombatRoom.Instance, _ownerRoom);
		return _lifetime.Observe(
			creatureInTree: Creature.IsInsideTree(),
			combatInProgress: CombatManager.Instance.IsInProgress,
			roomIsCurrent: roomIsCurrent,
			endsWithCombat: endsWithCombat);
	}

	private static NCombatRoom? FindOwnerRoom(NCreature creature)
	{
		for (Node? node = creature.GetParent(); node != null; node = node.GetParent())
		{
			if (node is NCombatRoom room)
			{
				return room;
			}
		}

		return null;
	}
}

/// <summary>
/// 生物附件逐帧循环的退出判定(不依赖节点,可单测)。战斗进行与房间为当前房间都按"先见到再失去"触发:
/// 附件常在原版开战(<c>IsInProgress</c> 置真)之前挂上,挂载那一刻的房间指针也可能尚未切换,
/// 只有曾经成立、之后不再成立才算战斗结束或房间切换。
/// </summary>
internal struct HextechAttachmentLifetime
{
	private bool _sawCombatInProgress;
	private bool _sawRoomCurrent;

	/// <param name="creatureInTree">生物节点仍在场景树里(节点有效性由调用方先判)。</param>
	/// <param name="combatInProgress">原版 <c>CombatManager.IsInProgress</c>;胜利与战败结算都会把它置假。</param>
	/// <param name="roomIsCurrent">生物所属战斗房间仍有效、在树且是当前战斗房间;生物不在战斗房间下时为 null,不参与判定。</param>
	/// <param name="endsWithCombat">常驻附件随战斗结束而结束;一次性特效为 false。</param>
	internal bool Observe(bool creatureInTree, bool combatInProgress, bool? roomIsCurrent, bool endsWithCombat)
	{
		if (!creatureInTree)
		{
			return false;
		}

		if (roomIsCurrent is { } current)
		{
			if (current)
			{
				_sawRoomCurrent = true;
			}
			else if (_sawRoomCurrent)
			{
				return false;
			}
		}

		if (combatInProgress)
		{
			_sawCombatInProgress = true;
		}
		else if (_sawCombatInProgress && endsWithCombat)
		{
			return false;
		}

		return true;
	}
}

/// <summary>
/// 画在生物身后的附件:根节点挂进生物父容器里共用的身后层 <c>HextechRunes_BehindCreatures</c>(与生物同一父节点与坐标系),
/// 每帧维持该层在最底,结束时释放根节点。
/// </summary>
internal abstract class HextechBehindCreatureVisual : HextechCreatureAttachedVisual
{
	private const string BehindCreaturesLayerName = "HextechRunes_BehindCreatures";

	private Node2D? _renderLayer;

	protected HextechBehindCreatureVisual(NCreature creature, string logTag, string displayName)
		: base(creature, logTag, displayName)
	{
	}

	protected Node2D? Root { get; private set; }

	protected override Node? FrameNode => Root;

	/// <summary>在生物父节点的身后层里建根节点;父节点失效时返回 false。</summary>
	protected bool TryCreateRoot(string name, bool visible)
	{
		Node2D? renderLayer = GetOrCreateBehindCreaturesLayer(Creature.GetParent());
		if (renderLayer == null)
		{
			return false;
		}

		_renderLayer = renderLayer;
		Root = new Node2D
		{
			Name = name,
			Visible = visible,
			ShowBehindParent = false,
			TopLevel = false,
			ZAsRelative = true,
			ZIndex = 0
		};
		renderLayer.AddChildSafely(Root);
		EnsureRenderOrder();
		return true;
	}

	protected void EnsureRenderOrder()
	{
		MoveLayerToBack(_renderLayer);
	}

	/// <summary>同一父容器下所有身后附件共用一个身后层;父节点失效时返回 null。</summary>
	private static Node2D? GetOrCreateBehindCreaturesLayer(Node? renderParent)
	{
		if (!GodotObject.IsInstanceValid(renderParent))
		{
			return null;
		}

		Node2D? layer = renderParent.GetNodeOrNull<Node2D>(BehindCreaturesLayerName);
		if (!GodotObject.IsInstanceValid(layer))
		{
			layer = new Node2D
			{
				Name = BehindCreaturesLayerName,
				ShowBehindParent = false,
				TopLevel = false,
				ZAsRelative = true,
				ZIndex = 0
			};
			renderParent.AddChildSafely(layer);
		}

		MoveLayerToBack(layer);
		return layer;
	}

	private static void MoveLayerToBack(Node2D? layer)
	{
		if (!GodotObject.IsInstanceValid(layer)
			|| layer.GetParent() is not Node renderParent
			|| !GodotObject.IsInstanceValid(renderParent)
			|| layer.GetIndex() == 0)
		{
			return;
		}

		renderParent.MoveChildSafely(layer, 0);
	}

	protected override void Release()
	{
		if (GodotObject.IsInstanceValid(Root))
		{
			Root.QueueFree();
		}
	}
}

/// <summary>身后光环/特效的一层贴图:<see cref="Plane"/> 负责缩放,<see cref="Sprite"/> 负责旋转与着色。</summary>
internal sealed record HextechAuraLayer(
	Node2D Plane,
	Sprite2D Sprite,
	Texture2D? ScaleBasis = null,
	ShaderMaterial? FlowMaterial = null)
{
	internal static HextechAuraLayer Create(Node2D parent, string name, Texture2D texture, Color modulate, bool additive = false)
	{
		Node2D plane = CreatePlane(parent, name);
		Sprite2D sprite = new()
		{
			Name = "Texture",
			Texture = texture,
			Centered = true,
			Modulate = modulate
		};
		if (additive)
		{
			sprite.Material = CreateAdditiveMaterial();
		}

		plane.AddChildSafely(sprite);
		return new HextechAuraLayer(plane, sprite);
	}

	internal static HextechAuraLayer? TryCreate(Node2D parent, string name, string path, Color modulate, bool additive = false)
	{
		Texture2D? texture = HextechTextures.LoadUiTexture(path);
		return texture == null ? null : Create(parent, name, texture, modulate, additive);
	}

	internal static Node2D CreatePlane(Node2D parent, string name)
	{
		Node2D plane = new()
		{
			Name = name,
			ZIndex = 0,
			ZAsRelative = true
		};
		parent.AddChildSafely(plane);
		return plane;
	}

	internal static CanvasItemMaterial CreateAdditiveMaterial()
	{
		return new CanvasItemMaterial
		{
			BlendMode = CanvasItemMaterial.BlendModeEnum.Add
		};
	}

	/// <summary>把该层缩放到 <paramref name="width"/> × <paramref name="height"/>(按 <see cref="ScaleBasis"/>,缺省为贴图本身)。</summary>
	internal static void Scale(HextechAuraLayer? layer, float width, float height)
	{
		Texture2D? texture = layer?.ScaleBasis ?? layer?.Sprite.Texture;
		if (layer == null || texture == null)
		{
			return;
		}

		layer.Plane.Scale = new Vector2(width / Math.Max(texture.GetWidth(), 1), height / Math.Max(texture.GetHeight(), 1));
	}
}
