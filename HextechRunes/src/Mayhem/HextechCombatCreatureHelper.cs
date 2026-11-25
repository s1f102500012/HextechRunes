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
}
