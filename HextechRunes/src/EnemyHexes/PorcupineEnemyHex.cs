namespace HextechRunes;

internal sealed class PorcupineEnemyHex : HextechEnemyHexEffect
{
	// 每 4N 次未被格挡伤害(N=联机人数)获得一次本回合荆棘;描述侧由 MonsterHexCatalog 的 HitsNeeded 阈值同步显示。
	internal const int HitsPerTriggerPerPlayer = 4;

	internal override MonsterHexKind Kind => MonsterHexKind.Porcupine;

	internal override async Task AfterEnemyDamageReceived(HextechEnemyHexContext context, Creature target, uint combatId, DamageResult result, Creature? dealer, CardModel? cardSource)
	{
		if (!target.IsAlive || result.UnblockedDamage <= 0m)
		{
			return;
		}

		if (!ReachesHitThreshold(context.Tracking.EnemyPorcupineUnblockedHitsThisCombat, combatId, HitsPerTriggerPerPlayer * context.ScalingPlayerCount))
		{
			return;
		}

		int thorns = context.TierValue(Kind, 1, 2, 3);
		context.Tracking.EnemyPorcupineTemporaryThornsThisTurn[combatId] =
			context.Tracking.EnemyPorcupineTemporaryThornsThisTurn.GetValueOrDefault(combatId, 0) + thorns;
		await PowerCmd.Apply<ThornsPower>(target, thorns, target, cardSource);
	}

	internal override async Task BeforeTurnEnd(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, CombatRoom? combatRoom)
	{
		if (combatRoom == null)
		{
			return;
		}

		await RemoveTemporaryThorns(context, combatRoom.CombatState.Enemies);
	}

	internal override async Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		await RemoveTemporaryThorns(context, combatState.Enemies);
	}

	private static async Task RemoveTemporaryThorns(HextechEnemyHexContext context, IReadOnlyList<Creature> enemies)
	{
		foreach ((uint combatId, int thorns) in GetTemporaryThornsToRemove(context.Tracking))
		{
			Creature? enemy = enemies.FirstOrDefault(creature => creature.CombatId == combatId);
			if (enemy is { IsAlive: true })
			{
				await PowerCmd.Apply<ThornsPower>(enemy, -thorns, enemy, null);
			}
		}

		context.Tracking.EnemyPorcupineTemporaryThornsThisTurn.Clear();
	}

	internal static IReadOnlyList<(uint CombatId, int Thorns)> GetTemporaryThornsToRemove(HextechMayhemCombatTrackingState tracking)
	{
		if (tracking.EnemyPorcupineTemporaryThornsThisTurn.Count == 0)
		{
			return [];
		}

		List<(uint CombatId, int Thorns)> result = new();
		foreach ((uint combatId, int thorns) in tracking.EnemyPorcupineTemporaryThornsThisTurn.OrderBy(static entry => entry.Key))
		{
			if (thorns <= 0)
			{
				continue;
			}

			result.Add((combatId, thorns));
		}

		return result;
	}
}
