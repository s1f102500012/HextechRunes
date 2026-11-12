using MegaCrit.Sts2.Core.Helpers;

namespace HextechRunes;

public sealed class TwinFlamesRune : HextechRelicBase
{
	internal const int MissileCount = 3;

	private int _targetRollsThisCombat;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Missiles", MissileCount)
	];

	public override Task BeforeCombatStart()
	{
		_targetRollsThisCombat = 0;
		return Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (Owner.Creature.IsDead
			|| !IsOwnedSkill(cardPlay.Card)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return Task.CompletedTask;
		}

		Creature source = Owner.Creature;
		decimal damage = HextechMissileVolley.DamageFromEnergyCost(HextechCombatHooks.GetEnergyCostForCurrentCardPlay(cardPlay.Card));
		if (damage <= 0m)
		{
			return Task.CompletedTask;
		}

		int targetOrdinal = ConsumeCombatProcOrdinal(ref _targetRollsThisCombat);
		string cardKey = HextechStableRandom.CardKey(cardPlay.Card);
		Creature? target = HextechRuneTargeting.PickRandomHittableEnemy(
			Owner,
			combatState,
			"twin-flames-target",
			combatState.RoundNumber.ToString(),
			targetOrdinal.ToString(),
			cardKey);
		if (target == null)
		{
			return Task.CompletedTask;
		}

		Flash([target]);
		Creature[] targets = [target];
		_ = TaskHelper.RunSafely(HextechMissileVolley.PlayVfxAsync(source, targets, MissileCount, HextechCombatVfx.PlayTwinFlamesMissile));
		return HextechMissileVolley.ResolveVolleyDamageInLockstepAsync(choiceContext, source, combatState, targets, MissileCount, _ => damage);
	}
}
