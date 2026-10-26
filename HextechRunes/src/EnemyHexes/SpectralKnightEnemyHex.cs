namespace HextechRunes;

internal sealed class SpectralKnightEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.SpectralKnight;

	// 玩家侧恶咒（HextechHexPower）持续整场战斗，不像原版那样随幽灵骑士死亡解除。
	internal override async Task ApplyCombatStartPlayerDebuffs(HextechEnemyHexContext context, CombatRoom room, IReadOnlyList<Creature> players)
	{
		Creature[] targets = players.Where(static creature => creature.IsPlayer).ToArray();
		if (targets.Length > 0)
		{
			await PowerCmd.Apply<HextechHexPower>(targets, 1m, null, null);
		}
	}
}
