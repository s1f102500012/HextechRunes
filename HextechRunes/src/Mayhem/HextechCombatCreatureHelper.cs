using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

internal static class HextechCombatCreatureHelper
{
	public static IReadOnlyList<Creature> GetAliveEnemies(HextechCombatState combatState)
	{
		return combatState.Enemies.Where(static creature => creature.IsAlive).ToList();
	}

	public static IReadOnlyList<Creature> GetAlivePlayerSideCreatures(HextechCombatState combatState)
	{
		return combatState.PlayerCreatures.Where(static creature => creature.IsAlive).ToList();
	}

	/// <summary>
	/// 清理因 PainfulStabsPower 滞留场上的死亡敌人:该 power 覆写了
	/// ShouldPowerBeRemovedAfterOwnerDeath/ShouldCreatureBeRemovedFromCombatAfterDeath,
	/// 持有者死后尸体留场继续排意图(攻击打不出但 debuff 意图照常生效)。
	/// 发疼痛戳刺的敌方海克斯(TestSubject)在 BeforeSideTurnStart 兜底调用。
	/// </summary>
	public static async Task CleanUpRetainedPainfulStabsEnemies(HextechCombatState combatState)
	{
		foreach (Creature enemy in combatState.Enemies.ToList())
		{
			if (enemy.CombatState != combatState)
			{
				continue;
			}

			PainfulStabsPower? legacyPower = enemy.GetPower<PainfulStabsPower>();
			if (legacyPower != null && enemy.IsDead)
			{
				await PowerCmd.Remove(legacyPower);
			}

			RemoveRetainedDeadEnemyIfNeeded(combatState, enemy);
		}
	}

	/// <summary>死前摘除疼痛戳刺,让尸体走标准移除链(boss 转阶段由 AdaptablePower 保留,不受影响)。</summary>
	public static async Task RemovePainfulStabsBeforeDeath(HextechEnemyHexContext context, Creature creature)
	{
		if (creature.Side != CombatSide.Enemy || creature.CombatState?.RunState != context.RunState)
		{
			return;
		}

		PainfulStabsPower? painfulStabs = creature.GetPower<PainfulStabsPower>();
		if (painfulStabs != null)
		{
			await PowerCmd.Remove(painfulStabs);
		}
	}

	public static void RemoveRetainedDeadEnemyIfNeeded(HextechCombatState combatState, Creature enemy)
	{
		if (enemy.Side != CombatSide.Enemy
			|| enemy.IsAlive
			|| !combatState.Enemies.Contains(enemy)
			|| !Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, enemy))
		{
			return;
		}

		CombatManager.Instance.RemoveCreature(enemy);
		combatState.RemoveCreature(enemy);
		try
		{
			NCreature? node = NCombatRoom.Instance?.GetCreatureNode(enemy);
			if (node != null)
			{
				NCombatRoom.Instance?.RemoveCreatureNode(node);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Dead enemy visual cleanup failed: {ex.Message}");
		}
		HextechLog.Info("Mayhem", $"Removed retained dead enemy after unsafe PainfulStabs cleanup: id={enemy.CombatId?.ToString() ?? "none"} model={enemy.ModelId.Entry}");
	}
}
