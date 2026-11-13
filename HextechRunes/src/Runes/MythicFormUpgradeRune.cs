namespace HextechRunes;

/// <summary>
/// 升级：神话形态（愚者专属）：每场战斗开始时，自动打出你所有的神话形态。
/// 原版神话形态只存在于 0.113.0，基类的泛型实参因此不能写在共享文件里：
/// 0.113.0 的分部（<c>Fool/MythicFormUpgradeRune.V113.cs</c>）继承 <see cref="AutoPlayFormsAtCombatStartRuneBase{TCard}"/>，
/// 旧变体的分部（<c>Fool/MythicFormUpgradeRune.Legacy.cs</c>）继承 <see cref="HextechRelicBase"/>，只是拿不到的壳。
/// 两个基类都不声明 SavedProperty，三个变体的存档属性集合一致；需要新增 SavedProperty 时写在本文件里。
/// </summary>
public sealed partial class MythicFormUpgradeRune
{
}
