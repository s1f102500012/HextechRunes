using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override ActMap ModifyGeneratedMapLate(IRunState runState, ActMap map, int actIndex)
	{
		if (!ReferenceEquals(runState, RunState))
		{
			return map;
		}

		return ApplyMapModifiers(map, runState, GetSelectionIndexForAct(actIndex));
	}

	internal void ApplyMapModifiersToCurrentAct(string reason, int? stageIndex = null)
	{
		RunState runState = ActiveRunState;
		ActMap currentMap = runState.Map;
		int effectiveStageIndex = stageIndex ?? GetCurrentStageIndex();
		ActMap modifiedMap = ApplyMapModifiers(currentMap, runState, effectiveStageIndex);
		if (ReferenceEquals(modifiedMap, currentMap))
		{
			return;
		}

		runState.Map = modifiedMap;
		runState.RemoveStaleVisitedMapCoords(modifiedMap);
		HextechPresentation.TryRun(
			"Mayhem",
			"Failed to refresh map screen after map modifiers",
			() => NMapScreen.Instance?.SetMap(modifiedMap, runState.Rng.Seed, clearDrawings: true));
		HextechLog.Info("Mayhem", $"Applied map modifiers to current act: reason={reason} act={runState.CurrentActIndex} stage={effectiveStageIndex} activeHexes={string.Join(",", GetActiveMonsterHexes())}");
	}

	private ActMap ApplyMapModifiers(ActMap map, IRunState runState, int actIndex)
	{
		HextechEnemyHexContext context = new(this);
		ActMap modifiedMap = map;
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			modifiedMap = effect.ModifyGeneratedMapLate(context, runState, modifiedMap, actIndex);
		}

		return modifiedMap;
	}
}
