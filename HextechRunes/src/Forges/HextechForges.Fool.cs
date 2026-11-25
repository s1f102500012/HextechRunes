namespace HextechRunes;

// 愚者专属锻造器。本文件只放与版本无关的声明（数值、可用性），各变体都编译；
// 悬浮提示（引用 0.113.0 的能力）、钩子与战斗逻辑在 HextechForges.Fool.V113.cs，只在 0.113.0 编译。
// 旧变体没有愚者，IsFoolPlayer 恒为 false，抽取池里不会出现；触发时仍要再判角色（与角色限定符文同一裁决）。

/// <summary>瞄准锻造器（白银，愚者专属）：回合开始时，获得2点瞄准。</summary>
public sealed partial class AimForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("AimPower", 2m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}

/// <summary>拉伸锻造器（白银，愚者专属）：回合开始时，获得2点拉伸。</summary>
public sealed partial class StretchForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("StretchPower", 2m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}

/// <summary>箭矢锻造器（黄金，愚者专属）：战斗开始时，额外获得1支箭矢。</summary>
public sealed partial class ArrowForge : HextechForgeBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Arrows", 1m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}
}
