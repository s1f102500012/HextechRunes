namespace HextechRunes;

/// <summary>
/// 箭无虚发（愚者专属）：每当你让箭矢松脱时，获得5点瞄准。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），三个变体都编译；
/// 悬浮提示（引用 0.113.0 的能力/卡牌）、钩子与战斗逻辑在 <c>Fool/SureShotRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class SureShotRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("AimPower", 5m)
	];

	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
