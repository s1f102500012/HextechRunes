using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

internal enum HextechSelectionMetadataMode
{
	PlayerRune,
	Forge
}

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private readonly TaskCompletionSource<IEnumerable<RelicModel>> _completionSource = new();
	private readonly Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? _rerollFunc;
	private readonly Func<IReadOnlyList<MonsterHexKind?>, int, int, MonsterHexKind?>? _enemyHexRerollFunc;
	private readonly Action<IReadOnlyList<MonsterHexKind?>, IReadOnlyList<int>>? _enemyHexChanged;
	private readonly int _playerRuneRerollLimit;
	private readonly int _enemyHexRerollLimit;
	private readonly string? _titleOverride;
	private readonly HextechSelectionMetadataMode _metadataMode;
	private readonly HextechGoldenRerollSession? _goldenRerollSession;
	private List<RelicModel> _relics;
	private readonly List<MonsterHexKind?> _monsterHexKinds = [];
	private readonly List<MonsterHexKind?> _monsterHexBeforeRemoval = [];
	private readonly List<int> _enemyHexRerollCounts = [];
	private readonly string _rarityKey;
	private readonly List<Button> _holders = new();
	private readonly List<Button> _rerollButtons = new();
	private readonly List<Button> _enemyHexRerollButtons = new();
	private readonly List<Button> _enemyHexRemoveButtons = new();
	private readonly List<HextechGoldenRerollVisual> _goldenRerollVisuals = new();
	private readonly List<int> _playerRuneRerollCounts = new();
	private readonly List<int> _rerollHistory = new();
	private readonly bool _enemyHexControlsEnabled;
	private readonly bool _enemyOnly;
	// 本稀有度已无任何可选海克斯:只显示说明和"继续"按钮,确认后以空结果完成,不发放遗物。
	private readonly bool _continueOnly;
	private Button? _enemyOnlyConfirm;
	private Button? _playerRuneConfirm;
	private Button? _playerRuneCancel;
	public bool EnemyOnlySelectionConfirmed => _enemyOnly && _choiceLocked;
	private HBoxContainer? _cardsRow;
	private VBoxContainer? _enemyPreviewHost;
	private MegaLabel? _statusLabel;
	private bool _choiceLocked;
	private int? _pendingPlayerRuneSlot;
	// 打开界面时读一次,界面存续期间不随配置菜单变化,避免已建好的按钮与行为不一致。
	private readonly bool _confirmRuneSelectionPreference;
	private bool _blockMapUntilDismissed;
	private bool _closed;
	private bool _selectionConfirmGuardStarted;
	private ulong _selectionConfirmGuardEndsAtMsec;

	public NetScreenType ScreenType => NetScreenType.Rewards;

	// 外部表现层接管时由它自己负责背景。
	public bool UseSharedBackstop => !IsExternallyPresented;

	// 只在游戏处于手柄/纯键盘方向导航时给默认焦点:原版切进手柄模式与界面切换时都会聚焦它;鼠标玩家返回 null,不出焦点框。
	public Control? DefaultFocusedControl => HextechControllerInput.IsDirectionalNavigation
		? (IsExternallyPresented
			? PresenterDefaultFocus
			: (_holders.FirstOrDefault(CanReceiveFocus)
				?? _selfPickButtons.FirstOrDefault(CanReceiveFocus)
				?? (_enemyOnlyConfirm != null && CanReceiveFocus(_enemyOnlyConfirm) ? _enemyOnlyConfirm : null)))
		: null;

	public bool RequestedReroll => false;

	public IReadOnlyList<RelicModel> CurrentRelics => _relics;

	public IReadOnlyList<int> RerollHistory => _rerollHistory;

	public IReadOnlyList<MonsterHexKind> CurrentMonsterHexes => _monsterHexKinds
		.OfType<MonsterHexKind>()
		.ToArray();

	public IReadOnlyList<MonsterHexKind?> CurrentMonsterHexSlots => _monsterHexKinds.ToArray();

	public IReadOnlyList<int> EnemyHexRerollCounts => _enemyHexRerollCounts.ToArray();

	private bool UsesPlayerRuneConfirmation => ShouldUsePlayerRuneConfirmation(
		_metadataMode,
		_enemyOnly,
		SelfPickMode,
		_confirmRuneSelectionPreference);

	/// <summary>
	/// 二次确认只在玩家开了本机偏好时用于普通的每幕三选一:锻造器保持点即选,只选敌方海克斯有自己的确认按钮,
	/// 自选模式本身就是"点选 + 确认"。自选的确认走同一个选定入口且不带卡槽,这里若不排除会被当成无效待定而吞掉。
	/// </summary>
	internal static bool ShouldUsePlayerRuneConfirmation(
		HextechSelectionMetadataMode metadataMode,
		bool enemyOnly,
		bool selfPickMode,
		bool preferenceEnabled)
	{
		return preferenceEnabled
			&& metadataMode == HextechSelectionMetadataMode.PlayerRune
			&& !enemyOnly
			&& !selfPickMode;
	}

	internal static int? ResolvePendingPlayerRuneSlot(
		int slotIndex,
		int slotCount)
	{
		return slotIndex >= 0
			&& slotIndex < slotCount
			? slotIndex
			: null;
	}

	internal static int? ResolvePendingSlotAfterReroll(int? pendingSlot, int rerolledSlot)
	{
		return pendingSlot == rerolledSlot ? null : pendingSlot;
	}

	private HextechRuneSelectionScreen(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions,
		int playerRuneRerollLimit,
		string? titleOverride,
		HextechSelectionMetadataMode metadataMode,
		HextechGoldenRerollSession? goldenRerollSession,
		IReadOnlyList<RelicModel>? selfPickPool,
		bool continueOnly)
	{
		_relics = HextechWeightedRuneOptions.Copy(relics);
		_selfPickPool = selfPickPool;
		_continueOnly = continueOnly && relics.Count == 0;
		_rerollFunc = rerollFunc;
		_enemyHexRerollFunc = enemyHexOptions?.RerollFunc;
		_enemyHexChanged = enemyHexOptions?.Changed;
		_playerRuneRerollLimit = HextechRuneConfiguration.ClampRerollLimit(playerRuneRerollLimit);
		_enemyHexRerollLimit = HextechRuneConfiguration.ClampRerollLimit(enemyHexOptions?.RerollLimit ?? HextechRuneConfiguration.GetDefaultMonsterHexRerollLimit());
		_titleOverride = titleOverride;
		_metadataMode = metadataMode;
		_goldenRerollSession = goldenRerollSession;
		_confirmRuneSelectionPreference = HextechUiPreferences.ConfirmRuneSelection;
		_enemyHexControlsEnabled = enemyHexOptions?.ControlsEnabled == true || enemyHexOptions?.RerollFunc != null;
		_enemyOnly = relics.Count == 0 && (enemyHexOptions != null || _continueOnly);
		List<MonsterHexKind> initialMonsterHexes = enemyHexOptions?.InitialHexes?.ToList() ?? [];
		if (initialMonsterHexes.Count == 0 && monsterHexRelic != null && MonsterHexCatalog.TryGetMonsterHexKind(monsterHexRelic, out MonsterHexKind monsterHexKind))
		{
			initialMonsterHexes.Add(monsterHexKind);
		}
		foreach (MonsterHexKind monsterHex in initialMonsterHexes)
		{
			_monsterHexKinds.Add(monsterHex);
			_monsterHexBeforeRemoval.Add(null);
			_enemyHexRerollCounts.Add(0);
		}
		_rarityKey = DetermineRarityKey(relics, metadataMode);
		Name = nameof(HextechRuneSelectionScreen);
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		FocusMode = FocusModeEnum.All;
		FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Enabled;
		Visible = true;
		if (!TryStartExternalPresentation())
		{
			BuildUi();
		}
		if (_goldenRerollSession?.CanActivate == true)
		{
			HextechGoldenRerollDebug.RegisterScreen(this);
		}
	}

	public static HextechRuneSelectionScreen Create(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc = null,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null,
		int playerRuneRerollLimit = 1,
		string? titleOverride = null,
		HextechSelectionMetadataMode metadataMode = HextechSelectionMetadataMode.PlayerRune,
		HextechGoldenRerollSession? goldenRerollSession = null,
		IReadOnlyList<RelicModel>? selfPickPool = null,
		bool continueOnly = false)
	{
		HextechLog.Info("Mayhem", $"SelectionScreen.Create: count={relics.Count} selfPickPool={selfPickPool?.Count ?? 0} continueOnly={continueOnly}");
		return new HextechRuneSelectionScreen(
			relics,
			monsterHexRelic,
			rerollFunc,
			enemyHexOptions,
			playerRuneRerollLimit,
			titleOverride,
			metadataMode,
			goldenRerollSession,
			selfPickPool,
			continueOnly);
	}

	public override void _ExitTree()
	{
		HextechGoldenRerollDebug.UnregisterScreen(this);
		HextechSignalNoise.UnregisterScreen(this);
		EndMapPreview(restoreOverlay: false);
		RestoreMapButtonState();
		_mapPreviewHint?.QueueFree();
		_mapPreviewHint = null;
		// 场景重建、SL 或 overlay 被意外移除时不能把“尚未选择”伪装成空的成功结果。
		// 成功选择会先由 OnHolderSelected 完成 TCS；其余退出统一取消，让本幕保持未解析并可恢复。
		if (!_choiceLocked)
		{
			_completionSource.TrySetCanceled();
		}
		base._ExitTree();
	}

}
