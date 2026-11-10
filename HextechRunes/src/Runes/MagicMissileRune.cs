using MegaCrit.Sts2.Core.Helpers;

namespace HextechRunes;

public sealed class MagicMissileRune : TurnScopedRelicBase
{
	internal const int MissileCount = 3;
	internal const decimal MaxHpDamagePercent = 3m;

	private bool _triggeredThisTurn;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Missiles", MissileCount),
		new DynamicVar("MaxHpDamagePercent", MaxHpDamagePercent)
	];

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent();
		if (Owner.Creature.IsDead
			|| !cardPlay.IsFirstInSeries
			|| !IsOwnedAttack(cardPlay.Card))
		{
			return Task.CompletedTask;
		}

		if (Owner.Creature.CombatState is not HextechCombatState combatState
			|| HextechRuneTargeting.ResolveCardPlayEnemyTargets(cardPlay, combatState) is not { Count: > 0 } targets
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return Task.CompletedTask;
		}

		Flash(targets);
		Creature source = Owner.Creature;
		decimal damagePercent = DynamicVars["MaxHpDamagePercent"].BaseValue;
		_ = TaskHelper.RunSafely(HextechMissileVolley.PlayVfxAsync(source, targets, MissileCount, HextechCombatVfx.PlayMagicMissile));
		return HextechMissileVolley.ResolveVolleyDamageInLockstepAsync(
			choiceContext,
			source,
			combatState,
			targets,
			MissileCount,
			target => CalculateMissileDamage(target.MaxHp, damagePercent));
	}

	internal static int CalculateMissileDamage(decimal targetMaxHp, decimal damagePercent = MaxHpDamagePercent)
	{
		return Math.Max(1, FloorToInt(targetMaxHp * damagePercent / 100m));
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
