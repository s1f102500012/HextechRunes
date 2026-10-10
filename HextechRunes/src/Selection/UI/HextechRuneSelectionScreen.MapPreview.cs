using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private bool _restoreAfterMapReopenQueued;
	private bool _mapPreviewActive;
	private bool _mapButtonForceEnabled;
	private MegaLabel? _mapPreviewHint;

	public void AfterOverlayOpened()
	{
		HextechLog.Info("Mayhem", $"SelectionScreen.AfterOverlayOpened");
		EnsureSelectionConfirmGuardStarted();
		EnsureMapButtonEnabled();
		Modulate = Colors.White;
		Visible = true;
		PlayCardFlashInVfx();
		TryGrabOverlayFocus();
	}

	// 后续幕的海克斯选择发生在刚进新幕、尚未进房间时，顶栏地图键可能被禁用。
	// 选择期间临时启用只读地图预览，选择结束后还原。
	private void EnsureMapButtonEnabled()
	{
		if (_closed)
		{
			return;
		}

		NTopBarMapButton? mapButton = GetTopBarMapButton();
		if (mapButton == null || mapButton.IsEnabled)
		{
			return;
		}

		_mapButtonForceEnabled = true;
		mapButton.Enable();
		HextechLog.Info("Mayhem", $"SelectionScreen: temporarily enabled top bar map button for selection map preview");
	}

	private void RestoreMapButtonState()
	{
		if (!_mapButtonForceEnabled)
		{
			return;
		}

		_mapButtonForceEnabled = false;
		GetTopBarMapButton()?.Disable();
	}

	private static NTopBarMapButton? GetTopBarMapButton()
	{
		return NRun.Instance?.GlobalUi?.TopBar?.Map;
	}

	public void AfterOverlayClosed()
	{
		if (_closed)
		{
			return;
		}

		_closed = true;
		_blockMapUntilDismissed = false;
		HextechLog.Info("Mayhem", $"SelectionScreen.AfterOverlayClosed");
		if (!_choiceLocked)
		{
			HextechLog.Info("Mayhem", $"SelectionScreen.AfterOverlayClosed: cancelling unresolved selection");
			_completionSource.TrySetCanceled();
		}

		if (!TryPlaySelectedCardFadeOut())
		{
			QueueFree();
		}
	}

	public void AfterOverlayShown()
	{
		EnsureSelectionConfirmGuardStarted();
		// 地图已关、overlay 已被重新显示：清理预览态（恢复行进/移除提示），不重复 ShowOverlays。
		EndMapPreview(restoreOverlay: false);
		EnsureMapButtonEnabled();
		Visible = true;
		TryGrabOverlayFocus();
	}

	public void AfterOverlayHidden()
	{
		if (_closed)
		{
			return;
		}

		HextechLog.Info("Mayhem", $"SelectionScreen.AfterOverlayHidden: choiceLocked={_choiceLocked} capstoneOpen={NCapstoneContainer.Instance?.InUse == true} mapOpen={NMapScreen.Instance?.IsOpen == true}");
		Visible = false;

		// 选择尚未完成、且玩家打开了地图 → 进入只读地图预览：禁止行进、显示提示，保留地图打开。
		// 不强制关闭地图、不触碰选择的 TaskCompletionSource，所以查看地图绝不会跳过海克斯选择。
		if (!_choiceLocked && !_blockMapUntilDismissed && IsInsideTree() && NMapScreen.Instance?.IsOpen == true)
		{
			BeginMapPreview();
			return;
		}

		if ((!_choiceLocked || _blockMapUntilDismissed) && !_restoreAfterMapReopenQueued && IsInsideTree())
		{
			_restoreAfterMapReopenQueued = true;
			_ = TaskHelper.RunSafely(RestoreAfterMapReopenAsync());
		}
	}

	private void BeginMapPreview()
	{
		if (_mapPreviewActive)
		{
			return;
		}

		NMapScreen? map = NMapScreen.Instance;
		if (map == null)
		{
			return;
		}

		_mapPreviewActive = true;
		map.SetTravelEnabled(enabled: false);   // 只读：禁止在地图上选节点前进，杜绝靠地图跳过海克斯选择
		map.Closed += OnMapPreviewClosed;        // 玩家关闭地图后把选择界面恢复回来
		ShowMapPreviewHint(map);
		HextechLog.Info("Mayhem", $"SelectionScreen.BeginMapPreview: read-only map preview, selection still pending");
	}

	private void OnMapPreviewClosed()
	{
		EndMapPreview(restoreOverlay: true);
	}

	private void EndMapPreview(bool restoreOverlay)
	{
		if (!_mapPreviewActive)
		{
			return;
		}

		_mapPreviewActive = false;
		NMapScreen? map = NMapScreen.Instance;
		if (map != null)
		{
			map.Closed -= OnMapPreviewClosed;
			map.SetTravelEnabled(enabled: true);  // 恢复地图行进可用性，供选完后正常选路（地图此刻已关，恢复无即时副作用）
		}

		HideMapPreviewHint();

		if (!restoreOverlay || _closed || !IsInsideTree())
		{
			return;
		}

		NOverlayStack.Instance?.ShowOverlays();   // 把选择界面重新顶回来
		Visible = true;
		TryGrabOverlayFocus();
	}

	private void ShowMapPreviewHint(NMapScreen map)
	{
		if (_mapPreviewHint == null || !GodotObject.IsInstanceValid(_mapPreviewHint))
		{
			_mapPreviewHint = BuildMapPreviewHint();
		}

		if (_mapPreviewHint.GetParent() == null)
		{
			map.AddChild(_mapPreviewHint);
		}

		_mapPreviewHint.Visible = true;
	}

	private void HideMapPreviewHint()
	{
		if (_mapPreviewHint != null && GodotObject.IsInstanceValid(_mapPreviewHint))
		{
			_mapPreviewHint.Visible = false;
		}
	}

	private MegaLabel BuildMapPreviewHint()
	{
		MegaLabel hint = new()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MaxFontSize = 26,
			MinFontSize = 18,
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 4096
		};
		HextechUiTheme.ApplyDefaultMegaLabelTheme(hint);
		hint.SetAnchorsPreset(LayoutPreset.BottomWide);
		hint.OffsetTop = -96f;
		hint.OffsetBottom = -44f;
		hint.SetTextAutoSize(new LocString(LocTable, "HEXTECH_MAP_PREVIEW_HINT").GetRawText());
		hint.Modulate = new Color(1f, 0.93f, 0.7f, 0.96f);
		return hint;
	}

	private async Task RestoreAfterMapReopenAsync()
	{
		try
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!IsInsideTree() || (_choiceLocked && !_blockMapUntilDismissed))
			{
				return;
			}

			bool isTopOverlay = ReferenceEquals(NOverlayStack.Instance?.Peek(), this);
			bool capstoneOpen = NCapstoneContainer.Instance?.InUse == true;
			bool mapOpen = NMapScreen.Instance?.IsOpen == true;
			if (!isTopOverlay || capstoneOpen || !mapOpen)
			{
				return;
			}

			HextechLog.Info("Mayhem", $"SelectionScreen.RestoreAfterMapReopen: closing map reopened over blocking selection");
			NMapScreen.Instance?.Close(animateOut: false);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			NOverlayStack.Instance?.ShowOverlays();
		}
		finally
		{
			_restoreAfterMapReopenQueued = false;
		}
	}

	private void TryGrabOverlayFocus()
	{
		if (!HextechControllerInput.IsDirectionalNavigation || _closed || !IsInsideTree() || !IsVisibleInTree() || FocusMode == FocusModeEnum.None)
		{
			return;
		}

		Control? focused = GetViewport()?.GuiGetFocusOwner();
		if (focused != null && IsAncestorOf(focused))
		{
			return;
		}

		RestoreFocusDeferred(DefaultFocusedControl ?? this);
	}
}
