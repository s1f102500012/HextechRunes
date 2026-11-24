namespace HextechRunes;

// 敌方「连拨击锤」要在怪物 RollMove 时把行动状态机多推进一步、在头顶意图里同时列出两个行动，
// 并在本回合行动后接着执行推进出来的那一步；标准 effect hook 无法表达，真实实现位于
// HextechCombatHooks.FanTheHammer（推进与顺延）和 HextechCombatHooks.JeweledGauntlet（执行与意图显示，
// 与珠光护手共用）。本类仅用于注册与图鉴/描述登记。
internal sealed class FanTheHammerEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.FanTheHammer;
}
