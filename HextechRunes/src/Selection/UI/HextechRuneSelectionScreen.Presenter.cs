using Godot;

namespace HextechRunes;

/// <summary>
/// 外部表现层（<see cref="IHextechSelectionPresenter"/>）的桥接。接管时本界面不构建 UI，只保留选择状态；
/// <see cref="HextechSelectionSession"/> 的操作都转到原有按钮的处理方法上。
/// </summary>
internal sealed partial class HextechRuneSelectionScreen
{
	private HextechSelectionSession? _presenterSession;

	internal bool IsExternallyPresented => _presenterSession != null;

	internal event Action? PresenterEnemyHexesChanged;

	/// <summary>构造时调用：有登记的表现层且它愿意接管时返回 true，此时不要 BuildUi。</summary>
	private bool TryStartExternalPresentation()
	{
		IHextechSelectionPresenter? presenter = HextechSelectionPresentation.Presenter;
		if (presenter == null || _continueOnly || SelfPickMode || _metadataMode != HextechSelectionMetadataMode.PlayerRune)
		{
			return false;
		}

		var session = new HextechSelectionSession(this);
		bool accepted = false;
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.ShouldPresent failed", () => accepted = presenter.ShouldPresent(session));
		if (!accepted)
		{
			return false;
		}

		_presenterSession = session;
		_presenterBaseRarity = ComputePresenterRarity();
		Ready += () => HextechPresentation.TryRun("Mayhem", "SelectionScreen.Present failed", () => presenter.Present(session));
		HextechLog.Info("Mayhem", $"SelectionScreen: presented by {presenter.GetType().FullName}");
		return true;
	}

	internal IReadOnlyList<RelicModel> PresenterOptions => _relics;

	internal bool PresenterLocked => _choiceLocked;

	internal bool PresenterEnemyOnly => _enemyOnly;

	internal bool PresenterEnemyControls => _enemyHexControlsEnabled;

	internal int PresenterEnemyHexCount => _monsterHexKinds.Count;

	private HextechRarityTier _presenterBaseRarity;

	private Control? _presenterFocusRoot;

	private Control? _presenterDefaultFocus;

	// 本次选择的品级在开选时确定；金色重随只改变被重随的那张候选的品级。
	internal HextechRarityTier PresenterRarity => _presenterBaseRarity;

	private HextechRarityTier ComputePresenterRarity() => _relics.Count > 0
		? HextechRunePoolBuilder.GetRarityForOptions(_relics)
		: _monsterHexKinds.FirstOrDefault(static k => k.HasValue) is MonsterHexKind hex
			? MonsterHexCatalog.GetMonsterHexRarity(hex)
			: HextechRarityTier.Silver;

	internal HextechRarityTier? PresenterOptionRarity(int slot) =>
		slot >= 0 && slot < _relics.Count ? HextechRunePoolBuilder.GetRarityForOptions([_relics[slot]]) : null;

	internal bool PresenterGoldenRerollActive => _goldenRerollSession?.IsActive == true;

	internal HextechRarityTier? PresenterGoldenRerollRarity =>
		_goldenRerollSession?.CanActivate == true ? _goldenRerollSession.UpgradedRarity : null;

	internal void PresenterSetControllerFocus(Control? root, Control? defaultFocus)
	{
		_presenterFocusRoot = root;
		_presenterDefaultFocus = defaultFocus;
		TryGrabOverlayFocus();
	}

	/// <summary>手柄焦点是否已经在表现层的节点里（接管时本界面不应把焦点抢回来）。</summary>
	private bool IsFocusInsidePresenter(Control focused) =>
		_presenterFocusRoot != null && GodotObject.IsInstanceValid(_presenterFocusRoot)
		&& (focused == _presenterFocusRoot || _presenterFocusRoot.IsAncestorOf(focused));

	private Control? PresenterDefaultFocus =>
		_presenterDefaultFocus != null && GodotObject.IsInstanceValid(_presenterDefaultFocus) && _presenterDefaultFocus.IsVisibleInTree()
			? _presenterDefaultFocus
			: null;

	internal bool PresenterCanRerollOption(int slot) =>
		!_choiceLocked && _rerollFunc != null && slot >= 0 && slot < _relics.Count && !IsPlayerRuneRerollLimitReached(slot);

	internal RelicModel? PresenterRerollOption(int slot)
	{
		if (!PresenterCanRerollOption(slot))
		{
			return null;
		}

		// 不构建 UI 时不会经过 RebuildCards，重随计数需要在这里补齐长度。
		while (_playerRuneRerollCounts.Count < _relics.Count)
		{
			_playerRuneRerollCounts.Add(0);
		}

		RelicModel before = _relics[slot];
		OnRerollPressed(slot);
		return ReferenceEquals(before, _relics[slot]) ? null : _relics[slot];
	}

	internal string? PresenterOptionFooter(int slot) =>
		slot >= 0 && slot < _relics.Count
		&& _relics[slot] is IHextechSelectionFooterProvider provider
		&& provider.GetSelectionFooterText() is { Length: > 0 } footer
			? footer
			: null;

	internal void PresenterChoose(int slot)
	{
		if (slot >= 0 && slot < _relics.Count)
		{
			CompleteHolderSelection(_relics[slot]);
		}
	}

	internal void PresenterConfirmEnemyOnly() => CompleteEnemyOnlySelection();

	internal HextechEnemyHexInfo? PresenterEnemyHex(int slot)
	{
		if (slot < 0 || slot >= _monsterHexKinds.Count)
		{
			return null;
		}

		MonsterHexKind? shown = _monsterHexKinds[slot] ?? GetMonsterHexBeforeRemovalSlot(slot);
		return shown is MonsterHexKind hex ? CreateEnemyHexInfo(hex, removed: !_monsterHexKinds[slot].HasValue) : null;
	}

	internal bool PresenterCanRerollEnemyHex(int slot) =>
		!_choiceLocked && _enemyHexRerollFunc != null && slot >= 0 && slot < _monsterHexKinds.Count
		&& _monsterHexKinds[slot].HasValue && !IsEnemyHexRerollLimitReached(slot);

	internal HextechEnemyHexInfo? PresenterRerollEnemyHex(int slot)
	{
		if (!PresenterCanRerollEnemyHex(slot))
		{
			return null;
		}

		MonsterHexKind? before = _monsterHexKinds[slot];
		OnEnemyHexRerollPressed(slot);
		return _monsterHexKinds[slot] is MonsterHexKind after && after != before ? CreateEnemyHexInfo(after, removed: false) : null;
	}

	internal bool PresenterToggleEnemyHex(int slot)
	{
		if (_choiceLocked || !_enemyHexControlsEnabled || slot < 0 || slot >= _monsterHexKinds.Count)
		{
			return false;
		}

		bool before = _monsterHexKinds[slot].HasValue;
		OnEnemyHexRemovePressed(slot);
		return before != _monsterHexKinds[slot].HasValue;
	}

	private void NotifyPresenterEnemyHexesChanged()
	{
		if (_presenterSession != null)
		{
			PresenterEnemyHexesChanged?.Invoke();
		}
	}

	private static HextechEnemyHexInfo CreateEnemyHexInfo(MonsterHexKind hex, bool removed)
	{
		RelicModel icon = MonsterHexCatalog.GetIconRelicForMonsterHex(hex);
		return new HextechEnemyHexInfo(
			hex.ToString(),
			icon.Title.GetFormattedText(),
			MonsterHexCatalog.GetEnemyHexDescriptionFormatted(hex),
			icon.Icon,
			MonsterHexCatalog.GetMonsterHexRarity(hex),
			MonsterHexCatalog.GetEnemyHexHoverTips(hex).Skip(1).ToList(),
			removed);
	}
}

/// <summary>登记的外部表现层（最多一个）。</summary>
internal static class HextechSelectionPresentation
{
	internal static IHextechSelectionPresenter? Presenter { get; private set; }

	internal static bool KeepsMapOpen => Presenter?.KeepsMapOpen == true;

	internal static void Register(IHextechSelectionPresenter presenter)
	{
		ArgumentNullException.ThrowIfNull(presenter);
		if (Presenter != null && !ReferenceEquals(Presenter, presenter))
		{
			HextechLog.Warn("ExternalContent", $"Selection presenter already registered ({Presenter.GetType().FullName}); ignoring {presenter.GetType().FullName}.");
			return;
		}

		Presenter = presenter;
	}
}
