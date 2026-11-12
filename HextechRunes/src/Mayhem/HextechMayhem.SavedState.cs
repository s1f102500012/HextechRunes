using MegaCrit.Sts2.Core.Localization;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	// 本局全部 Modifier 状态都在 _runContext；下面是各分部常用的转发，SavedProperty 经它们读写。
	private readonly HextechMayhemRunContext _runContext = new();

	private HextechMayhemActState ActState => _runContext.ActState;

	private HextechMayhemChoiceHistoryState ChoiceHistory => _runContext.ChoiceHistory;

	private HextechActiveMonsterHexCache ActiveMonsterHexCache => _runContext.ActiveMonsterHexCache;

	private HextechHexCountState PlayerHexCounts => _runContext.PlayerHexCounts;

	private HextechHexCountState EnemyHexCounts => _runContext.EnemyHexCounts;

	private int HexCountRecoveryBaseline
	{
		get => _runContext.HexCountRecoveryBaseline;
		set => _runContext.HexCountRecoveryBaseline = value;
	}

	private int MonsterHexStrengthTierFloor
	{
		get => _runContext.MonsterHexStrengthTierFloor;
		set => _runContext.MonsterHexStrengthTierFloor = value;
	}

	private int EnemyTezcatarasMercyCombatCounter
	{
		get => _runContext.EnemyTezcatarasMercyCombatCounter;
		set => _runContext.EnemyTezcatarasMercyCombatCounter = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedRarityByAct
	{
		get => ActState.SavedRarityByAct;
		set => ActState.SavedRarityByAct = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[]? SavedMonsterHexByAct
	{
		get => ActState.SavedMonsterHexByAct;
		set => ActState.SavedMonsterHexByAct = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedMonsterHexesByActJson
	{
		get => ActState.SavedMonsterHexesByActJson;
		set => ActState.SavedMonsterHexesByActJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedCarriedMonsterHexes
	{
		get => ActState.SavedCarriedMonsterHexes;
		set => ActState.SavedCarriedMonsterHexes = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedResolvedActs
	{
		get => ActState.SavedResolvedActs;
		set => ActState.SavedResolvedActs = value;
	}

	// 地图缩短已不再生效，只为旧存档兼容保留名称与类型。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedMapLengthReducedActs
	{
		get => ActState.SavedMapLengthReducedActs;
		set => ActState.SavedMapLengthReducedActs = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedExtraStageIndexesJson
	{
		get => ActState.SavedExtraStageIndexesJson;
		set => ActState.SavedExtraStageIndexesJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedActSelectionIndexOffset
	{
		get => _runContext.ActSelectionIndexOffset;
		set => _runContext.ActSelectionIndexOffset = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedPlayerHexCountsByAct
	{
		get => PlayerHexCounts.Snapshot;
		set => PlayerHexCounts.Set(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedEnemyHexCountsByAct
	{
		get => EnemyHexCounts.Snapshot;
		set => EnemyHexCounts.Set(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedHextechRunConfigurationSnapshotJson
	{
		get => SerializeRunConfigurationSnapshot();
		set => RestoreRunConfigurationSnapshot(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedPlayerRuneConfigDisabledIdsJson
	{
		get => SerializePlayerRuneConfigDisabledIds();
		set => RestorePlayerRuneConfigDisabledIds(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedTelemetryChoicesJson
	{
		get => ChoiceHistory.SavedTelemetryChoicesJson;
		set => ChoiceHistory.SavedTelemetryChoicesJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedSeenPlayerRuneIdsJson
	{
		get => ChoiceHistory.SavedSeenPlayerRuneIdsJson;
		set => ChoiceHistory.SavedSeenPlayerRuneIdsJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedRuneSelectionJournalJson
	{
		get => _runContext.RuneSelectionJournal.Serialize();
		set => _runContext.RuneSelectionJournal.Restore(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedHexCountRecoveryBaseline
	{
		get => HexCountRecoveryBaseline;
		set => HexCountRecoveryBaseline = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedMonsterHexStrengthTierFloor
	{
		get => MonsterHexStrengthTierFloor;
		set => MonsterHexStrengthTierFloor = Math.Clamp(value, 0, 3);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedCombatTrackingJson
	{
		get => CombatTracking.Serialize();
		set => CombatTracking.Restore(value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedEnemyTezcatarasMercyCombatCounter
	{
		get => EnemyTezcatarasMercyCombatCounter;
		set => EnemyTezcatarasMercyCombatCounter = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedHextechHostUsesBetterMultiplayerScaling
	{
		get => HostUsesBetterMultiplayerScaling;
		set => HostUsesBetterMultiplayerScaling = value;
	}

	// 模组总开关的本局冻结值。空字符串表示未冻结，按本局有效配置判断；"True"/"False" 表示已冻结。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedModActiveForRun
	{
		get => _runContext.ModActiveForRun?.ToString() ?? string.Empty;
		set => _runContext.ModActiveForRun = string.IsNullOrEmpty(value)
			? null
			: (bool.TryParse(value, out bool parsed) ? parsed : null);
	}

	public override LocString Title => new("modifiers", "HEXTECH_MAYHEM.title");

	public override LocString Description => new("modifiers", "HEXTECH_MAYHEM.description");

	protected override string IconPath => $"res://{ModInfo.Id}/images/relics/prismaticForge.png";

	public override IEnumerable<IHoverTip> HoverTips => [];

	public RunState ActiveRunState => RunState;

	internal bool IsEndlessLoopActive => _runContext.IsEndlessLoopActive;

	internal bool HostUsesBetterMultiplayerScaling
	{
		get => _runContext.HostUsesBetterMultiplayerScaling;
		set => _runContext.HostUsesBetterMultiplayerScaling = value;
	}
}
