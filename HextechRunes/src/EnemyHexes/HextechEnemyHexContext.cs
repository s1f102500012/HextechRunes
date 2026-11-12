using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

// turnParticipants 只在回合开始/结束钩子里有值：队友的额外回合只带那名玩家重入这些钩子。
internal readonly struct HextechEnemyHexContext(HextechMayhemModifier modifier, IEnumerable<Creature>? turnParticipants = null)
{
	internal HextechMayhemModifier Modifier => modifier;

	internal RunState RunState => modifier.ActiveRunState;

	internal HextechMayhemCombatTrackingState Tracking => modifier.CombatTracking;

	// 敌方海克斯按人数缩放时的人数上下限；本体各处缩放（含 HextechEnemyPowerScalingHooks.Rules）同一口径。
	internal const int MaxScalingPlayerCount = 16;

	internal int ScalingPlayerCount => ClampScalingPlayerCount(RunState.Players.Count);

	internal static int ClampScalingPlayerCount(int playerCount)
	{
		return Math.Clamp(playerCount, 1, MaxScalingPlayerCount);
	}

	internal bool IsActive(MonsterHexKind kind)
	{
		return modifier.HasActiveMonsterHex(kind);
	}

	internal int GetStrengthTier(MonsterHexKind kind)
	{
		return modifier.GetMonsterHexStrengthTier(kind);
	}

	internal int TierValue(MonsterHexKind kind, int tier1, int tier2, int tier3)
	{
		return GetStrengthTier(kind) switch
		{
			<= 1 => tier1,
			2 => tier2,
			_ => tier3
		};
	}

	// 口径见 HextechRoundInterval；额外回合不推进回合号，按海克斯和回合防重。
	internal bool TryConsumeRoundInterval(
		MonsterHexKind kind,
		HextechCombatState combatState,
		int everyNRounds)
	{
		return HextechRoundInterval.IsDue(combatState.RoundNumber, everyNRounds)
			&& TryConsumeOncePerRound(kind, combatState);
	}

	// 额外回合不推进 RoundNumber 且回合开始钩子会重入：同一海克斯在同一回合号只放行一次。
	internal bool TryConsumeOncePerRound(MonsterHexKind kind, HextechCombatState combatState)
	{
		return HextechCombatProcTracker.ConsumeGlobalProcInCombat(
			Tracking,
			$"round-once:{kind}:{combatState.RoundNumber}") <= 0;
	}

	internal decimal TierValue(MonsterHexKind kind, decimal tier1, decimal tier2, decimal tier3)
	{
		return GetStrengthTier(kind) switch
		{
			<= 1 => tier1,
			2 => tier2,
			_ => tier3
		};
	}

	/// <summary>
	/// 「玩家每打出 1 张牌」类敌方海克斯的统一口径：玩家侧的手动打出（自动打出、同一张牌的重放不计），且发生在本局战斗中。
	/// </summary>
	internal bool IsManualPlayerCardPlay(
		CardPlay cardPlay,
		[NotNullWhen(true)] out Player? owner,
		[NotNullWhen(true)] out HextechCombatState? combatState)
	{
		owner = cardPlay.Card.Owner;
		combatState = owner?.Creature.CombatState;
		return cardPlay.IsFirstInSeries
			&& !cardPlay.IsAutoPlay
			&& owner != null
			&& owner.Creature.Side == CombatSide.Player
			&& combatState != null
			&& combatState.RunState == RunState;
	}

	/// <summary>玩家侧生物里存活的玩家，按 NetId 稳定排序（两端遍历顺序一致）。</summary>
	internal static IEnumerable<Player> GetAlivePlayersByNetId(IEnumerable<Creature> playerSideCreatures)
	{
		return playerSideCreatures
			.Where(static creature => !creature.IsDead)
			.Select(static creature => creature.Player)
			.OfType<Player>()
			.OrderBy(static player => player.NetId);
	}

	/// <summary>最大生命的一个比例，向下取整且至少为 1。</summary>
	internal static int FractionOfMaxHp(Creature creature, decimal fraction)
	{
		return FractionOfMaxHp(creature.MaxHp, fraction);
	}

	/// <inheritdoc cref="FractionOfMaxHp(Creature, decimal)"/>
	internal static int FractionOfMaxHp(int maxHp, decimal fraction)
	{
		return Math.Max(1, (int)Math.Floor(maxHp * fraction));
	}

	internal IReadOnlyList<Creature> GetAliveEnemies(HextechCombatState combatState)
	{
		return HextechCombatCreatureHelper.GetAliveEnemies(combatState);
	}

	internal IReadOnlyList<Creature> GetAlivePlayerSideCreatures(HextechCombatState combatState)
	{
		return HextechCombatCreatureHelper.GetAlivePlayerSideCreatures(combatState);
	}

	// 玩家侧回合钩子里作用于"每名玩家"的效果用这个：只取本次真正开始/结束回合的玩家（宠物随主人）。
	internal IReadOnlyList<Creature> GetAlivePlayerSideCreaturesTakingTurn(HextechCombatState combatState)
	{
		return FilterTakingTurn(GetAlivePlayerSideCreatures(combatState), turnParticipants);
	}

	internal static IReadOnlyList<Creature> FilterTakingTurn(IReadOnlyList<Creature> creatures, IEnumerable<Creature>? participants)
	{
		return participants == null
			? creatures
			: creatures.Where(creature => HextechTurnParticipants.Includes(participants, creature)).ToList();
	}

	internal Task TryApplyServantMasterIllusion(Creature creature, Creature? applier, CardModel? cardSource)
	{
		return modifier.TryApplyServantMasterIllusion(creature, applier, cardSource);
	}

	internal void UpdateEnemyScale(Creature creature)
	{
		modifier.UpdateEnemyScale(creature);
	}
}
