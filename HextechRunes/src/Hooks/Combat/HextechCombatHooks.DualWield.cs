using System.Runtime.CompilerServices;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	// AttackCommand._damagePerHit（decimal 白值）与 _hitCount（int 段数），0.107.1/0.111.0 原版私有字段。
	// 任一缺失时攻击改写与意图预览一并停用（Prepare 返回 false），缺失成员进启动摘要。
	private static readonly FieldInfo DualWieldDamagePerHitField = TryGetField(typeof(AttackCommand), "_damagePerHit")!;
	private static readonly FieldInfo DualWieldHitCountField = TryGetField(typeof(AttackCommand), "_hitCount")!;
	private static readonly ConditionalWeakTable<AttackCommand, object> DualWieldProcessedCommands = new();
	private static readonly object DualWieldProcessedMarker = new();

	private static bool DualWieldFieldsAvailable => DualWieldDamagePerHitField != null && DualWieldHitCountField != null;

	// 敌方「双刀流」:敌人攻击伤害白值减半(向上取整)、段数加倍。直接改写 AttackCommand 的
	// _damagePerHit(白值)与 _hitCount(段数),不碰伤害系数——力量等加成仍在减半后的白值上叠加。
	[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute), typeof(PlayerChoiceContext))]
	[HextechPatch("combat.dual-wield.attack", "敌方双刀流")]
	private static class DualWieldAttackPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => DualWieldFieldsAvailable;

		[HarmonyPrefix]
		private static void Prefix(AttackCommand __instance)
		{
			if (!TryGetActiveEnemyHexModifier(__instance.Attacker, MonsterHexKind.DualWield, out _))
			{
				return;
			}

			// 同一攻击命令只处理一次,避免重入/重复执行时反复减半加段。
			if (DualWieldProcessedCommands.TryGetValue(__instance, out _))
			{
				return;
			}

			DualWieldProcessedCommands.Add(__instance, DualWieldProcessedMarker);

			// 计算型伤害(_damagePerHit < 0,改用 _calculatedDamageVar)不在此减半,只加倍段数。
			if (DualWieldDamagePerHitField.GetValue(__instance) is decimal damagePerHit && damagePerHit >= 1m)
			{
				DualWieldDamagePerHitField.SetValue(__instance, Math.Ceiling(damagePerHit / 2m));
			}

			if (DualWieldHitCountField.GetValue(__instance) is int hitCount && hitCount >= 1)
			{
				DualWieldHitCountField.SetValue(__instance, hitCount * 2);
			}
		}
	}
}
