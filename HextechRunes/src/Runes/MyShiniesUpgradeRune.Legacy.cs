#if !STS2_113_OR_NEWER
namespace HextechRunes;

// 0.107.1 / 0.111.0 没有我的小钱钱：只保留注册身份，永远不可获得。
public sealed partial class MyShiniesUpgradeRune : HextechRelicBase
{
	public override bool IsAvailableForPlayer(Player player)
	{
		return false;
	}
}
#endif
