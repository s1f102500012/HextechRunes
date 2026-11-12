using VanillaPowerCmd = MegaCrit.Sts2.Core.Commands.PowerCmd;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static bool ShouldReplaceTemporaryShrinkWithPermanent(PowerModel power, decimal offset, Creature? applier)
	{
		return power is ShrinkPower
			&& power.Amount > 0
			&& offset < 0m
			&& power.Owner.Side == CombatSide.Player
			&& applier?.Side == CombatSide.Enemy
			&& power.Owner.GetPowerAmount<ArtifactPower>() <= 0
			&& IsVanillaFixActiveFor(power.Owner);
	}

	private static async Task<int> ReplaceTemporaryShrinkWithPermanent(
		PlayerChoiceContext choiceContext,
		PowerModel temporaryShrink,
		decimal permanentOffset,
		Creature? applier,
		CardModel? cardSource,
		bool silent)
	{
		Creature owner = temporaryShrink.Owner;
		await HextechPowerCmdCompat.Remove(temporaryShrink);
		ShrinkPower? permanentShrink = await HextechPowerCmdCompat.Apply<ShrinkPower>(
			choiceContext,
			owner,
			permanentOffset,
			applier,
			cardSource,
			silent);
		return permanentShrink?.Amount ?? 0;
	}

	// 跳过型前缀（已裁决保留，见 architecture.md）：原版缩小正层数是逐回合递减的临时缩小、负层数是永久缩小，
	// 敌人对已有临时缩小的玩家再施加永久缩小（负 offset）时，PowerCmd.ModifyAmount 直接相加，永久效果被临时层数抵掉。
	// 没有能把"这次叠加改成替换为永久缩小"的 Hook。只在本局启用海克斯、玩家无人工制品且施加者是敌人时，
	// 改为移除临时实例并施加等量永久缩小。目标 IL 由原版拷贝守卫冻结（0.107.1/0.111.0）。
	[HarmonyPatch(typeof(VanillaPowerCmd), nameof(VanillaPowerCmd.ModifyAmount), typeof(PlayerChoiceContext), typeof(PowerModel), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool))]
	[HextechPatch("combat.shrink-power", "缩小能力兼容")]
	private static class ModifyAmountPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(
			PlayerChoiceContext choiceContext,
			PowerModel power,
			decimal offset,
			Creature? applier,
			CardModel? cardSource,
			bool silent,
			ref Task<int> __result)
		{
			if (!ShouldReplaceTemporaryShrinkWithPermanent(power, offset, applier))
			{
				return true;
			}

			__result = ReplaceTemporaryShrinkWithPermanent(
				choiceContext,
				power,
				offset,
				applier,
				cardSource,
				silent);
			return false;
		}
	}
}
