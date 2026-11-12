namespace HextechRunes;

public sealed class LoopUpgradeRune : CardUpgradeRuneBase<Loop>
{
	protected override bool IsAvailableForCharacter(Player player) => IsDefectPlayer(player);

	internal static async Task TriggerAll(LoopPower power, PlayerChoiceContext context, Player player)
	{
		if (player != power.Owner.Player || player.PlayerCombatState == null)
		{
			return;
		}

		// 与原版循环相同，每层直接触发一次被动，不额外套用回合末被动次数修正。
		await OrbSnapshotPassiveHelper.TriggerRounds(
			player,
			player.PlayerCombatState.OrbQueue.Orbs.ToArray(),
			power.Amount,
			orb => OrbCmd.Passive(context, orb, null));
	}

	// 跳过理由：原版 LoopPower.AfterPlayerTurnStart(0.107.1/0.111.0 反编译一致)写死只对 Orbs[0] 触发被动，
	// 每层一次、每次后 Cmd.Wait(0.25f)；Hook.ModifyOrbPassiveTriggerCount 只改单个球的次数，没有能改"触发哪些球"的 Hook。
	// 激活条件：仅 LoopPower 持有者拥有本符文时替换，其余玩家走原版；替换体按回合开始时的球快照逐个触发，
	// 未保留原版每次触发后的 0.25 秒表现等待。原方法 IL 由 vanilla_copy_guard 冻结，游戏更新漂移会在测试与启动日志显形。
	[HarmonyPatch(typeof(LoopPower), nameof(LoopPower.AfterPlayerTurnStart))]
	[HextechPatch("rune.loop.all-orbs", "升级循环", Rune = typeof(LoopUpgradeRune))]
	private static class LoopAllOrbsPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(LoopPower __instance, PlayerChoiceContext choiceContext, Player player, ref Task __result)
		{
			if (__instance.Owner.Player?.GetRelic<LoopUpgradeRune>() == null)
			{
				return true;
			}

			__result = TriggerAll(__instance, choiceContext, player);
			return false;
		}
	}
}
