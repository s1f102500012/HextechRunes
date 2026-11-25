#if !STS2_114_OR_NEWER
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

// 0.113.x 及更早：原版疼痛戳刺让持有者死后留场、能力也不移除，这里在死前摘除并在回合边界兜底清理。
// 0.114.0 起原版删掉了这两个覆写，尸体与能力走标准移除链，不再需要这段（敌人攻击途中被反伤打死时，原版照常塞伤口）。
internal sealed partial class TestSubjectEnemyHex
{
	// 疼痛戳刺覆写了 ShouldPowerBeRemovedAfterOwnerDeath/ShouldCreatureBeRemovedFromCombatAfterDeath,
	// 持有者死后尸体留场继续排意图(攻击不生效但 debuff 意图照常生效)。
	// 双保险:死前摘除 + 回合边界兜底清理。
	internal override async Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		await CleanUpRetainedPainfulStabsEnemies(combatState);
	}

	internal override async Task BeforeDeath(HextechEnemyHexContext context, Creature creature)
	{
		await RemovePainfulStabsBeforeDeath(context, creature);
	}

	/// <summary>
	/// 清理因 PainfulStabsPower 滞留场上的死亡敌人:该 power 覆写了
	/// ShouldPowerBeRemovedAfterOwnerDeath/ShouldCreatureBeRemovedFromCombatAfterDeath,
	/// 持有者死后尸体留场继续排意图(攻击打不出但 debuff 意图照常生效)。
	/// </summary>
	private static async Task CleanUpRetainedPainfulStabsEnemies(HextechCombatState combatState)
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
	private static async Task RemovePainfulStabsBeforeDeath(HextechEnemyHexContext context, Creature creature)
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

	private static void RemoveRetainedDeadEnemyIfNeeded(HextechCombatState combatState, Creature enemy)
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
#endif
