using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace HextechRunes;

internal static class HextechControllerCompat
{
	// 0.110 起原版区分手柄与纯键盘,二者都算方向导航。
	internal static bool IsUsingDirectionalNavigation(NControllerManager? manager) => manager?.IsUsingDirectionalNavigation == true;
}
