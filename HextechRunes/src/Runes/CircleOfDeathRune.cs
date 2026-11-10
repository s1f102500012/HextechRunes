namespace HextechRunes;

public sealed class CircleOfDeathRune : HextechRelicBase
{
	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，跨战斗累加、读档归零，
	// 改成每场清零会改变单机的随机结果，按现状保留；联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	private int _localTargetRollOrdinal;

	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_localTargetRollOrdinal = 0;
		}

		return base.BeforeCombatStart();
	}

	public Task HandleSustainGained(decimal amount)
	{
		if (Owner.Creature.IsDead
			|| Owner.Creature.CombatState == null
			|| !CombatManager.Instance.IsInProgress
			|| amount <= 0m)
		{
			return Task.CompletedTask;
		}

		int damage = FloorToInt(amount);
		if (damage <= 0)
		{
			return Task.CompletedTask;
		}

		List<Creature> enemies = Owner.Creature.CombatState.HittableEnemies
			.Where(static enemy => enemy.IsAlive)
			.ToList();
		if (enemies.Count == 0)
		{
			return Task.CompletedTask;
		}

		// 候选保持 HittableEnemies 的原顺序（不按 CombatId 排序），与 HextechRuneTargeting.PickRandomHittableEnemy
		// 不同口径；换用它会改变同一盐值选中的目标，所以不合并。
		int targetOrdinal = ConsumeCombatProcOrdinal(nameof(CircleOfDeathRune), ref _localTargetRollOrdinal);
		Creature target = enemies[HextechStableRandom.Index(
			(RunState)Owner.RunState,
			enemies.Count,
			"circle-of-death-target",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState.RoundNumber.ToString(),
			damage.ToString(),
			targetOrdinal.ToString())];
		Flash([target]);
		HextechCombatVfx.DeathRingLash(Owner.Creature, target);
		return HextechGameApiCompat.Damage(new BlockingPlayerChoiceContext(), target, damage, ValueProp.Unpowered, Owner.Creature, null);
	}

	public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
	{
		return creature == Owner.Creature ? HandleSustainGained(amount) : Task.CompletedTask;
	}
}
