namespace HextechRunes;

// 敌方专属海克斯的图标/标题/描述载体 relic：只作为 MonsterHexIconRelicTypes 的模型，
// 不进玩家 rune 池（不在 HextechPlayerRuneRegistry），IsAvailableForPlayer=false 双保险。
// 图标按约定路径 res://HextechRunes/images/relics/{stem}.png；文件缺失时
// HextechRelicBase.GetResolvedIconPath 回退占位图，不会崩。
// loc：relics.json 的 {stem}.title/.description/.flavor/.enemyDescription。

/// <summary>敌方海克斯图标载体的共同基类：永不进玩家池。</summary>
public abstract class EnemyHexIconRelicBase : HextechRelicBase
{
	public sealed override bool IsAvailableForPlayer(Player player) => false;
}

/// <summary>升级：鬼祟珊瑚群</summary>
public sealed class SkulkingColonyHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：花园幽灵鳗</summary>
public sealed class PhantasmalGardenerHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：女王</summary>
public sealed class QueenHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：乐加维林族母</summary>
public sealed class LagavulinMatriarchHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：外骨骼虫</summary>
public sealed class ExoskeletonHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：实验体</summary>
public sealed class TestSubjectHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：树叶史莱姆</summary>
public sealed class LeafSlimeHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：缩小甲虫</summary>
public sealed class ShrinkerBeetleHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：墨宝</summary>
public sealed class InkletHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：异蛙寄生虫</summary>
public sealed class PhrogParasiteHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：墨影幻灵</summary>
public sealed class VantomHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：永世沙漏</summary>
public sealed class AeonglassHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：失落之物</summary>
public sealed class TheLostHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：遗忘之物</summary>
public sealed class TheForgottenHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：史莱姆狂战士</summary>
public sealed class SlimedBerserkerHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：电球头</summary>
public sealed class GlobeHeadHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：异螨</summary>
public sealed class MyteHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：化石追踪者</summary>
public sealed class FossilStalkerHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：钨合金棍</summary>
public sealed class TungstenRodHex : EnemyHexIconRelicBase
{
}

/// <summary>百炼成钢</summary>
public sealed class HundredRefinementsHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：旧日雕像</summary>
public sealed class AncientStatueHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：多尼斯异鸟</summary>
public sealed class ByrdonisHex : EnemyHexIconRelicBase
{
}

/// <summary>我饥饿</summary>
public sealed class HungryHex : EnemyHexIconRelicBase
{
}

/// <summary>我细看</summary>
public sealed class InspectHex : EnemyHexIconRelicBase
{
}

/// <summary>我紧握</summary>
public sealed class GripHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：活雾</summary>
public sealed class LivingFogHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：仪式兽</summary>
public sealed class CeremonialBeastHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：灵魂异鱼</summary>
public sealed class SoulFyshHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：偷窃草蜢</summary>
public sealed class ThievingHopperHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：幽灵船</summary>
public sealed class HauntedShipHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：腐化之心</summary>
public sealed class CorruptHeartHex : EnemyHexIconRelicBase
{
}

/// <summary>MonsterHexKind.ArcanePunch 的图标载体</summary>
public sealed class InfestedPrismHex : EnemyHexIconRelicBase
{
}

/// <summary>MonsterHexKind.Enlightenment 的图标载体；玩家符文"开悟"仍是 EnlightenmentRune</summary>
public sealed class EnlightenmentHex : EnemyHexIconRelicBase
{
}

/// <summary>MonsterHexKind.MadScientist 的图标载体（显示为"升级：蜂群术士"）；玩家符文"科学狂人"仍是 MadScientistRune</summary>
public sealed class EntomancerHex : EnemyHexIconRelicBase
{
}

/// <summary>质变：白银阶（敌方黄金）；图标借用玩家"质变：棱彩阶"</summary>
public sealed class TransmuteSilverHex : EnemyHexIconRelicBase
{
}

/// <summary>质变：黄金阶（敌方棱彩）；图标借用玩家"质变：混沌"</summary>
public sealed class TransmuteGoldHex : EnemyHexIconRelicBase
{
}

/// <summary>MonsterHexKind.SpinToWin 的图标载体（棱彩）；我方符文"转出胜利"仍是 SpinToWinRune（黄金）</summary>
public sealed class SpinToWinHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：啃咬机</summary>
public sealed class ChomperHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：藤蔓蹒跚者</summary>
public sealed class VineShamblerHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：棘刺蟾蜍</summary>
public sealed class SpinyToadHex : EnemyHexIconRelicBase
{
}

/// <summary>升级：咬人卷轴</summary>
public sealed class ScrollOfBitingHex : EnemyHexIconRelicBase
{
}
