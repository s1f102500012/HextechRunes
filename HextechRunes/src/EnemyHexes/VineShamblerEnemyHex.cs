namespace HextechRunes;

internal sealed class VineShamblerEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.VineShambler;

	internal override async Task BeforePlayerSideTurnStart(
		HextechEnemyHexContext context,
		HextechCombatState combatState,
		IReadOnlyList<Creature> players)
	{
		// 缠结（本回合攻击牌费用 +1）在原版侧回合结束时移除，所以要在玩家回合开始时给，当回合才生效。
		// 额外回合不推进回合号，按回合号防重；没有玩家时不消耗本回合的防重次数。
		if (players.Count > 0
			&& context.TryConsumeRoundInterval(Kind, combatState, context.TierValue(Kind, 4, 3, 2)))
		{
			await PowerCmd.Apply<TangledPower>(players, 1m, null, null);
		}
	}
}
