namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal HextechMayhemCombatTrackingState CombatTracking => _runContext.CombatTracking;

	private void ResetCombatTracking(bool atCombatStart = false)
	{
		HextechEnemyHexEffects.ResetAllRunScopedState();
		_runContext.ResetCombatTracking(atCombatStart);
	}
}
