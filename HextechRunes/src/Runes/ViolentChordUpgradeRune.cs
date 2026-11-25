namespace HextechRunes;

/// <summary>
/// 升级：暴力和弦（愚者专属）：你的暴力和弦会由任意旋律触发。
/// 原版暴力和弦只存在于 0.113.0，基类的泛型实参因此不能写在共享文件里（同升级：神话形态）：
/// 0.113.0 的分部（<c>Fool/ViolentChordUpgradeRune.V113.cs</c>）继承 <see cref="CardUpgradeRuneBase{TCard}"/>，
/// 旧变体的分部（<c>Fool/ViolentChordUpgradeRune.Legacy.cs</c>）继承 <see cref="HextechRelicBase"/>，只是拿不到的壳。
/// 两个基类都不声明 SavedProperty，各变体的存档属性集合一致；需要新增 SavedProperty 时写在本文件里。
/// </summary>
public sealed partial class ViolentChordUpgradeRune
{
}
