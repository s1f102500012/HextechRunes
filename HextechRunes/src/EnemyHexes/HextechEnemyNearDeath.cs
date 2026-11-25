using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

/// <summary>
/// 敌方「濒死狂宴」的濒死状态机,镜像玩家版 <see cref="NearDeathFeastRune"/> 的静态判定 API:
/// 敌人生命低于 1 时进入濒死(负血),无法获得治疗与格挡,每 1 点负血获得 1 层力量;
/// 负血达到最大生命的 5%/10%/15%(白银/黄金/棱彩)时真正死亡。
/// 状态按 CombatId 存在 Mayhem 的 CombatTracking 里(联机同步、rejoin/存档随快照走),
/// 两端由同一命令流驱动,结果确定。
/// 死亡联动:负血打满后走标准 SetCurrentHpInternal(0)+killed 死亡链,实验体转阶段、
/// 千足虫接续等"死亡时不移除"的机制照常接管;新阶段/接续体把 HP 设回正值时,
/// CurrentHp setter 分支会清掉残留的濒死状态(见 ClearIfRecovered)。
/// </summary>
internal static class HextechEnemyNearDeath
{
	private const decimal LimitPercentPerTier = 0.05m;

	/// <summary>敌人是否具备濒死狂宴(本场海克斯激活且可参与追踪)。</summary>
	internal static bool HasDyingState(Creature creature)
	{
		return TryGetContext(creature, out _, out _);
	}

	internal static bool IsDyingButAlive(Creature creature)
	{
		if (!TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			return false;
		}

		return modifier.CombatTracking.NearDeathFeastEnemyDebt.TryGetValue(combatId, out int debt)
			&& creature.CurrentHp > 0
			&& debt < GetDeathNegativeHpLimit(creature, modifier);
	}

	internal static bool ShouldInterceptLoseHp(Creature creature, decimal amount)
	{
		if (amount <= 0m || !TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			return false;
		}

		return modifier.CombatTracking.NearDeathFeastEnemyDebt.ContainsKey(combatId)
			|| creature.CurrentHp - HextechNearDeathHpLoss.ToHpLoss(amount) < 1;
	}

	// 与玩家版相同：同步前缀里只记债务，力量补差由 NearDeathFeastEnemyHex.AfterCurrentHpChanged 等待执行。
	internal static HextechNearDeathHpLoss LoseHpAllowingDying(Creature creature, decimal amount, bool dryRun = false)
	{
		if (amount <= 0m || !TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			return default;
		}

		HextechMayhemCombatTrackingState tracking = modifier.CombatTracking;
		bool wasDying = tracking.NearDeathFeastEnemyDebt.TryGetValue(combatId, out int debt);
		HextechNearDeathHpLoss outcome = HextechNearDeathHpLoss.Resolve(
			wasDying,
			debt,
			creature.CurrentHp,
			amount,
			GetDeathNegativeHpLimit(creature, modifier));
		if (dryRun)
		{
			return outcome;
		}

		if (outcome.Dying)
		{
			tracking.NearDeathFeastEnemyDebt[combatId] = outcome.Debt;
		}
		else
		{
			// 恢复到正血或负血打满都退出濒死；后者走标准死亡链(转阶段/接续/掉魂等在上层照常发生)。
			ClearState(tracking, combatId);
		}

		creature.SetCurrentHpInternal(outcome.CurrentHp);
		return outcome;
	}

	/// <summary>斩杀/处决类命令(CreatureCmd.Kill 系)无视濒死,直接判死。</summary>
	internal static void ForceDeathThresholdForKill(Creature creature)
	{
		if (TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			ClearState(modifier.CombatTracking, combatId);
			creature.SetCurrentHpInternal(0);
		}
	}

	/// <summary>CurrentHp 被外部设为负值时,转化为濒死债务(与玩家版 setter 拦截对应)。</summary>
	internal static void PreserveNegativeHpAsDyingState(Creature creature, int requestedHp)
	{
		if (!TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			creature.SetCurrentHpInternal(Math.Max(0, requestedHp));
			return;
		}

		HextechMayhemCombatTrackingState tracking = modifier.CombatTracking;
		int deathLimit = GetDeathNegativeHpLimit(creature, modifier);
		int debt = Math.Max(0, -requestedHp);
		if (debt >= deathLimit)
		{
			ClearState(tracking, combatId);
			creature.SetCurrentHpInternal(0);
			return;
		}

		tracking.NearDeathFeastEnemyDebt[combatId] = debt;
		creature.SetCurrentHpInternal(1);
	}

	/// <summary>
	/// HP 被设回正值(转阶段满血、接续、复活、治疗直设)时清掉残留濒死状态,
	/// 避免新阶段仍被判为"濒死中"而禁疗禁格挡。
	/// </summary>
	internal static void ClearIfRecovered(Creature creature, int newHp)
	{
		if (newHp >= 1 && TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			ClearState(modifier.CombatTracking, combatId);
		}
	}

	internal static bool TryGetDisplayedHp(Creature creature, out int displayedHp)
	{
		displayedHp = 0;
		if (!TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId)
			|| !modifier.CombatTracking.NearDeathFeastEnemyDebt.TryGetValue(combatId, out int debt))
		{
			return false;
		}

		displayedHp = -debt;
		return true;
	}

	/// <summary>纯只读:供特效层轮询濒死强度(债务/死亡上限,0..1)。</summary>
	internal static bool TryGetFeastIntensity(Creature creature, out float intensity)
	{
		intensity = 0f;
		if (!TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId)
			|| creature.CurrentHp < 1
			|| !modifier.CombatTracking.NearDeathFeastEnemyDebt.TryGetValue(combatId, out int debt))
		{
			return false;
		}

		int limit = GetDeathNegativeHpLimit(creature, modifier);
		intensity = limit > 0 ? Math.Clamp(debt / (float)limit, 0f, 1f) : 0f;
		return true;
	}

	internal static int GetDeathNegativeHpLimit(Creature creature, HextechMayhemModifier modifier)
	{
		int tier = Math.Clamp(modifier.GetMonsterHexStrengthTier(MonsterHexKind.NearDeathFeast), 1, 3);
		return Math.Max(1, (int)Math.Floor(creature.MaxHp * LimitPercentPerTier * tier));
	}

	private static bool TryGetContext(Creature creature, [NotNullWhen(true)] out HextechMayhemModifier? modifier, out uint combatId)
	{
		modifier = null;
		combatId = 0;
		if (creature.Side != CombatSide.Enemy
			|| creature.CombatId is not { } id
			|| creature.CombatState?.RunState is not { } runState)
		{
			return false;
		}

		HextechMayhemModifier? found = HextechMayhemModifier.FindIn(runState);
		if (found == null || !found.HasActiveMonsterHex(MonsterHexKind.NearDeathFeast))
		{
			return false;
		}

		modifier = found;
		combatId = id;
		return true;
	}

	private static void ClearState(HextechMayhemCombatTrackingState tracking, uint combatId)
	{
		tracking.NearDeathFeastEnemyDebt.Remove(combatId);
		tracking.NearDeathFeastEnemyStrength.Remove(combatId);
	}

	/// <summary>
	/// 生命变化后把力量补到"每 1 负血 1 层"的目标值(只补差额、只增不减,与玩家版一致)。
	/// 先预留目标值再发命令:命令链里若再次失血(重入本方法),只会补新的差额。
	/// </summary>
	internal static async Task SyncStrengthAfterHpChanged(Creature creature)
	{
		if (!TryGetContext(creature, out HextechMayhemModifier? modifier, out uint combatId))
		{
			return;
		}

		HextechMayhemCombatTrackingState tracking = modifier.CombatTracking;
		if (!tracking.NearDeathFeastEnemyDebt.TryGetValue(combatId, out int debt))
		{
			return;
		}

		int delta = debt - tracking.NearDeathFeastEnemyStrength.GetValueOrDefault(combatId, 0);
		if (delta <= 0)
		{
			return;
		}

		tracking.NearDeathFeastEnemyStrength[combatId] = debt;
		await PowerCmd.Apply<StrengthPower>(creature, delta, creature, null);
	}
}
