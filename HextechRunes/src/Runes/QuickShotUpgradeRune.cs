namespace HextechRunes;

/// <summary>
/// 升级：快速射击（愚者专属）：打出快速射击后，抽1张牌。
/// 原版快速射击只存在于 0.113.0，基类的泛型实参因此不能写在共享文件里（同升级：神话形态）：
/// 0.113.0 的分部（<c>Fool/QuickShotUpgradeRune.V113.cs</c>）继承 <see cref="CardUpgradeRuneBase{TCard}"/>，
/// 旧变体的分部（<c>Fool/QuickShotUpgradeRune.Legacy.cs</c>）继承 <see cref="HextechRelicBase"/>，只是拿不到的壳。
/// 数值写在这里，三个变体的描述占位符一致；两个基类都不声明 SavedProperty，需要新增时也写在本文件里。
/// </summary>
public sealed partial class QuickShotUpgradeRune
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];
}
