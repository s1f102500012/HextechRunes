namespace HextechRunes;

/// <summary>
/// 呱！（愚者专属）：获得时，将牌组中的每对基础打击和基础防御替换为1张哇 啾 叽。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），三个变体都编译；
/// 悬浮提示与获得时的替换（引用 0.113.0 的卡牌）在 <c>Fool/RibbitRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class RibbitRune : HextechRelicBase
{
	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];

	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
