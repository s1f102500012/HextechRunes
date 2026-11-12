namespace HextechRunes;

// 扭曲现实的效果（敌方生成给玩家的状态牌数量翻倍）要在状态牌加入牌堆前改写整批卡牌，标准 effect hook 无法表达，
// 实现在 HextechEnemyManipulateRealityHooks。本类仅用于注册与图鉴/描述登记。
internal sealed class ManipulateRealityEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.ManipulateReality;
}
