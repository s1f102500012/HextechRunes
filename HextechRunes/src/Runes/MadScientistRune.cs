using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

public sealed class MadScientistRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbSlots", 1m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsDefectPlayer(player);
	}

	public override async Task AfterOrbChanneled(PlayerChoiceContext choiceContext, Player player, OrbModel orb)
	{
		if (player != Owner || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await OrbCmd.AddSlots(Owner, DynamicVars["OrbSlots"].IntValue);
	}

	// 原版充能球上限就是 10 个：球数超过 10 时，把每个球触发后的表现等待按 10/球数 压缩，
	// 整串触发的总时长与 10 个球时相同。只改本机表现等待，不改结算。
	private const int VanillaOrbSlotCap = 10;

	internal static float GetOrbWaitScale(Player? player)
	{
		if (player?.GetRelic<MadScientistRune>() == null || player.PlayerCombatState?.OrbQueue is not OrbQueue queue)
		{
			return 1f;
		}

		return ComputeOrbWaitScale(queue.Orbs.Count);
	}

	internal static float ComputeOrbWaitScale(int orbCount)
	{
		return orbCount > VanillaOrbSlotCap ? (float)VanillaOrbSlotCap / orbCount : 1f;
	}

	// 与原版同口径的触发后等待（本机玩家 CustomScaledWait(0.1, 0.25)，其他玩家 Wait(0.05)），时长乘 scale。
	private static Task ScaledOrbWait(Player owner, float scale)
	{
		return LocalContext.IsMe(owner)
			? Cmd.CustomScaledWait(0.1f * scale, 0.25f * scale)
			: Cmd.Wait(0.05f * scale);
	}

	// 跳过理由：0.110 起每个球的被动由 OrbModel.TriggerPassive 触发，每次触发后的表现等待写死在方法内部，没有 Hook 可改。
	// 激活条件：持有本符文且球数超过 10 时，用同一流程（触发次数 Hook → 逐次 Passive → 等待）替换，只把等待按比例缩短；
	// 其余情况走原版。原方法 IL 由 vanilla_copy_guard 冻结。
	[HarmonyPatch(typeof(OrbModel), nameof(OrbModel.TriggerPassive))]
	[HextechPatch("rune.mad-scientist.orb-wait", "科学狂人", Rune = typeof(MadScientistRune))]
	private static class MadScientistOrbWaitPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(OrbModel __instance, PlayerChoiceContext choiceContext, Creature? target, ref Task __result)
		{
			float scale = GetOrbWaitScale(__instance.Owner);
			if (scale >= 1f)
			{
				return true;
			}

			__result = TriggerPassiveScaled(__instance, choiceContext, target, scale);
			return false;
		}

		private static async Task TriggerPassiveScaled(OrbModel orb, PlayerChoiceContext choiceContext, Creature? target, float scale)
		{
			HextechCombatState combatState = orb.Owner.Creature.CombatState;
			int triggerCount = MegaCrit.Sts2.Core.Hooks.Hook.ModifyOrbPassiveTriggerCount(combatState, orb, 1, out List<AbstractModel> modifyingModels);
			await MegaCrit.Sts2.Core.Hooks.Hook.AfterModifyingOrbPassiveTriggerCount(combatState, orb, modifyingModels);
			for (int i = 0; i < triggerCount; i++)
			{
				await orb.Passive(choiceContext, target);
				await ScaledOrbWait(orb.Owner, scale);
			}
		}
	}

	// 跳过理由：原版 OrbCmd.AddSlots(三个版本一致)写死 amount = Min(10 - Capacity, amount)，充能球栏位上限 10
	// 没有任何 Hook 可改。激活条件：仅对持有本符文的玩家替换为不封顶的同一流程(IsOverOrEnding 判断 → AddCapacity →
	// 栏位动画)，其他玩家走原版；战斗外(PlayerCombatState 为空)回落原版。原方法 IL 由 vanilla_copy_guard 冻结。
	[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.AddSlots), typeof(Player), typeof(int))]
	[HextechPatch("rune.mad-scientist", "科学狂人", Rune = typeof(MadScientistRune))]
	private static class MadScientistPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Player player, int amount, ref Task __result)
		{
			if (player.GetRelic<MadScientistRune>() == null)
			{
				return true;
			}

			if (CombatManager.Instance.IsOverOrEnding || amount <= 0)
			{
				__result = Task.CompletedTask;
				return false;
			}

			if (player.PlayerCombatState == null)
			{
				return true;
			}

			player.PlayerCombatState.OrbQueue.AddCapacity(amount);
			try
			{
				NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager?.AddSlotAnim(amount);
			}
			catch (Exception ex)
			{
				HextechLog.Warn("MadScientist", $"Orb slot visual failed: {ex.Message}");
			}
			__result = Task.CompletedTask;
			return false;
		}
	}
}
