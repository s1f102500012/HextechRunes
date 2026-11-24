#if !STS2_108_OR_NEWER
namespace HextechRunes;

// 0.107.1 没有群情激愤（0.108.0 起才有）：只保留注册身份，永远不可获得。
public sealed partial class OutrageUpgradeRune : HextechRelicBase
{
	public override bool IsAvailableForPlayer(Player player)
	{
		return false;
	}
}
#endif
