using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace HextechRunes;

/// <summary>
/// 升级：啃咬机：敌人的下一次行动不含攻击时，补一个"把晕眩加入玩家弃牌堆"的意图，行动结束后执行。
/// 补意图与执行沿用怪物升级的公共流程（HextechCombatHooks.MonsterUpgrades）。
/// </summary>
internal sealed class ChomperEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Chomper;

	internal static int GetDazedCount(HextechEnemyHexContext context)
	{
		return context.TierValue(MonsterHexKind.Chomper, 0, 1, 2);
	}
}

internal sealed class ChomperDazedIntent(Creature source, int count) : StatusIntent(count)
{
	internal Creature Source => source;
}
