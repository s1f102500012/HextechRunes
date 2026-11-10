namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal int StageCount => ActState.ActCount;

	public int[] EnemyHexCountsByAct => EnemyHexCounts.Snapshot;

	public bool IsActResolved(int actIndex)
	{
		return IsStageResolved(GetSelectionIndexForAct(actIndex));
	}

	internal bool IsStageResolved(int stageIndex)
	{
		return ActState.IsResolved(stageIndex);
	}

	internal void SetStageResolved(int stageIndex, bool resolved)
	{
		ActState.SetResolved(stageIndex, resolved);
	}

	internal int GetSelectionIndexForAct(int actIndex)
	{
		return actIndex < 0 ? -1 : _runContext.ActSelectionIndexOffset + actIndex;
	}

	internal int GetCurrentActSelectionIndex()
	{
		return GetSelectionIndexForAct(RunState.CurrentActIndex);
	}

	internal int GetCurrentStageIndex()
	{
		return _runContext.ActiveExtraStageIndex ?? GetCurrentActSelectionIndex();
	}

	internal int ActivateExtraStage(string stageKey)
	{
		string scopedKey = $"{_runContext.ActSelectionIndexOffset}:{stageKey}";
		int minimumIndex = Math.Max(
			GetSelectionIndexForAct(RunState.Acts.Count),
			ActState.ActCount);
		int stageIndex = ActState.GetOrCreateExtraStageIndex(scopedKey, minimumIndex);
		_runContext.ActiveExtraStageIndex = stageIndex;
		return stageIndex;
	}

	internal void ClearActiveExtraStage()
	{
		_runContext.ActiveExtraStageIndex = null;
	}

	public bool TryRecoverResolvedActsFromPlayerRelics(string reason, int? currentStageIndex = null)
	{
		int stageIndex = currentStageIndex ?? GetCurrentStageIndex();
		int maxRecoverActIndex = HasRuneSelectionJournalEntriesForAct(stageIndex)
			? stageIndex - 1
			: stageIndex;
		HextechMayhemActRecoveryResult recovery = HextechMayhemActRecovery.RecoverResolvedActs(
			RunState,
			ActState,
			ChoiceHistory,
			HexCountRecoveryBaseline,
			PlayerHexCountsByAct,
			maxRecoverActIndex,
			stageIndex);
		if (recovery.Changed)
		{
			HextechLog.Info("Mayhem", $"Recovered resolved stages from saved choices/player relics: reason={reason} currentAct={RunState.CurrentActIndex} currentStage={stageIndex} recoverThrough={recovery.RecoverThroughAct} telemetryThrough={recovery.TelemetryRecoverThroughAct} countThrough={recovery.CountRecoverThroughAct} baseline={HexCountRecoveryBaseline} {ActState.Describe()} counts={DescribePlayerHexCounts()} choices={DescribeTelemetryChoiceCounts()}");
		}

		return recovery.Changed;
	}

	public string DescribeActState()
	{
		return $"offset={_runContext.ActSelectionIndexOffset} activeExtra={_runContext.ActiveExtraStageIndex?.ToString() ?? "none"} {ActState.Describe()}";
	}

	public HextechRarityTier? GetRarityForAct(int actIndex)
	{
		return ActState.GetRarity(actIndex);
	}

	public void SetRarityForAct(int actIndex, HextechRarityTier rarity)
	{
		ActState.SetRarity(actIndex, rarity);
	}

	public IReadOnlyList<MonsterHexKind> GetMonsterHexesForAct(int actIndex)
	{
		return ActState.GetMonsterHexes(actIndex);
	}

	public void SetMonsterHexesForAct(int actIndex, IEnumerable<MonsterHexKind> hexes)
	{
		ActState.SetMonsterHexes(actIndex, hexes);
	}

	public IReadOnlyList<MonsterHexKind> GetActiveMonsterHexes()
	{
		return ActiveMonsterHexCache.Get(this, ActState, GetCurrentStageIndex(), IsInCombatRoomForMonsterHexRecovery());
	}

	// 当前生效的敌方海克斯效果，按注册顺序；返回的数组只读，状态变化后由缓存换成新数组。
	internal HextechEnemyHexEffect[] GetActiveEnemyHexEffects(out int[] orderIndexes)
	{
		return ActiveMonsterHexCache.GetActiveEffects(this, ActState, GetCurrentStageIndex(), IsInCombatRoomForMonsterHexRecovery(), out orderIndexes);
	}

	public IReadOnlyList<MonsterHexKind> GetActiveMonsterHexesBeforeAct(int actIndex)
	{
		return ActState.GetActiveMonsterHexesBeforeAct(actIndex);
	}

	public IReadOnlyList<MonsterHexKind> GetKnownMonsterHexes()
	{
		return ActState.GetKnownMonsterHexes();
	}

	internal IReadOnlyList<IReadOnlyList<MonsterHexKind>> GetMonsterHexRows()
	{
		return ActState.GetMonsterHexRows();
	}

	internal bool ShouldRecoverMonsterHexInCombat(int actIndex)
	{
		return actIndex <= GetCurrentStageIndex() && RunState.CurrentRoom is CombatRoom;
	}

	// 等于 ShouldRecoverMonsterHexInCombat(GetCurrentStageIndex())：当前阶段恒满足前一项，只剩是否在战斗房间。
	private bool IsInCombatRoomForMonsterHexRecovery()
	{
		return RunState.CurrentRoom is CombatRoom;
	}

	public void ResetForNewRun()
	{
		HextechEnemyHexEffects.ResetAllRunScopedState();
		HextechRunConfigurationSnapshot snapshot = CreateNewRunConfigurationSnapshot();
		_runContext.ResetForNewRun(snapshot.PlayerHexCountsByAct, snapshot.EnemyHexCountsByAct);
		SetRunConfigurationSnapshot(snapshot, "new run");
		HextechLog.Info("Mayhem", $"Reset for new run: playerCounts={string.Join(",", PlayerHexCountsByAct)} enemyCounts={string.Join(",", EnemyHexCountsByAct)} playerConfigDisabled={PlayerRuneConfigDisabledIds.Count}");
	}

	public void ResetForEndlessLoop(string reason)
	{
		HextechEnemyHexEffects.ResetAllRunScopedState();
		_runContext.ResetForEndlessLoop(HextechMayhemActRecovery.GetMinimumPlayerHexCount(RunState));
		HextechLog.Info("Mayhem", $"Reset for endless loop: reason={reason} baseline={HexCountRecoveryBaseline} strengthTierFloor={MonsterHexStrengthTierFloor} enemyCounts={string.Join(",", EnemyHexCountsByAct)} counts={DescribePlayerHexCounts()} {ActState.Describe()}");
		HextechRunLifecycleHooks.HandleEndlessLoopReset(this, reason);
	}

	public void DebugSetOnlyMonsterHex(int actIndex, MonsterHexKind hex, HextechRarityTier rarity)
	{
		_runContext.ResetForDebugMonsterHex(actIndex, hex, rarity);
		SetRunConfigurationSnapshot(HextechRuneConfiguration.GetSnapshot(), "debug set monster hex");
	}

	public bool DebugAddMonsterHex(MonsterHexKind hex)
	{
		return ActState.AddCarriedMonsterHex(hex);
	}

	public bool DebugRemoveMonsterHex(MonsterHexKind hex)
	{
		return ActState.RemoveMonsterHexEverywhere(hex);
	}

	public bool HasActiveMonsterHex(MonsterHexKind hex)
	{
		return ActiveMonsterHexCache.Contains(this, ActState, GetCurrentStageIndex(), IsInCombatRoomForMonsterHexRecovery(), hex);
	}

	public int GetMonsterHexStrengthTier(MonsterHexKind hex)
	{
		return GetMonsterHexStrengthTierForAct(hex, RunState.CurrentActIndex);
	}

	public int GetMonsterHexStrengthTierForAct(MonsterHexKind hex, int actIndex)
	{
		_ = hex;
		// Enemy hex strength tracks the active act, even for hexes obtained in earlier acts.
		int actStrengthTier = Math.Clamp(actIndex + 1, 1, 3);
		return Math.Max(actStrengthTier, MonsterHexStrengthTierFloor);
	}

	public int GetEnemyHexCountForAct(int actIndex)
	{
		return EnemyHexCounts.GetForAct(actIndex, IsEndlessLoopActive);
	}

	public void SetEnemyHexCountsByActSnapshot(IReadOnlyList<int> counts, string reason)
	{
		EnemyHexCounts.Set(counts);
		HextechLog.Info("Mayhem", $"EnemyHexCountsByAct snapshot set: reason={reason} counts={string.Join(",", EnemyHexCountsByAct)}");
	}

	internal bool IncrementEnemyTezcatarasMercyCombatCounter(int interval)
	{
		EnemyTezcatarasMercyCombatCounter++;
		if (EnemyTezcatarasMercyCombatCounter < interval)
		{
			return false;
		}

		EnemyTezcatarasMercyCombatCounter = 0;
		return true;
	}
}
