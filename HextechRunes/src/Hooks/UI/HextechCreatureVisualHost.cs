using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

/// <summary>
/// 战斗内 creature 节点视觉附件的统一宿主:战斗房间就绪、召唤加入、节点就绪三个时机各只补一次,
/// 再按固定顺序把节点交给各附件的 <c>TryAttach</c>。新增附件按是否依赖节点注册表加进
/// <see cref="AttachPreRegistry"/> 或 <see cref="AttachPostRegistry"/>,
/// 各附件自身不补丁这几个原版方法。
/// </summary>
/// <remarks>
/// 顺序即语义:光环类附件先挂,再重建节点注册表,最后挂依赖注册表的灼烧/玻璃大炮。
/// 附件的 <c>TryAttach</c> 必须自行容忍重复调用与失效节点。
/// </remarks>
internal static class HextechCreatureVisualHost
{
	private static void Attach(NCreature? node)
	{
		if (!GodotObject.IsInstanceValid(node))
		{
			return;
		}

		AttachPreRegistry(node);
		HextechCreatureNodeRegistry.Register(node);
		AttachPostRegistry(node);
	}

	/// <summary>在 entity → 节点映射重建之前挂的光环类附件。</summary>
	private static void AttachPreRegistry(NCreature node)
	{
		HandOfBaronAuraVisual.TryAttach(node);
		SlowCookAuraVisual.TryAttach(node);
		HextechNearDeathFeastVisual.TryAttach(node);
	}

	/// <summary>在 entity → 节点映射重建之后挂的附件,按灼烧、玻璃大炮的顺序。</summary>
	private static void AttachPostRegistry(NCreature node)
	{
		HextechBurnVisual.TryAttach(node);
		HextechGlassCannonHealthBarVisual.TryAttach(node);
	}

	[HarmonyPatch(typeof(NCombatRoom), "_Ready", new Type[0])]
	[HextechPatch("visual.host.room-ready", "战斗视觉附件")]
	private static class CombatRoomReadyPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCombatRoom __instance)
		{
			NCreature[] nodes = __instance.CreatureNodes.ToArray();
			foreach (NCreature node in nodes)
			{
				AttachPreRegistry(node);
			}

			// 新战斗重建 entity → 节点映射,再挂依赖映射的附件。
			HextechCreatureNodeRegistry.Clear();
			foreach (NCreature node in nodes)
			{
				HextechCreatureNodeRegistry.Register(node);
			}

			foreach (NCreature node in nodes)
			{
				AttachPostRegistry(node);
			}
		}
	}

	[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature), typeof(Creature))]
	[HextechPatch("visual.host.add-creature", "战斗视觉附件")]
	private static class AddCreaturePatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCombatRoom __instance, Creature creature)
		{
			// 原版 GetCreatureNode 只是在节点列表里按 Entity 查找，不会抛异常。
			Attach(__instance.GetCreatureNode(creature));
		}
	}

	[HarmonyPatch(typeof(NCreature), "_Ready", new Type[0])]
	[HextechPatch("visual.host.creature-ready", "战斗视觉附件")]
	private static class CreatureReadyPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCreature __instance)
		{
			Attach(__instance);
		}
	}
}
