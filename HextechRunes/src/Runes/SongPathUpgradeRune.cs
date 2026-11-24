namespace HextechRunes;

/// <summary>
/// 升级：歌之道（愚者专属）：你的歌之道会由任意旋律触发。
/// 原版歌之道只存在于 0.113.0，基类的泛型实参因此不能写在共享文件里（同升级：神话形态）：
/// 0.113.0 的分部（<c>Fool/SongPathUpgradeRune.V113.cs</c>）继承 <see cref="CardUpgradeRuneBase{TCard}"/>，
/// 旧变体的分部（<c>Fool/SongPathUpgradeRune.Legacy.cs</c>）继承 <see cref="HextechRelicBase"/>，只是拿不到的壳。
/// 两个基类都不声明 SavedProperty，三个变体的存档属性集合一致；需要新增 SavedProperty 时写在本文件里。
/// </summary>
public sealed partial class SongPathUpgradeRune
{
}
