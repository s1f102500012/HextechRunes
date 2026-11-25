#if STS2_114_OR_NEWER
using System.Runtime.CompilerServices;

namespace HextechRunes;

// 代偿（我方符文与敌方海克斯）靠“查询钩子记下拆分 → 原版紧接着的确认钩子确认”配对。
// 0.114.0 起原版先在 CreatureCmd.GetDamageResults 里试算（查询），再派发 BeforeDamageReceived，
// 最后在 ApplyDamageResults 里落实（确认）；卡面伤害预览也会试算。中间那一步里荆棘会再发一次伤害、再查询一次，
// 等待期间本机的卡面预览也可能查询，都会把“最近一次查询”的记录冲掉，延后的伤害就此丢失（预览还只发生在本机）。
// 所以试算一结束就把记录取走、挂在这次试算的结果对象上，落实开始时按结果对象确认：
// 预览的结果不会被落实，记录随结果对象一起回收；嵌套伤害有自己的结果对象，互不干扰。不依赖伤害命令的 AsyncLocal 作用域。
internal static partial class HextechCombatHooks
{
	private static readonly ConditionalWeakTable<DamageResult, List<Action>> CompensationConfirmationsByResult = new();

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
	[HextechPatch("combat.compensation.capture-split", "代偿 / 敌方海克斯:代偿", Rune = typeof(CompensationRune), MonsterHexes = [MonsterHexKind.Compensation])]
	private static class CompensationCaptureSplitPatch
	{
		[HarmonyPostfix]
		private static void Postfix(ICombatState? combatState, DamageResults __result)
		{
			List<Action>? confirmations = null;
			if (CompensationEnemyHex.TakeQuerySplitConfirmation() is { } enemyConfirmation)
			{
				(confirmations ??= []).Add(enemyConfirmation);
			}

			if (combatState != null)
			{
				foreach (Player player in combatState.Players)
				{
					if (player.GetRelic<CompensationRune>()?.TakeQuerySplitConfirmation() is { } confirmation)
					{
						(confirmations ??= []).Add(confirmation);
					}
				}
			}

			if (confirmations != null)
			{
				CompensationConfirmationsByResult.AddOrUpdate(__result.original, confirmations);
			}
		}
	}

	[HarmonyPatch(typeof(CreatureCmd), "ApplyDamageResults", typeof(IRunState), typeof(ICombatState), typeof(DamageResults))]
	[HextechPatch("combat.compensation.confirm-split", "代偿 / 敌方海克斯:代偿", Rune = typeof(CompensationRune), MonsterHexes = [MonsterHexKind.Compensation])]
	private static class CompensationConfirmSplitPatch
	{
		// 同步前缀：赶在原版扣血与本次伤害的受伤回调之前确认，与旧版本的确认时机一致。
		[HarmonyPrefix]
		private static void Prefix(DamageResults results)
		{
			if (!CompensationConfirmationsByResult.TryGetValue(results.original, out List<Action>? confirmations))
			{
				return;
			}

			CompensationConfirmationsByResult.Remove(results.original);
			foreach (Action confirmation in confirmations)
			{
				confirmation();
			}
		}
	}
}
#endif
