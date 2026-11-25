#if STS2_114_OR_NEWER
namespace HextechRunes;

// 0.114.0 起原版每次伤害先在 CreatureCmd.GetDamageResults 里试算（格挡与失血都以 dryRun 调用），
// 之后才派发 BeforeDamageReceived，再用试算结果落实。穿透必须在试算格挡时生效，否则多出的失血进不了结果：
// 所以在 GetDamageResults 入口登记穿透量，只在试算那次 DamageBlockInternal 里扣减可格挡伤害；
// 落实时原版传入的已经是扣减后的格挡量，不再处理。
internal static partial class HextechCombatHooks
{
	[HarmonyPatch(
		typeof(CreatureCmd),
		"GetDamageResults",
		typeof(IRunState),
		typeof(ICombatState),
		typeof(Creature),
		typeof(decimal),
		typeof(ValueProp),
		typeof(Creature),
		typeof(CardModel),
		typeof(CardPlay))]
	[HextechPatch("combat.piercing-thread.register", "穿刺之线")]
	private static class PiercingThreadRegisterPatch
	{
		// 卡面伤害预览（CreatureCmd.GetDamageResultsPreview）也走这个方法；预览不在伤害命令里，命令 ID 为 0，不登记。
		[HarmonyPrefix]
		private static void Prefix(ICombatState? combatState, Creature target, decimal damage, ValueProp props, Creature? dealer, CardModel? cardSource)
		{
			if (combatState == null || CurrentActualDamageCommandId == 0L)
			{
				return;
			}

			foreach (Player player in combatState.Players)
			{
				player.GetRelic<PiercingThreadRune>()?.RegisterPendingDamage(target, damage, props, dealer, cardSource);
			}
		}
	}

	[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal), typeof(decimal), typeof(ValueProp), typeof(bool))]
	[HextechPatch("combat.piercing-thread.block", "穿刺之线")]
	private static class DamageBlockPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.First)]
		private static void Prefix(Creature __instance, ref decimal amount, ValueProp props, bool dryRun)
		{
			if (dryRun
				&& PiercingThreadRune.TryTakeBlockableDamage(
					CurrentActualDamageCommandId,
					__instance,
					amount,
					props,
					out decimal blockableDamage))
			{
				amount = blockableDamage;
			}
		}
	}
}
#endif
