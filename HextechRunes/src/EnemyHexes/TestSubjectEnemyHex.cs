namespace HextechRunes;

internal sealed partial class TestSubjectEnemyHex : HextechEnemyHexEffect
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
}
