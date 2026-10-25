using System.Text.Json.Serialization;

namespace HextechRunes;

internal sealed class CombatTrackingSnapshot
{
	public Dictionary<uint, int> SlapProcsThisTurn { get; set; } = new();
	public Dictionary<uint, int> TormentorProcsThisTurn { get; set; } = new();
	public Dictionary<uint, int> CourageProcsThisTurn { get; set; } = new();
	public Dictionary<uint, int> BloodPactProcsThisTurn { get; set; } = new();
	public Dictionary<string, int> PlayerRuneProcsThisTurn { get; set; } = new();
	public Dictionary<string, int> PlayerRuneProcsThisCombat { get; set; } = new();
	public Dictionary<string, int> GlobalProcsThisCombat { get; set; } = new();
	public Dictionary<uint, int> BloodArmorHpLossThisPlayerTurn { get; set; } = new();
	public Dictionary<uint, int> ClownCollegeProcsThisTurn { get; set; } = new();
	public List<uint> EscapePlanTriggered { get; set; } = [];
	public List<uint> RepulsorTriggered { get; set; } = [];
	public List<uint> DawnTriggered { get; set; } = [];
	public Dictionary<uint, int> NearDeathFeastEnemyDebt { get; set; } = new();
	public Dictionary<uint, int> NearDeathFeastEnemyStrength { get; set; } = new();
	public List<uint> SpeedDemonPending { get; set; } = [];
	public Dictionary<uint, int> DelayedEnemyHealingBlock { get; set; } = new();
	public List<uint> DevilsDanceTriggeredThisTurn { get; set; } = [];
	public List<uint> FinalFormTriggeredThisTurn { get; set; } = [];
	public List<uint> FeelTheBurnTriggered { get; set; } = [];
	public Dictionary<uint, uint> FeyMagicPendingNoDrawPlayers { get; set; } = new();
	public Dictionary<uint, int> MikaelsBlessingTriggers { get; set; } = new();
	public List<uint> GoliathApplied { get; set; } = [];
	public List<uint> ProtectiveVeilApplied { get; set; } = [];
	public List<uint> MindToMatterApplied { get; set; } = [];
	public List<uint> ScrollOfBitingApplied { get; set; } = [];
	public List<uint> ThornmailApplied { get; set; } = [];
	public List<uint> SuperBrainApplied { get; set; } = [];
	public List<uint> AstralBodyApplied { get; set; } = [];
	public List<uint> MadScientistApplied { get; set; } = [];
	public List<uint> UnmovableMountainApplied { get; set; } = [];
	public List<uint> GoldenSpatulaApplied { get; set; } = [];
	public List<uint> StatsApplied { get; set; } = [];
	public List<uint> StatsOnStatsApplied { get; set; } = [];
	public List<uint> StatsOnStatsOnStatsApplied { get; set; } = [];
	public List<uint> DoormakerRealStartApplied { get; set; } = [];
	public Dictionary<uint, int> TestSubjectPhaseStartApplied { get; set; } = new();
	public Dictionary<uint, int> MonsterMaxHpCoefficientBase { get; set; } = new();
	public Dictionary<uint, int> MonsterMaxHpCoefficientProjected { get; set; } = new();
	public Dictionary<uint, int> TankEngineStacks { get; set; } = new();
	public Dictionary<uint, int> TankEngineLastAppliedRound { get; set; } = new();
	public Dictionary<uint, int> ShrinkEngineStacks { get; set; } = new();
	public List<uint> FeelTheBurnPending { get; set; } = [];
	public List<uint> MountainSoulHasPreviousTurn { get; set; } = [];
	public List<uint> MountainSoulDamagedSinceLastTurn { get; set; } = [];
	public Dictionary<ulong, int> PlayerAttackCardsPlayedThisTurn { get; set; } = new();
	public Dictionary<ulong, int> BackToBasicsCardsPlayedThisTurn { get; set; } = new();
	// 旧存档与旧版本写出的键名,改名只改 C# 标识符。
	[JsonPropertyName("PlayerCardsDrawnThisCombat")]
	public Dictionary<ulong, int> WarmogsSpiritPlayerCardsDrawnThisCombat { get; set; } = new();
	public Dictionary<ulong, int> SwiftAndSafePlayerCardsDrawnThisCombat { get; set; } = new();
	public Dictionary<ulong, int> NightstalkingPlayerCardsDrawnThisCombat { get; set; } = new();
	public List<ulong> MindOverMatterPlayersTriggeredThisTurn { get; set; } = [];
	public Dictionary<uint, int> EnemyPorcupineTemporaryThornsThisTurn { get; set; } = new();
	public Dictionary<uint, int> EnemySpinyToadTemporaryThorns { get; set; } = new();
	public Dictionary<uint, int> EnemyPorcupineUnblockedHitsThisCombat { get; set; } = new();
	public Dictionary<uint, int> EnemyHundredRefinementsUnblockedHitsThisCombat { get; set; } = new();
	public Dictionary<ulong, int> InspectExtraDrawsPreventedThisTurn { get; set; } = new();
	public Dictionary<ulong, int> GripPlayerCardsPlayedThisCombat { get; set; } = new();
}
