namespace HextechRunes;

/// <summary>
/// 穿云箭（愚者专属）：每当你的箭矢扎穿敌人时，使其获得2层易伤。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），三个变体都编译；
/// 悬浮提示（引用 0.113.0 的能力/卡牌）、钩子与战斗逻辑在 <c>Fool/CloudPiercerRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class CloudPiercerRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<VulnerablePower>(2m)
	];

	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
