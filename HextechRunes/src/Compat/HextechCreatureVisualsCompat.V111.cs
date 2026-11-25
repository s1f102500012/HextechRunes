using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

internal static class HextechCreatureVisualsCompat
{
	// 0.111.0 起形态特效容器为 FormVfxHolder 属性。
	internal static Control? GetFormVfxHolder(NCreatureVisuals visuals) => visuals.FormVfxHolder;
}
