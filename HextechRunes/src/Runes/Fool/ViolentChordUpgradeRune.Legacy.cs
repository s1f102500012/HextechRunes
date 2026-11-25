#if !STS2_113_OR_NEWER
namespace HextechRunes;

// 0.111.0 没有暴力和弦与愚者：只保留注册身份，永远不可获得。
public sealed partial class ViolentChordUpgradeRune : HextechRelicBase
{
	public override bool IsAvailableForPlayer(Player player)
	{
		return false;
	}
}
#endif
