namespace HextechRunes;

internal static class HextechCreatureStatLimits
{
	// 原版生命、最大生命、格挡的统一硬上限 999999999：Creature.LoseHpInternal 的失血截断、
	// SetMaxHpInternal 与 GainBlockInternal 的上限（0.107.1 / 0.111.0 相同）。
	internal const int StatHardCap = 999999999;
}
