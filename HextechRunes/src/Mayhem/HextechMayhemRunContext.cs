namespace HextechRunes;

internal sealed class HextechMayhemRunContext
{
	public HextechMayhemActState ActState { get; } = new();
	public HextechMayhemCombatTrackingState CombatTracking { get; } = new();
	public HextechMayhemChoiceHistoryState ChoiceHistory { get; } = new();
	public HextechRuneSelectionJournalState RuneSelectionJournal { get; } = new();
	public HextechActiveMonsterHexCache ActiveMonsterHexCache { get; } = new();
	public HextechHexCountState PlayerHexCounts { get; } = new(HextechRuneConfiguration.NormalizePlayerHexCounts);
	public HextechHexCountState EnemyHexCounts { get; } = new(HextechHexCountState.NormalizeEnemyCounts);
	public HextechPlayerRuneConfigSnapshotState PlayerRuneConfig { get; } = new();
	public HextechRunConfigurationSnapshot? RunConfigurationSnapshot { get; set; }
	public int HexCountRecoveryBaseline { get; set; }
	public int MonsterHexStrengthTierFloor { get; set; }
	public int ActSelectionIndexOffset { get; set; }
	public int? ActiveExtraStageIndex { get; set; }
	public int EnemyTezcatarasMercyCombatCounter { get; set; }
	public bool HostUsesBetterMultiplayerScaling { get; set; }

	// null 表示尚未冻结，使用本局有效配置；首次 act-roll 同步后冻结一次。
	// 载入时由 SavedProperty 恢复，客户端使用房主同步的快照值。
	public bool? ModActiveForRun { get; set; }

	// 原版游戏模式不满足本模组流程假设时的原因（null 表示支持）。按各端一致的 RunState.GameMode 在
	// 挂上/读入 Modifier 时重算，不随存档保存，换局时由新局重算覆盖。
	public string? UnsupportedGameModeReason { get; set; }

	public bool IsEndlessLoopActive => MonsterHexStrengthTierFloor >= 3;

	public void ResetForNewRun(IReadOnlyList<int> playerHexCountsByAct, IReadOnlyList<int> enemyHexCountsByAct)
	{
		PlayerHexCounts.Set(playerHexCountsByAct);
		EnemyHexCounts.Set(enemyHexCountsByAct);
		ModActiveForRun = null;
		ActSelectionIndexOffset = 0;
		ActiveExtraStageIndex = null;
		ResetProgressState(hexCountRecoveryBaseline: 0, monsterHexStrengthTierFloor: 0);
		ActState.Reset();
		ChoiceHistory.Reset();
		RuneSelectionJournal.Reset();
		ResetCombatTracking();
	}

	public void ResetForEndlessLoop(int hexCountRecoveryBaseline)
	{
		ActSelectionIndexOffset = Math.Max(ActState.ActCount, ActSelectionIndexOffset + 1);
		ActiveExtraStageIndex = null;
		ResetProgressState(hexCountRecoveryBaseline, monsterHexStrengthTierFloor: 3);
		// 幕状态不清：无尽继续沿用单调递增的阶段序号，保留每次获得敌方海克斯的分组。
		ChoiceHistory.Reset();
		RuneSelectionJournal.Reset(preserveCharacterWeights: true);
		ResetCombatTracking();
	}

	public void ResetForDebugMonsterHex(int actIndex, MonsterHexKind hex, HextechRarityTier rarity)
	{
		PlayerHexCounts.ResetToDefault();
		EnemyHexCounts.ResetToDefault();
		ActSelectionIndexOffset = 0;
		ActiveExtraStageIndex = null;
		ResetProgressState(hexCountRecoveryBaseline: 0, monsterHexStrengthTierFloor: 0);
		ActState.DebugSetOnlyMonsterHex(actIndex, hex, rarity);
		ChoiceHistory.Reset();
		RuneSelectionJournal.Reset();
		ResetCombatTracking();
	}

	public void ResetCombatTracking(bool atCombatStart = false)
	{
		CombatTracking.Reset(atCombatStart);
	}

	private void ResetProgressState(int hexCountRecoveryBaseline, int monsterHexStrengthTierFloor)
	{
		HexCountRecoveryBaseline = hexCountRecoveryBaseline;
		MonsterHexStrengthTierFloor = monsterHexStrengthTierFloor;
		EnemyTezcatarasMercyCombatCounter = 0;
	}
}
