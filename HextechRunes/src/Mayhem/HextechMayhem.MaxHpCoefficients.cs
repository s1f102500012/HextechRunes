using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

// 敌方持久海克斯的 MaxHp 系数基准/投影维护。状态一律经 CombatTracking 读写。
internal sealed partial class HextechMayhemModifier
{
	private async Task ApplyPersistentMonsterHexes(
		Creature creature,
		bool replayOneShotPowers = false)
	{
		int? maxHpBaseOverride = replayOneShotPowers ? creature.MaxHp : null;
		_ = CaptureMonsterMaxHpCoefficientBase(
			creature,
			maxHpBaseOverride,
			out bool migratedLegacyCoefficients);
		if (migratedLegacyCoefficients)
		{
			// 旧存档的 persistent markers 已经置位，后续各 effect 会跳过 Apply。
			// 基准迁移后必须在这里统一投影一次，否则旧实际 MaxHp 会与新基准永久脱节。
			await ReapplyMonsterMaxHpCoefficients(creature);
		}

		await HextechEnemyHexDispatcher.ForEachActiveOrdered(
			this,
			static effect => effect.PersistentOrder,
			(effect, context) => effect.ApplyPersistentToEnemy(context, creature, maxHpBaseOverride, replayOneShotPowers));
	}

	internal int CaptureMonsterMaxHpCoefficientBase(Creature creature, int? baseMaxHpOverride = null)
	{
		return CaptureMonsterMaxHpCoefficientBase(
			creature,
			baseMaxHpOverride,
			out _);
	}

	private int CaptureMonsterMaxHpCoefficientBase(
		Creature creature,
		int? baseMaxHpOverride,
		out bool migratedLegacyCoefficients)
	{
		migratedLegacyCoefficients = false;
		if (creature.CombatId is not uint combatId)
		{
			return Math.Max(1, baseMaxHpOverride ?? creature.MaxHp);
		}

		if (baseMaxHpOverride is int overriddenBase)
		{
			int normalizedOverride = Math.Max(1, overriddenBase);
			CombatTracking.MonsterMaxHpCoefficientBase[combatId] = normalizedOverride;
			CombatTracking.MonsterMaxHpCoefficientProjected.Remove(combatId);
			return normalizedOverride;
		}

		if (CombatTracking.MonsterMaxHpCoefficientBase.TryGetValue(combatId, out int trackedBase)
			&& trackedBase > 0)
		{
			migratedLegacyCoefficients =
				CombatTracking.MonsterMaxHpCoefficientProjected.GetValueOrDefault(combatId, 0) <= 0
				&& HasAppliedMonsterMaxHpCoefficientMarker(combatId);
			return trackedBase;
		}

		bool coefficientsWereAlreadyApplied = HasAppliedMonsterMaxHpCoefficientMarker(combatId);
		int baseMaxHp = coefficientsWereAlreadyApplied
			? ResolveLegacyMonsterMaxHpCoefficientBase(creature, combatId)
			: Math.Max(1, creature.MaxHp);
		migratedLegacyCoefficients = coefficientsWereAlreadyApplied;

		CombatTracking.MonsterMaxHpCoefficientBase[combatId] = baseMaxHp;
		return baseMaxHp;
	}

	private bool HasAppliedMonsterMaxHpCoefficientMarker(uint combatId)
	{
		return
			CombatTracking.GoliathApplied.Contains(combatId)
			|| CombatTracking.AstralBodyApplied.Contains(combatId)
			|| CombatTracking.GoldenSpatulaApplied.Contains(combatId)
			|| CombatTracking.StatsApplied.Contains(combatId)
			|| CombatTracking.StatsOnStatsApplied.Contains(combatId)
			|| CombatTracking.StatsOnStatsOnStatsApplied.Contains(combatId)
			|| CombatTracking.MadScientistApplied.Contains(combatId)
			|| CombatTracking.MindToMatterApplied.Contains(combatId)
			|| CombatTracking.MoreTheMerrierApplied.Contains(combatId)
			|| CombatTracking.TankEngineStacks.GetValueOrDefault(combatId, 0) > 0;
	}

	private int ResolveLegacyMonsterMaxHpCoefficientBase(Creature creature, uint combatId)
	{
		HextechEnemyHexContext context = new(this);
		List<decimal> appliedFixedBonusFractions = new(3);
		if (CombatTracking.GoliathApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(context.TierValue(MonsterHexKind.Goliath, 0.20m, 0.30m, 0.40m));
		}

		if (CombatTracking.AstralBodyApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(context.TierValue(MonsterHexKind.AstralBody, 0.20m, 0.30m, 0.40m));
		}

		if (CombatTracking.GoldenSpatulaApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(context.TierValue(MonsterHexKind.GoldenSpatula, 0.25m, 0.30m, 0.45m));
		}

		if (CombatTracking.StatsApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.Stats, context.GetStrengthTier(MonsterHexKind.Stats)));
		}

		if (CombatTracking.StatsOnStatsApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStats, context.GetStrengthTier(MonsterHexKind.StatsOnStats)));
		}

		if (CombatTracking.StatsOnStatsOnStatsApplied.Contains(combatId))
		{
			appliedFixedBonusFractions.Add(EnemyAttributeBoostValues.GetBonusFraction(MonsterHexKind.StatsOnStatsOnStats, context.GetStrengthTier(MonsterHexKind.StatsOnStatsOnStats)));
		}

		decimal madScientistLossFraction = CombatTracking.MadScientistApplied.Contains(combatId)
			? context.TierValue(MonsterHexKind.MadScientist, 0.30m, 0.15m, 0.00m)
			: 0m;
		int tankEngineStacks = Math.Max(0, CombatTracking.TankEngineStacks.GetValueOrDefault(combatId, 0));
		int? rawMonsterMaxHp = creature.MonsterMaxHpBeforeModification is int rawMaxHp && rawMaxHp > 0
			? rawMaxHp
			: null;
		int migratedBaseMaxHp = HextechLegacyEnemyMaxHpMigration.ResolveBaseMaxHp(
			creature.MaxHp,
			rawMonsterMaxHp,
			appliedFixedBonusFractions,
			madScientistLossFraction,
			tankEngineStacks);
		HextechLog.Info(
			"Mayhem", $"Migrated legacy enemy max HP base: combatId={combatId} current={creature.MaxHp} raw={rawMonsterMaxHp?.ToString() ?? "unknown"} fixedBonuses={string.Join(",", appliedFixedBonusFractions)} madLoss={madScientistLossFraction} tankStacks={tankEngineStacks} base={migratedBaseMaxHp}");
		return migratedBaseMaxHp;
	}

	internal async Task ReapplyMonsterMaxHpCoefficients(Creature creature, int? baseMaxHpOverride = null)
	{
		int baseMaxHp = CaptureMonsterMaxHpCoefficientBase(creature, baseMaxHpOverride);
		if (baseMaxHpOverride == null)
		{
			baseMaxHp = ReconcileObservedMonsterMaxHpChange(creature, baseMaxHp);
		}

		decimal scale = GetMonsterMaxHpCoefficientScale(creature);
		int expectedMaxHp = (int)Math.Clamp(Math.Floor(baseMaxHp * scale), 1m, int.MaxValue);
		int delta = expectedMaxHp - creature.MaxHp;
		if (delta > 0)
		{
			await GainMonsterMaxHpWithoutHeal(creature, delta);
			TrackProjectedMonsterMaxHp(creature);
			return;
		}

		if (delta < 0)
		{
			await CreatureCmd.SetMaxHp(creature, expectedMaxHp);
		}

		TrackProjectedMonsterMaxHp(creature);
		await KeepFurCoatMarkedEnemyAtOneHp(creature);
	}

	private int ReconcileObservedMonsterMaxHpChange(Creature creature, int baseMaxHp)
	{
		if (creature.CombatId is not uint combatId
			|| !CombatTracking.MonsterMaxHpCoefficientProjected.TryGetValue(
				combatId,
				out int projectedMaxHp)
			|| projectedMaxHp <= 0
			|| projectedMaxHp == creature.MaxHp)
		{
			return baseMaxHp;
		}

		long observedDelta = (long)creature.MaxHp - projectedMaxHp;
		int adjustedBaseMaxHp = (int)Math.Clamp(
			(long)baseMaxHp + observedDelta,
			1L,
			int.MaxValue);
		CombatTracking.MonsterMaxHpCoefficientBase[combatId] = adjustedBaseMaxHp;
		HextechLog.Info(
			"Mayhem", $"Reconciled enemy max HP base after an external change: "
			+ $"combatId={combatId} base={baseMaxHp} projected={projectedMaxHp} "
			+ $"observed={creature.MaxHp} adjustedBase={adjustedBaseMaxHp}");
		return adjustedBaseMaxHp;
	}

	private void TrackProjectedMonsterMaxHp(Creature creature)
	{
		if (creature.CombatId is uint combatId)
		{
			CombatTracking.MonsterMaxHpCoefficientProjected[combatId] =
				Math.Max(1, creature.MaxHp);
		}
	}

	private decimal GetMonsterMaxHpCoefficientScale(Creature creature)
	{
		HextechEnemyHexContext context = new(this);
		return HextechEnemyCoefficientHelper.CombineBonusFractionsByHex(
			HextechEnemyHexEffects.GetActive(this)
				.OfType<IHextechEnemyMaxHpCoefficientProvider>()
				.Select(provider =>
				(((HextechEnemyHexEffect)provider).Kind, provider.GetMaxHpBonusFraction(context, creature))));
	}

	private static async Task GainMonsterMaxHpWithoutHeal(Creature creature, int amount)
	{
		if (amount <= 0)
		{
			return;
		}

		int oldMaxHp = creature.MaxHp;
		int oldCurrentHp = creature.CurrentHp;
		await CreatureCmd.SetMaxHp(creature, oldMaxHp + amount);

		int actualMaxHpGain = Math.Max(0, creature.MaxHp - oldMaxHp);
		if (actualMaxHpGain <= 0)
		{
			return;
		}

		int newCurrentHp = IsFurCoatMarkedEnemy(creature)
			? 1
			: Math.Min(creature.MaxHp, oldCurrentHp + actualMaxHpGain);
		if (newCurrentHp != creature.CurrentHp)
		{
			await CreatureCmd.SetCurrentHp(creature, newCurrentHp);
		}
	}

	private static Task KeepFurCoatMarkedEnemyAtOneHp(Creature creature)
	{
		if (IsFurCoatMarkedEnemy(creature) && creature.CurrentHp != 1)
		{
			return CreatureCmd.SetCurrentHp(creature, 1m);
		}

		return Task.CompletedTask;
	}

	private static bool IsFurCoatMarkedEnemy(Creature creature)
	{
		if (creature.Side != CombatSide.Enemy || !creature.IsAlive || creature.CombatState == null)
		{
			return false;
		}

		foreach (RelicModel relic in creature.CombatState.Players.SelectMany(static player => player.Relics))
		{
			if (relic is not FurCoat furCoat || furCoat.Owner?.RunState.CurrentMapPoint == null)
			{
				continue;
			}

			if (furCoat.GetMarkedCoords()?.Contains(furCoat.Owner.RunState.CurrentMapPoint.coord) == true)
			{
				return true;
			}
		}

		return false;
	}

	internal void UpdateEnemyScale(Creature creature)
	{
		float baseScale = HasActiveMonsterHex(MonsterHexKind.Goliath) ? GoliathEnemyHex.BodyScale : 1f;
		float giantSlayerShrink = HasActiveMonsterHex(MonsterHexKind.GiantSlayer) ? GiantSlayerEnemyHex.BodyScaleShrink : 0f;
		int tankStacks = creature.CombatId == null ? 0 : CombatTracking.TankEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		int shrinkStacks = creature.CombatId == null ? 0 : CombatTracking.ShrinkEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		float finalScale = Math.Max(
			HextechPlayerBodyScaleHelper.MinCreatureBodyScale,
			baseScale
				+ tankStacks * TankEngineEnemyHex.BodyScalePerStack
				- shrinkStacks * ShrinkEngineEnemyHex.BodyScalePerStack
				- giantSlayerShrink);
		HextechPresentation.TryRun(
			"Mayhem",
			"Enemy scale visual failed",
			() => NCombatRoom.Instance?.GetCreatureNode(creature)?.SetDefaultScaleTo(finalScale, 0f));
	}
}
