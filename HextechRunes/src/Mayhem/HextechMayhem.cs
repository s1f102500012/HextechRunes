namespace HextechRunes;

internal sealed partial class HextechMayhemModifier : HextechModifierBase
{
	// 取最后一个本类型 Modifier；手牌上限、濒死等热路径反复调用，倒序按索引找，不分配、不跨调用缓存。
	internal static HextechMayhemModifier? FindIn(IRunState? runState)
	{
		if (runState == null)
		{
			return null;
		}

		IReadOnlyList<ModifierModel> modifiers = runState.Modifiers;
		for (int i = modifiers.Count - 1; i >= 0; i--)
		{
			if (modifiers[i] is HextechMayhemModifier modifier)
			{
				return modifier;
			}
		}

		return null;
	}

	internal static bool IsEnabledForRun(IRunState? runState)
	{
		if (IsGameModeUnsupportedForRun(runState))
		{
			return false;
		}

		if (FindIn(runState) is HextechMayhemModifier modifier)
		{
			return modifier.IsModActiveForRun;
		}

		// 联机缺少本局快照时不能用各端本地菜单值决定共享模型写入。
		return !HextechPlayerContextHelper.IsNetworkMultiplayerRun() && HextechRuneConfiguration.GetModEnabled();
	}
}
