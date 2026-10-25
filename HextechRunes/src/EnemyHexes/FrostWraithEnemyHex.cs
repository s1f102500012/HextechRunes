namespace HextechRunes;

internal sealed class FrostWraithEnemyHex : HextechEnemyHexEffect
{
	internal const int TurnsNeeded = 3;
	internal const int TemporarySlowAmount = 50;

	internal override MonsterHexKind Kind => MonsterHexKind.FrostWraith;

	internal override async Task BeforePlayerSideTurnStart(
		HextechEnemyHexContext context,
		HextechCombatState combatState,
		IReadOnlyList<Creature> players)
	{
		// 玩家回合开始时施加：玩家在自己的回合里就能看到，持续到下一个玩家回合开始前（覆盖本轮敌方回合）。
		// 旧层的清理是临时缓慢自身的回合开始回调（能力先于 Mayhem 结算），同回合新施加的层数保留。
		// 额外回合不推进回合号，按回合号防重；没有玩家时不消耗本回合的防重次数。
		if (players.Count > 0
			&& context.TryConsumeRoundInterval(Kind, combatState, TurnsNeeded))
		{
			await PowerCmd.Apply<HextechTemporarySlowPower>(players, TemporarySlowAmount, null, null);
		}
	}

	internal static bool ShouldTriggerForRound(int roundNumber)
	{
		return HextechRoundInterval.IsDue(roundNumber, TurnsNeeded);
	}
}
