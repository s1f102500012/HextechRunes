namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override async Task BeforeDeath(Creature creature)
	{
		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.BeforeDeath(context, creature));
	}

#if STS2_113_OR_NEWER
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
#else
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
#endif
	{
		if (wasRemovalPrevented
			|| target.Side != CombatSide.Enemy
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target, out HextechCombatState? combatState))
		{
			return;
		}

		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterDeath(context, choiceContext, target, combatState));
	}
}
