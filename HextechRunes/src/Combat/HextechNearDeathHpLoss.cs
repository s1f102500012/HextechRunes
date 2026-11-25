namespace HextechRunes;

/// <summary>
/// 玩家版 <see cref="NearDeathFeastRune"/> 与敌方版 <see cref="HextechEnemyNearDeath"/> 共用的失血推演：
/// 把"当前生命 / 负血债务"折算成有效生命，扣除本次失血后判定是否真正死亡、是否进入濒死以及应写回的生命值。
/// 只做纯计算，状态写回与力量同步由各自的持有方完成。
/// </summary>
internal readonly record struct HextechNearDeathHpLoss(int HpLoss, bool Killed, bool Dying, int Debt, int CurrentHp, int OverkillDamage)
{
	// 与原版 Creature.LoseHpInternal 相同的失血截断。
	internal static int ToHpLoss(decimal amount)
	{
		return (int)Math.Min(amount, HextechCreatureStatLimits.StatHardCap);
	}

	internal static HextechNearDeathHpLoss Resolve(bool wasDying, int debt, int currentHp, decimal amount, int deathLimit)
	{
		int oldEffectiveHp = wasDying ? -debt : currentHp;
		int hpLoss = ToHpLoss(amount);
		int newEffectiveHp = oldEffectiveHp - hpLoss;
		if (newEffectiveHp <= -deathLimit)
		{
			return new HextechNearDeathHpLoss(hpLoss, Killed: true, Dying: false, deathLimit, CurrentHp: 0, Math.Max(0, -deathLimit - newEffectiveHp));
		}

		bool dying = newEffectiveHp < 1;
		return new HextechNearDeathHpLoss(hpLoss, Killed: false, dying, Math.Max(0, -newEffectiveHp), dying ? 1 : newEffectiveHp, OverkillDamage: 0);
	}

}
