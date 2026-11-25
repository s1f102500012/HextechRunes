namespace HextechRunes;

/// <summary>
/// 渐入佳境（愚者专属）：回合开始时，获得等同于当前回合数的瞄准。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），各变体都编译；
/// 悬浮提示（引用 0.113.0 的能力/卡牌）、钩子与战斗逻辑在 <c>Fool/HittingStrideRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class HittingStrideRune : HextechRelicBase
{
	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
