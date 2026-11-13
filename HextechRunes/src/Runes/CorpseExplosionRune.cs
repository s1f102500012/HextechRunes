namespace HextechRunes;

public sealed class CorpseExplosionRune : HextechRelicBase
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

#if STS2_113_OR_NEWER
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
#else
	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
#endif
	{
		if (wasRemovalPrevented
			|| Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		List<Creature> enemies = combatState.HittableEnemies
			.Where(enemy => enemy != target && enemy.IsAlive)
			.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Flash(enemies);
		// 表现:尸体位置毒绿脓爆,毒液弧线泼向每个存活敌人(纯表现层,中毒立即结算)。
		HextechCombatVfx.CorpseBloomBurst(target, enemies);
		await PowerCmd.Apply<PoisonPower>(enemies, target.MaxHp, Owner.Creature, null);
	}
}
