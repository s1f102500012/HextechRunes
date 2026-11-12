namespace HextechRunes;

internal sealed partial class HextechMayhemCombatTrackingState
{
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<uint, int> SlapProcsThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<uint, int> TormentorProcsThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<uint, int> CourageProcsThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<uint, int> BloodPactProcsThisTurn = new();
	// 只剩敌方海克斯经 HextechCombatProcTracker 使用（我方符文计数在遗物实例上）；字段名是快照 JSON 键，不改名。
	// 键为"玩家 NetId:计次键"。只在该玩家自己开始回合时清（见 ResetPlayerRuneProcsThisTurn）：
	// 队友的额外回合不重置别人的每回合次数，自己的额外回合算新回合，与本地按 TurnNumber 计数一致。
	[CombatTrackingClear(CombatTrackingClearPhase.None)]
	public readonly Dictionary<string, int> PlayerRuneProcsThisTurn = new();
	public readonly Dictionary<string, int> PlayerRuneProcsThisCombat = new();
	public readonly Dictionary<string, int> GlobalProcsThisCombat = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart | CombatTrackingClearPhase.EnemyTurnStart)]
	public readonly Dictionary<uint, int> BloodArmorHpLossThisPlayerTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<uint, int> ClownCollegeProcsThisTurn = new();
	public readonly HashSet<uint> EscapePlanTriggered = new();
	public readonly HashSet<uint> RepulsorTriggered = new();
	public readonly HashSet<uint> DawnTriggered = new();
	// 敌方濒死狂宴:负血债务(含 key=濒死激活中)与已发放的力量层数(差额补给)。
	public readonly Dictionary<uint, int> NearDeathFeastEnemyDebt = new();
	public readonly Dictionary<uint, int> NearDeathFeastEnemyStrength = new();
	public readonly HashSet<uint> SpeedDemonPending = new();
	public readonly Dictionary<uint, int> DelayedEnemyHealingBlock = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly HashSet<uint> DevilsDanceTriggeredThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly HashSet<uint> FinalFormTriggeredThisTurn = new();
	public readonly Dictionary<uint, uint> FeyMagicPendingNoDrawPlayers = new();
	public readonly Dictionary<uint, int> MikaelsBlessingTriggers = new();
	public readonly HashSet<uint> GoliathApplied = new();
	public readonly HashSet<uint> ProtectiveVeilApplied = new();
	public readonly HashSet<uint> MindToMatterApplied = new();
	public readonly HashSet<uint> MoreTheMerrierApplied = new();
	public readonly HashSet<uint> ScrollOfBitingApplied = new();
	public readonly HashSet<uint> OwlMagistrateApplied = new();
	public readonly HashSet<uint> ThornmailApplied = new();
	public readonly HashSet<uint> SuperBrainApplied = new();
	public readonly HashSet<uint> AstralBodyApplied = new();
	public readonly HashSet<uint> MadScientistApplied = new();
	public readonly HashSet<uint> UnmovableMountainApplied = new();
	public readonly HashSet<uint> GoldenSpatulaApplied = new();
	public readonly HashSet<uint> StatsApplied = new();
	public readonly HashSet<uint> StatsOnStatsApplied = new();
	public readonly HashSet<uint> StatsOnStatsOnStatsApplied = new();
	public readonly HashSet<uint> DoormakerRealStartApplied = new();
	public readonly Dictionary<uint, int> TestSubjectPhaseStartApplied = new();
	public readonly Dictionary<uint, int> MonsterMaxHpCoefficientBase = new();
	public readonly Dictionary<uint, int> MonsterMaxHpCoefficientProjected = new();
	public readonly Dictionary<uint, int> TankEngineStacks = new();
	public readonly Dictionary<uint, int> TankEngineLastAppliedRound = new();
	public readonly Dictionary<uint, int> ShrinkEngineStacks = new();
	public readonly HashSet<uint> MountainSoulHasPreviousTurn = new();
	public readonly HashSet<uint> MountainSoulDamagedSinceLastTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.EveryTurnBoundary)]
	public readonly Dictionary<ulong, int> PlayerAttackCardsPlayedThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.EveryTurnBoundary)]
	public readonly Dictionary<ulong, int> BackToBasicsCardsPlayedThisTurn = new();
	// 沃格莫特之灵(敌方)的全队累计抽牌数(合计存在键 0,见 HextechEnemyDrawProgress);快照里的 JSON 键保持旧名 PlayerCardsDrawnThisCombat。
	public readonly Dictionary<ulong, int> WarmogsSpiritPlayerCardsDrawnThisCombat = new();
	public readonly Dictionary<ulong, int> SwiftAndSafePlayerCardsDrawnThisCombat = new();
	public readonly Dictionary<ulong, int> NightstalkingPlayerCardsDrawnThisCombat = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly HashSet<ulong> MindOverMatterPlayersTriggeredThisTurn = new();
	[CombatTrackingClear(CombatTrackingClearPhase.EveryTurnBoundary)]
	public readonly Dictionary<uint, int> EnemyPorcupineTemporaryThornsThisTurn = new();
	// 升级：棘刺蟾蜍在玩家回合开始时给的临时荆棘，按敌人 CombatId 记实际给出的层数，玩家回合结束时撤回。
	public readonly Dictionary<uint, int> EnemySpinyToadTemporaryThorns = new();
	// 豪猪/百炼成钢按"每 N 次未被格挡伤害"触发:计数按敌人 CombatId 跨回合累积,余数带到下回合,只在战斗结束清空。
	public readonly Dictionary<uint, int> EnemyPorcupineUnblockedHitsThisCombat = new();
	public readonly Dictionary<uint, int> EnemyHundredRefinementsUnblockedHitsThisCombat = new();
	[CombatTrackingClear(CombatTrackingClearPhase.PlayerTurnStart)]
	public readonly Dictionary<ulong, int> InspectExtraDrawsPreventedThisTurn = new();
	// 原版的玩家回合判定从回合开始 hook 起即为 true；单独记录进入出牌阶段前的窗口。
	[CombatTrackingTransient]
	public readonly HashSet<ulong> PlayersAwaitingPlayPhase = new();
	// 我紧握(敌方)按每名玩家手动打出的牌数计数，满阈值失去 1 点能量；余数跨回合累积，只在战斗结束清空。
	public readonly Dictionary<ulong, int> GripPlayerCardsPlayedThisCombat = new();
	[CombatTrackingTransient]
	public string? LastEnemyThresholdTriggerKey;
	[CombatTrackingTransient]
	public bool HandlingMonsterTormentorBurn;
	[CombatTrackingTransient]
	public bool HandlingServantMasterIllusion;

	public void PreparePlayerSideTurnStart()
	{
		HextechMayhemCombatTrackingSerializer.ClearPhase(this, CombatTrackingClearPhase.PlayerTurnStart);
		PlayersAwaitingPlayPhase.Clear();
	}

	public void BeginPlayerTurnStart(IEnumerable<ulong> playerIds)
	{
		PlayersAwaitingPlayPhase.Clear();
		foreach (ulong playerId in playerIds)
		{
			PlayersAwaitingPlayPhase.Add(playerId);
			ResetPlayerRuneProcsThisTurn(playerId);
		}
	}

	private void ResetPlayerRuneProcsThisTurn(ulong playerId)
	{
		string prefix = playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
		foreach (string key in PlayerRuneProcsThisTurn.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
		{
			PlayerRuneProcsThisTurn.Remove(key);
		}
	}

	public void EnterPlayerPlayPhase(ulong playerId)
	{
		PlayersAwaitingPlayPhase.Remove(playerId);
	}

	public bool IsPlayerTurnStart(ulong playerId)
	{
		return PlayersAwaitingPlayPhase.Contains(playerId);
	}

	public void PreparePlayerSideTurnEnd()
	{
		HextechMayhemCombatTrackingSerializer.ClearPhase(this, CombatTrackingClearPhase.PlayerTurnEnd);
	}

	public void PrepareEnemySideTurnStart()
	{
		// 字段按 EnemyTurnStart 标注反射清空。
		PlayersAwaitingPlayPhase.Clear();
		HextechMayhemCombatTrackingSerializer.ClearPhase(this, CombatTrackingClearPhase.EnemyTurnStart);
	}

	// atCombatStart：原版在调用 BeforeCombatStart 钩子之前就把 IsInProgress 置为 true，开战那次重置不算异常。
	public void Reset(bool atCombatStart = false)
	{
		if (!atCombatStart)
		{
			WarnIfCombatInProgress("reset");
		}

		HextechMayhemCombatTrackingSerializer.Clear(this);
	}

	// 战斗追踪只该在战斗开始/结束与读档时整体清空或还原。战斗进行中被整体改写会让这一端的敌方海克斯
	// 计数与另一端分叉，留下调用栈方便从玩家日志定位来源。
	internal static void WarnIfCombatInProgress(string operation)
	{
		if (CombatManager.Instance?.IsInProgress != true)
		{
			return;
		}

		string stack = string.Join(" <- ", new System.Diagnostics.StackTrace(2, false).GetFrames()
			.Select(static frame => frame.GetMethod())
			.Where(static method => method != null)
			.Take(8)
			.Select(static method => $"{method!.DeclaringType?.Name}.{method.Name}"));
		HextechLog.Warn("CombatTracking", $"Combat tracking {operation} while combat is in progress: {stack}");
	}
}
