namespace HextechRunes;

internal static class HextechCombatProcTracker
{
	public static bool TryConsumeLimitedProc(Dictionary<uint, int> counts, Creature creature, int maxPerTurn)
	{
		if (creature.CombatId == null)
		{
			return false;
		}

		uint combatId = creature.CombatId.Value;
		int current = counts.GetValueOrDefault(combatId, 0);
		if (current >= maxPerTurn)
		{
			return false;
		}

		counts[combatId] = current + 1;
		return true;
	}

	public static bool TryMarkPersistentHexApplied(HashSet<uint> appliedSet, Creature creature, bool forceReapply = false)
	{
		if (creature.CombatId == null)
		{
			return false;
		}

		bool firstApplication = appliedSet.Add(creature.CombatId.Value);
		return forceReapply || firstApplication;
	}

	public static int GetPlayerRuneProcsThisTurn(HextechMayhemCombatTrackingState tracking, Player player, string procKey)
	{
		return tracking.PlayerRuneProcsThisTurn.GetValueOrDefault(GetPlayerRuneProcKey(player, procKey), 0);
	}

	public static bool TryConsumePlayerRuneProcThisTurn(
		HextechMayhemCombatTrackingState tracking,
		Player player,
		string procKey,
		int maxPerTurn)
	{
		if (maxPerTurn <= 0)
		{
			return false;
		}

		string key = GetPlayerRuneProcKey(player, procKey);
		int current = tracking.PlayerRuneProcsThisTurn.GetValueOrDefault(key, 0);
		if (current >= maxPerTurn)
		{
			return false;
		}

		tracking.PlayerRuneProcsThisTurn[key] = current + 1;
		return true;
	}

	public static int GetPlayerRuneProcsInCombat(HextechMayhemCombatTrackingState tracking, Player player, string procKey)
	{
		return tracking.PlayerRuneProcsThisCombat.GetValueOrDefault(GetPlayerRuneProcKey(player, procKey), 0);
	}

	public static int ConsumePlayerRuneProcInCombat(HextechMayhemCombatTrackingState tracking, Player player, string procKey)
	{
		string key = GetPlayerRuneProcKey(player, procKey);
		int current = tracking.PlayerRuneProcsThisCombat.GetValueOrDefault(key, 0);
		tracking.PlayerRuneProcsThisCombat[key] = current + 1;
		return current;
	}

	public static int GetGlobalProcsInCombat(HextechMayhemCombatTrackingState tracking, string procKey)
	{
		return tracking.GlobalProcsThisCombat.GetValueOrDefault(procKey, 0);
	}

	public static int ConsumeGlobalProcInCombat(HextechMayhemCombatTrackingState tracking, string procKey)
	{
		int current = tracking.GlobalProcsThisCombat.GetValueOrDefault(procKey, 0);
		tracking.GlobalProcsThisCombat[procKey] = current + 1;
		return current;
	}

	// 非遗物模型（Power）的「持有者每回合 1 次」：记在能力实例自己的标记上（由调用方在持有者回合开始时清），
	// 与 HextechRelicBase.TryConsumeTurnProc 同口径，不再借用 Modifier 的共享账（见那里的说明）。
	public static bool HasOwnerTurnProcTriggered(bool localTriggered)
	{
		return localTriggered;
	}

	public static bool TryConsumeOwnerTurnProc(ref bool localTriggered)
	{
		if (localTriggered)
		{
			return false;
		}

		localTriggered = true;
		return true;
	}

	public static bool TrackPlayerAttackCardPlayedThisTurn(HextechMayhemCombatTrackingState tracking, CardPlay cardPlay)
	{
		if (!cardPlay.IsFirstInSeries
			|| cardPlay.IsAutoPlay
			|| cardPlay.Card.Owner?.Creature.Side != CombatSide.Player
			|| cardPlay.Card.Type != CardType.Attack)
		{
			return false;
		}

		ulong playerId = cardPlay.Card.Owner.NetId;
		tracking.PlayerAttackCardsPlayedThisTurn[playerId] = tracking.PlayerAttackCardsPlayedThisTurn.GetValueOrDefault(playerId, 0) + 1;
		return true;
	}

	public static int GetPlayerAttacksPlayedThisTurn(HextechMayhemCombatTrackingState tracking, CardModel card)
	{
		if (card.Owner == null)
		{
			return 0;
		}

		return tracking.PlayerAttackCardsPlayedThisTurn.GetValueOrDefault(card.Owner.NetId, 0);
	}

	private static string GetPlayerRuneProcKey(Player player, string procKey)
	{
		return $"{player.NetId}:{procKey}";
	}
}
