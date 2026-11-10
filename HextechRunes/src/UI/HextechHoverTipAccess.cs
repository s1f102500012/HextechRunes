using Godot;

namespace HextechRunes;

/// <summary>
/// 原版 <see cref="HoverTip"/> 是 record struct,<c>Description</c>/<c>Icon</c> 只有私有 setter;改写一个已构造好的提示
/// 只能经由编译器生成的 backing field。两处私有访问集中在这里:缺失时进启动摘要,调用方按返回值降级(不改提示)。
/// </summary>
internal static class HextechHoverTipAccess
{
	// 原版 HoverTip.<Description>k__BackingField(0.107.1 / 0.111.0 均为自动属性)。
	private static readonly FieldInfo? DescriptionField = HextechHookReflection.TryGetField(typeof(HoverTip), "<Description>k__BackingField");

	// 原版 HoverTip.<Icon>k__BackingField(同上)。
	private static readonly FieldInfo? IconField = HextechHookReflection.TryGetField(typeof(HoverTip), "<Icon>k__BackingField");

	internal static bool CanSetDescription => DescriptionField != null;

	internal static bool CanSetIcon => IconField != null;

	/// <summary>
	/// 在装箱的 <see cref="HoverTip"/> 上就地改写描述。传入的必须是箱体本身(接口引用或 <c>object</c>),
	/// 改动才会反映到持有这个箱体的字段/集合里。
	/// </summary>
	internal static bool TrySetDescription(object boxedHoverTip, string description)
	{
		if (DescriptionField == null || boxedHoverTip is not HoverTip)
		{
			return false;
		}

		DescriptionField.SetValue(boxedHoverTip, description);
		return true;
	}

	/// <inheritdoc cref="TrySetDescription"/>
	internal static bool TrySetIcon(object boxedHoverTip, Texture2D icon)
	{
		if (IconField == null || boxedHoverTip is not HoverTip)
		{
			return false;
		}

		IconField.SetValue(boxedHoverTip, icon);
		return true;
	}
}
