namespace HextechRunes;

internal sealed class QueenEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Queen;

	internal override async Task BeforeEnemySideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players, IReadOnlyList<Creature> enemies)
	{
		if (!context.TryConsumeRoundInterval(Kind, combatState, everyNRounds: 2))
		{
			return;
		}

		IReadOnlyList<Creature> queenTargets = players
			.Where(player => player.GetPowerAmount<ChainsOfBindingPower>() < 3m)
			.ToList();
		if (queenTargets.Count > 0)
		{
			await PowerCmd.Apply<ChainsOfBindingPower>(queenTargets, 1m, null, null);
		}
	}
}
