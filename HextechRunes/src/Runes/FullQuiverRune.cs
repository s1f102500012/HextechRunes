namespace HextechRunes;

/// <summary>
/// 箭袋满满（愚者专属）：战斗开始时，额外获得1支箭矢。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），三个变体都编译；
/// 悬浮提示（引用 0.113.0 的能力/卡牌）、钩子与战斗逻辑在 <c>Fool/FullQuiverRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class FullQuiverRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Arrows", 1m)
	];

	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
