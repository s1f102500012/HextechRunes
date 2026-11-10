namespace HextechRunes;

internal sealed class TestSubjectEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.TestSubject;

	internal override async Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		if (!enemy.IsAlive)
		{
			return;
		}

		int enrage = context.TierValue(Kind, 0, 0, 1);
		int painfulStabs = context.TierValue(Kind, 1, 2, 2);
		if (enrage > 0)
		{
			await PowerCmd.Apply<EnragePower>(enemy, enrage, enemy, null);
		}

		if (painfulStabs > 0)
		{
			await PowerCmd.Apply<PainfulStabsPower>(enemy, painfulStabs, enemy, null);
		}
	}

	// 疼痛戳刺覆写了 ShouldPowerBeRemovedAfterOwnerDeath/ShouldCreatureBeRemovedFromCombatAfterDeath,
	// 持有者死后尸体留场继续排意图(攻击不生效但 debuff 意图照常生效)。
	// 双保险:死前摘除 + 回合边界兜底清理。
	internal override async Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		await HextechCombatCreatureHelper.CleanUpRetainedPainfulStabsEnemies(combatState);
	}

	internal override async Task BeforeDeath(HextechEnemyHexContext context, Creature creature)
	{
		await HextechCombatCreatureHelper.RemovePainfulStabsBeforeDeath(context, creature);
	}
}
