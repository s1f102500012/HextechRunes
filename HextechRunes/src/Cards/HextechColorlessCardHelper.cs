namespace HextechRunes;

internal static class HextechColorlessCardHelper
{
	// 与原版传家宝锤（HeirloomHammer）选牌条件一致：显示卡池为无色池。0.107.1～0.111.0 中无色池包括
	// 无色卡池、衍生卡池（君王之剑、仆从牌、小刀等）、事件卡池与废弃卡池；第三方卡池按其自身的 IsColorless。
	public static bool IsColorlessCard(CardModel card)
	{
		return card.VisualCardPool.IsColorless;
	}
}
