namespace HextechRunes;

/// <summary>
/// 升级：蜂群术士（MonsterHexKind.MadScientist；枚举名是兼容契约，类名按 {Kind}EnemyHex 约定随之保留）：敌人减少最大生命值，并获得 1 层原版人体蜂房。
/// 晕眩由原版 PersonalHivePower 负责（受到攻击伤害时往攻击者抽牌堆随机位置加晕眩，奥斯提归到主人），
/// 本类不自己往牌堆加牌。
/// </summary>
internal sealed class MadScientistEnemyHex : HextechEnemyHexEffect, IHextechEnemyMaxHpCoefficientProvider
{
	private const decimal PersonalHiveStacks = 1m;

	internal override MonsterHexKind Kind => MonsterHexKind.MadScientist;

	internal override int PersistentOrder => 40;

	// 与最大生命减少共用同一个持久标记：开战时的所有敌人、战斗中新加入的敌人（召唤/分裂）各施加一次，
	// 首领转阶段重放（replayOneShotPowers）时随最大生命一起补发。
	// 人体蜂房不参与原版联机缩放（PersonalHivePower 未覆写 ShouldScaleInMultiplayer），固定 1 层。
	internal override async Task ApplyPersistentToEnemy(HextechEnemyHexContext context, Creature creature, int? maxHpBaseOverride, bool replayOneShotPowers)
	{
		if (creature.CombatId == null
			|| !HextechCombatProcTracker.TryMarkPersistentHexApplied(context.Tracking.MadScientistApplied, creature, replayOneShotPowers))
		{
			return;
		}

		await context.Modifier.ReapplyMonsterMaxHpCoefficients(creature, maxHpBaseOverride);
		if (creature.IsAlive)
		{
			await PowerCmd.Apply<PersonalHivePower>(creature, PersonalHiveStacks, creature, null);
		}
	}

	public decimal GetMaxHpBonusFraction(HextechEnemyHexContext context, Creature creature)
	{
		return -context.TierValue(Kind, 0.30m, 0.15m, 0.00m);
	}
}
