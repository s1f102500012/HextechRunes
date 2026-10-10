using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace HextechRunes;

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private const int DismissMouseReleaseWaitLimit = 30;

	public void CompleteEnemyOnlySelection()
	{
		if (!_enemyOnly || _choiceLocked)
		{
			return;
		}

		_choiceLocked = true;
		if (_enemyOnlyConfirm != null)
		{
			_enemyOnlyConfirm.Disabled = true;
		}

		foreach (Button button in _enemyHexRerollButtons.Concat(_enemyHexRemoveButtons))
		{
			button.Disabled = true;
		}

		_completionSource.TrySetResult([]);
	}

	private void OnHolderSelected(RelicModel relic)
	{
		OnHolderSelected(relic, -1);
	}

	private void OnHolderSelected(RelicModel relic, int slotIndex)
	{
		if (_choiceLocked)
		{
			return;
		}

		if (IsSelectionConfirmGuardActive())
		{
			UpdatePendingPlayerRuneVisuals();
			HextechLog.Info("Mayhem", $"SelectionScreen.OnHolderSelected: ignored early selection relic={relic.CanonicalId().Entry}");
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (UsesPlayerRuneConfirmation)
		{
			_pendingPlayerRuneSlot = ResolvePendingPlayerRuneSlot(slotIndex, _relics.Count);
			if (!_pendingPlayerRuneSlot.HasValue)
			{
				return;
			}

			UpdatePlayerRuneActionButtons();
			GetViewport()?.SetInputAsHandled();
			return;
		}

		CompleteHolderSelection(relic);
	}

	private void CompleteHolderSelection(RelicModel relic)
	{
		if (_choiceLocked)
		{
			return;
		}

		_choiceLocked = true;
		_pendingPlayerRuneSlot = null;
		_selectedSlotIndex = _relics.FindIndex(candidate => ReferenceEquals(candidate, relic));
		UpdatePlayerRuneActionButtons();
		for (int i = 0; i < _holders.Count; i++)
		{
			// 选中的卡不进禁用样式、保留选中描边，界面关闭时以选中态渐隐；只是不再接收输入。
			if (i == _selectedSlotIndex)
			{
				_holders[i].MouseFilter = MouseFilterEnum.Ignore;
				_holders[i].FocusMode = FocusModeEnum.None;
				if (i < _pendingSelectionOutlines.Count)
				{
					_pendingSelectionOutlines[i].Visible = true;
				}
				continue;
			}

			_holders[i].Disabled = true;
		}
		foreach (Button rerollButton in _rerollButtons)
		{
			rerollButton.Disabled = true;
		}
		foreach (HextechGoldenRerollVisual visual in _goldenRerollVisuals)
		{
			visual.SetVisualState(active: false, hovered: false, disabled: true);
		}
		LockSelfPickControls();
		PlaySelectionVfx();

		HextechLog.Info("Mayhem", $"SelectionScreen.OnHolderSelected: relic={relic.CanonicalId().Entry}");
		PlayRuneSelectSfx(relic);
		GetViewport()?.SetInputAsHandled();
		_completionSource.TrySetResult([relic]);
	}

	private void OnPlayerRuneConfirmPressed()
	{
		if (_choiceLocked || !UsesPlayerRuneConfirmation)
		{
			return;
		}

		if (IsSelectionConfirmGuardActive())
		{
			GetViewport()?.SetInputAsHandled();
			return;
		}

		if (_pendingPlayerRuneSlot is not int pendingSlot
			|| pendingSlot < 0
			|| pendingSlot >= _relics.Count)
		{
			ClearPendingPlayerRuneSelection();
			return;
		}

		GetViewport()?.SetInputAsHandled();
		CompleteHolderSelection(_relics[pendingSlot]);
	}

	private void OnPlayerRuneCancelPressed()
	{
		if (_choiceLocked || !UsesPlayerRuneConfirmation)
		{
			return;
		}

		if (_pendingPlayerRuneSlot is not int pendingSlot)
		{
			return;
		}

		ClearPendingPlayerRuneSelection();
		GetViewport()?.SetInputAsHandled();
		RestoreFocusDeferred(GetHolderForSlot(pendingSlot));
	}

	private void ClearPendingPlayerRuneSelection()
	{
		_pendingPlayerRuneSlot = null;
		UpdatePlayerRuneActionButtons();
	}

	private void UpdatePlayerRuneActionButtons()
	{
		UpdatePendingPlayerRuneVisuals();
		if (_playerRuneConfirm != null)
		{
			_playerRuneConfirm.Disabled = _choiceLocked || !_pendingPlayerRuneSlot.HasValue;
		}
		if (_playerRuneCancel != null)
		{
			_playerRuneCancel.Disabled = _choiceLocked || !_pendingPlayerRuneSlot.HasValue;
		}
		if (IsInsideTree())
		{
			ConfigureControllerNavigation();
		}
	}

	private void UpdatePendingPlayerRuneVisuals()
	{
		if (!UsesPlayerRuneConfirmation)
		{
			return;
		}

		for (int i = 0; i < _pendingSelectionOutlines.Count; i++)
		{
			_pendingSelectionOutlines[i].Visible = _pendingPlayerRuneSlot == i;
		}
	}

	private void EnsureSelectionConfirmGuardStarted()
	{
		if (_selectionConfirmGuardStarted)
		{
			return;
		}

		RestartSelectionConfirmGuard();
	}

	private void RestartSelectionConfirmGuard()
	{
		_selectionConfirmGuardStarted = true;
		_selectionConfirmGuardEndsAtMsec = Time.GetTicksMsec() + SelectionConfirmGuardDurationMsec;
	}

	private bool IsSelectionConfirmGuardActive()
	{
		EnsureSelectionConfirmGuardStarted();
		return Time.GetTicksMsec() < _selectionConfirmGuardEndsAtMsec;
	}

	private void OnRerollPressed(int slotIndex)
	{
		if (_choiceLocked || _rerollFunc == null || IsPlayerRuneRerollLimitReached(slotIndex))
		{
			return;
		}

		bool restoreControllerFocus = slotIndex >= 0
			&& slotIndex < _rerollButtons.Count
			&& _rerollButtons[slotIndex].HasFocus();
		bool goldenRerollWasActive = _goldenRerollSession?.IsActive == true;
		IReadOnlyList<RelicModel> rerolled = _rerollFunc(_relics, slotIndex, _rerollHistory.Count);
		if (rerolled.Count != _relics.Count)
		{
			return;
		}

		string oldRelic = _relics[slotIndex].CanonicalId().Entry;
		string newRelic = rerolled[slotIndex].CanonicalId().Entry;
		if (HextechSelectionHelpers.SameRuneCandidate(_relics[slotIndex], rerolled[slotIndex]))
		{
			return;
		}

		HextechLog.Info("Mayhem", $"SelectionScreen.OnRerollPressed: slot={slotIndex} old={oldRelic} new={newRelic}");
		PlayRerollSfx();
		_relics = HextechWeightedRuneOptions.Copy(rerolled);
		_playerRuneRerollCounts[slotIndex]++;
		_rerollHistory.Add(slotIndex);
		_pendingPlayerRuneSlot = ResolvePendingSlotAfterReroll(_pendingPlayerRuneSlot, slotIndex);
		if (goldenRerollWasActive && _goldenRerollSession is { } goldenReroll)
		{
			goldenReroll.Consume();
			HextechLog.Info(
				"Mayhem", $"SelectionScreen.OnRerollPressed: golden reroll consumed " +
				$"slot={slotIndex} upgraded={goldenReroll.UpgradedRarity}");
		}
		// RebuildCards 会在当前输入事件内销毁并重建按钮。重新开启确认保护，避免鼠标、
		// 手柄确认键或键盘重复输入落到新生成的卡片上，表现为“刷新后直接跳过”。
		RestartSelectionConfirmGuard();
		List<CardVfxAttachment> keptVfx = DetachCardVfx(slotIndex);
		RebuildCards();
		ReattachCardVfx(keptVfx);
		PlayRerollVfx(slotIndex, goldenRerollWasActive);
		if (restoreControllerFocus)
		{
			RestorePlayerRerollFocus(slotIndex);
		}
	}

	internal bool ActivateGoldenRerollForDebug()
	{
		if (_choiceLocked || _goldenRerollSession?.ActivateForDebug() != true)
		{
			return false;
		}

		for (int i = 0; i < _goldenRerollVisuals.Count; i++)
		{
			bool disabled = i >= _rerollButtons.Count || _rerollButtons[i].Disabled;
			_goldenRerollVisuals[i].SetVisualState(
				active: true,
				hovered: false,
				disabled);
		}

		HextechLog.Info("Mayhem", $"SelectionScreen: golden reroll forced by console");
		return true;
	}

	private void OnEnemyHexRerollPressed(int slotIndex)
	{
		if (_choiceLocked || _enemyHexRerollFunc == null || slotIndex < 0 || slotIndex >= _monsterHexKinds.Count || IsEnemyHexRerollLimitReached(slotIndex))
		{
			return;
		}

		bool restoreControllerFocus = slotIndex < _enemyHexRerollButtons.Count
			&& _enemyHexRerollButtons[slotIndex].HasFocus();
		MonsterHexKind? currentHex = _monsterHexKinds[slotIndex];
		if (!currentHex.HasValue)
		{
			return;
		}

		MonsterHexKind? rerolled = _enemyHexRerollFunc(_monsterHexKinds.ToArray(), slotIndex, _enemyHexRerollCounts[slotIndex]);
		if (rerolled == null || rerolled == currentHex)
		{
			return;
		}

		PlayRerollSfx();
		_monsterHexKinds[slotIndex] = rerolled;
		_enemyHexRerollCounts[slotIndex]++;
		HextechLog.Info("Mayhem", $"SelectionScreen.OnEnemyHexRerollPressed: slot={slotIndex} hex={rerolled} count={_enemyHexRerollCounts[slotIndex]}");
		NotifyEnemyHexChanged();
		RebuildEnemyPreview();
		if (restoreControllerFocus)
		{
			RestoreEnemyRerollFocus(slotIndex);
		}
	}

	private void OnEnemyHexRemovePressed(int slotIndex)
	{
		if (_choiceLocked || slotIndex < 0 || slotIndex >= _monsterHexKinds.Count)
		{
			return;
		}

		bool restoreControllerFocus = slotIndex < _enemyHexRemoveButtons.Count
			&& _enemyHexRemoveButtons[slotIndex].HasFocus();
		bool wasRemoved = !_monsterHexKinds[slotIndex].HasValue;
		MonsterHexKind? previous = wasRemoved
			? GetMonsterHexBeforeRemovalSlot(slotIndex)
			: _monsterHexKinds[slotIndex];
		if (!ToggleEnemyHexRemoval(_monsterHexKinds, _monsterHexBeforeRemoval, slotIndex))
		{
			return;
		}

		PlayButtonClickSfx();
		if (wasRemoved)
		{
			HextechLog.Info("Mayhem", $"SelectionScreen.OnEnemyHexRemovePressed: undo slot={slotIndex} hex={previous}");
		}
		else
		{
			HextechLog.Info("Mayhem", $"SelectionScreen.OnEnemyHexRemovePressed: remove slot={slotIndex} previous={previous}");
		}

		NotifyEnemyHexChanged();
		RebuildEnemyPreview();
		if (restoreControllerFocus)
		{
			RestoreEnemyRemoveFocus(slotIndex);
		}
	}

	internal static bool ToggleEnemyHexRemoval(
		IList<MonsterHexKind?> monsterHexes,
		IList<MonsterHexKind?> monsterHexesBeforeRemoval,
		int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= monsterHexes.Count || slotIndex >= monsterHexesBeforeRemoval.Count)
		{
			return false;
		}

		if (monsterHexes[slotIndex].HasValue)
		{
			monsterHexesBeforeRemoval[slotIndex] = monsterHexes[slotIndex];
			monsterHexes[slotIndex] = null;
			return true;
		}

		if (!monsterHexesBeforeRemoval[slotIndex].HasValue)
		{
			return false;
		}

		monsterHexes[slotIndex] = monsterHexesBeforeRemoval[slotIndex];
		monsterHexesBeforeRemoval[slotIndex] = null;
		return true;
	}

	public void ApplyEnemyHexAdjustment(IReadOnlyList<MonsterHexKind?> monsterHexes, IReadOnlyList<int> rerollCounts)
	{
		_monsterHexKinds.Clear();
		_monsterHexBeforeRemoval.Clear();
		_enemyHexRerollCounts.Clear();
		for (int i = 0; i < monsterHexes.Count; i++)
		{
			_monsterHexKinds.Add(monsterHexes[i]);
			_monsterHexBeforeRemoval.Add(null);
			_enemyHexRerollCounts.Add(i < rerollCounts.Count ? rerollCounts[i] : 0);
		}

		HextechLog.Info("Mayhem", $"SelectionScreen.ApplyEnemyHexAdjustment: slots={string.Join(",", _monsterHexKinds.Select(static hex => hex?.ToString() ?? "None"))} rerolls={string.Join(",", _enemyHexRerollCounts)}");
		RebuildEnemyPreview();
	}

	private void NotifyEnemyHexChanged()
	{
		_enemyHexChanged?.Invoke(_monsterHexKinds.ToArray(), _enemyHexRerollCounts.ToArray());
	}

	private bool IsPlayerRuneRerollLimitReached(int slotIndex)
	{
		return IsRerollLimitReached(_playerRuneRerollLimit, GetPlayerRuneRerollCount(slotIndex));
	}

	private int GetPlayerRuneRerollCount(int slotIndex)
	{
		return slotIndex >= 0 && slotIndex < _playerRuneRerollCounts.Count
			? _playerRuneRerollCounts[slotIndex]
			: 0;
	}

	private bool IsEnemyHexRerollLimitReached(int slotIndex)
	{
		int count = slotIndex >= 0 && slotIndex < _enemyHexRerollCounts.Count
			? _enemyHexRerollCounts[slotIndex]
			: 0;
		return IsRerollLimitReached(_enemyHexRerollLimit, count);
	}

	private static bool IsRerollLimitReached(int limit, int count)
	{
		return limit != HextechRuneConfiguration.InfiniteRerollLimit && count >= limit;
	}

	public async Task<IEnumerable<RelicModel>> RelicsSelected(bool removeOverlay = true)
	{
		IEnumerable<RelicModel> result = await _completionSource.Task;
		HextechLog.Info("Mayhem", $"SelectionScreen.RelicsSelected: begin dismiss mousePressed={Input.IsMouseButtonPressed(MouseButton.Left)}");
		await WaitForMouseReleaseAsync();
		if (!removeOverlay)
		{
			_blockMapUntilDismissed = true;
			ShowWaitingForRemotePlayers();
			HextechLog.Info("Mayhem", $"SelectionScreen.RelicsSelected: keeping overlay until multiplayer sync completes");
			return result;
		}

		HextechLog.Info("Mayhem", $"SelectionScreen.RelicsSelected: removing overlay");
		NOverlayStack.Instance?.Remove(this);
		return result;
	}

	public async Task DismissAfterSelectionComplete()
	{
		if (!IsInsideTree())
		{
			return;
		}

		bool mouseReleased = await WaitForMouseReleaseAsync(DismissMouseReleaseWaitLimit);
		if (!mouseReleased)
		{
			HextechLog.Warn("Mayhem", $"SelectionScreen.DismissAfterSelectionComplete: mouse release wait reached its limit; forcing overlay removal.");
		}
		HextechLog.Info("Mayhem", $"SelectionScreen.DismissAfterSelectionComplete: removing overlay");
		_blockMapUntilDismissed = false;
		NOverlayStack.Instance?.Remove(this);
	}

	private async Task<bool> WaitForMouseReleaseAsync(
		int pressedWaitLimit = int.MaxValue,
		CancellationToken cancellationToken = default)
	{
		if (!await AwaitProcessFrameIfInsideTreeAsync(cancellationToken))
		{
			return true;
		}

		int pressedWaitCount = 0;
		while (Input.IsMouseButtonPressed(MouseButton.Left))
		{
			if (pressedWaitCount >= pressedWaitLimit)
			{
				return false;
			}

			pressedWaitCount++;
			if (!await AwaitProcessFrameIfInsideTreeAsync(cancellationToken))
			{
				return true;
			}
		}

		await AwaitProcessFrameIfInsideTreeAsync(cancellationToken);
		return true;
	}

	private async Task<bool> AwaitProcessFrameIfInsideTreeAsync(CancellationToken cancellationToken = default)
	{
		if (!IsInsideTree())
		{
			return false;
		}

		await HextechSelectionHelpers.WaitForProcessFrameOrDelayAsync(cancellationToken);
		return IsInsideTree();
	}

	private void ShowWaitingForRemotePlayers()
	{
		if (_statusLabel == null)
		{
			return;
		}

		_statusLabel.SetTextAutoSize(new LocString(LocTable, "HEXTECH_WAITING_FOR_PLAYERS").GetRawText());
		_statusLabel.Visible = true;
	}
}
