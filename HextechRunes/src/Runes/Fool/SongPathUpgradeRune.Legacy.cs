#if !STS2_113_OR_NEWER
namespace HextechRunes;

// 0.111.0 没有歌之道与愚者：只保留注册身份，永远不可获得。
public sealed partial class SongPathUpgradeRune : HextechRelicBase
{
	public override bool IsAvailableForPlayer(Player player)
	{
		return false;
	}
}
#endif
