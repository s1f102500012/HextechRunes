#if STS2_110_OR_NEWER
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Forms;

namespace HextechRunes;

internal static partial class HextechFormVfxSafetyHooks
{
	private static bool HasSymphonyOfWar(NCreatureVisuals visuals)
	{
		NCombatRoom? room = NCombatRoom.Instance;
		return room != null && room.CreatureNodes.Any(creatureNode =>
			ReferenceEquals(creatureNode.Visuals, visuals)
			&& creatureNode.Entity.Player?.GetRelic<SymphonyOfWarRune>() != null);
	}

	private static FormVfxKind GetFormVfxKind(NFormVfx formVfx)
	{
		return formVfx switch
		{
			NDemonFormVfx => FormVfxKind.Demon,
			NSerpentFormVfx => FormVfxKind.Serpent,
			_ => FormVfxKind.Other
		};
	}

	// 跳过型前缀：原版 AddFormVfx 直接往 %FormVfx 容器挂节点，缺容器即空引用，且没有"挂载形态特效"的 Hook。
	// 激活条件：容器缺失（跳过纯视觉挂载），或持有战争交响乐（按保留规则自行挂载）；其余情况走原版。
	// 默认 Priority.Low，让其他模组的前缀先执行；目标 IL 由原版拷贝守卫冻结（0.111.0）。
	[HarmonyPatch(typeof(NCreatureVisuals), nameof(NCreatureVisuals.AddFormVfx), typeof(NFormVfx))]
	[HextechPatch("compat.form-vfx.add", "形态特效容器安全")]
	private static class AddFormVfxPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NCreatureVisuals __instance, NFormVfx formVfx)
		{
			Control? holder = HextechCreatureVisualsCompat.GetFormVfxHolder(__instance);
			if (holder == null)
			{
				return false;
			}

			if (!HasSymphonyOfWar(__instance))
			{
				return true;
			}

			// 战争交响乐固定保留恶魔与群蛇两层视觉；其他形态之间仍维持原版的后到覆盖先到。
			FormVfxKind incoming = GetFormVfxKind(formVfx);
			foreach (Node child in holder.GetChildren())
			{
				FormVfxKind existing = child is NFormVfx existingFormVfx
					? GetFormVfxKind(existingFormVfx)
					: FormVfxKind.Other;
				if (!ShouldPreserveExistingForSymphony(incoming, existing))
				{
					child.Free();
				}
			}

			holder.AddChild(formVfx);
			formVfx.Position = Vector2.Zero;
			return false;
		}
	}

	// 跳过型前缀：原版 RemoveFormVfx 同样直接访问 %FormVfx 容器；只在容器缺失时跳过，Priority.Low。
	[HarmonyPatch(typeof(NCreatureVisuals), nameof(NCreatureVisuals.RemoveFormVfx), new Type[0])]
	[HextechPatch("compat.form-vfx.remove", "形态特效容器安全")]
	private static class RemoveFormVfxPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NCreatureVisuals __instance)
		{
			return HextechCreatureVisualsCompat.GetFormVfxHolder(__instance) != null;
		}
	}
}
#endif
