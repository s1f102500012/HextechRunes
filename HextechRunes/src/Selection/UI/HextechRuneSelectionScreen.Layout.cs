using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private void BuildUi()
	{
		VBoxContainer root = BuildContentRoot();
		root.AddChild(CreateTitleLabel());
		if (_monsterHexKinds.Count > 0 || _enemyHexControlsEnabled)
		{
			_enemyPreviewHost = new VBoxContainer()
			{
				Name = "EnemyPreviewHost",
				MouseFilter = MouseFilterEnum.Ignore,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			root.AddChild(_enemyPreviewHost);
			RebuildEnemyPreview();
		}

		HBoxContainer row = new()
		{
			Name = "PlayerCardsRow",
			Alignment = BoxContainer.AlignmentMode.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore
		};
		row.AddThemeConstantOverride("separation", 28);
		root.AddChild(row);
		_cardsRow = row;
		row.Visible = !_enemyOnly && !SelfPickMode;
		RollSignalNoise();

		if (SelfPickMode)
		{
			root.AddChild(CreateSelfPickPanel());
		}

		RebuildCards();
		if (_continueOnly)
		{
			root.AddChild(CreateContinueHint());
		}

		if (_enemyOnly)
		{
			root.AddChild(CreateEnemyOnlyConfirm());
		}
		else if (UsesPlayerRuneConfirmation)
		{
			root.AddChild(CreatePlayerRuneSelectionActions());
		}

		AddStatusLabel(root);
	}

	// 遮罩 + 居中内容面板,返回放置标题、敌方预览、候选卡与按钮的纵向容器。
	private VBoxContainer BuildContentRoot()
	{
		ColorRect backdrop = new()
		{
			Name = "DimOverlay",
			Color = new Color(0.02f, 0.025f, 0.035f, 0.56f),
			MouseFilter = MouseFilterEnum.Stop
		};
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);

		CenterContainer screenCenter = new()
		{
			Name = "ScreenCenter",
			MouseFilter = MouseFilterEnum.Ignore
		};
		screenCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(screenCenter);

		PanelContainer contentPanel = new()
		{
			Name = "ContentPanel",
			CustomMinimumSize = new Vector2(1180f, _enemyOnly ? 460f : 780f),
			MouseFilter = MouseFilterEnum.Ignore
		};
		contentPanel.AddThemeStyleboxOverride("panel", CreateContentPanelStyle());
		screenCenter.AddChild(contentPanel);

		MarginContainer contentMargin = new()
		{
			MouseFilter = MouseFilterEnum.Ignore
		};
		contentMargin.AddThemeConstantOverride("margin_left", 30);
		contentMargin.AddThemeConstantOverride("margin_right", 30);
		contentMargin.AddThemeConstantOverride("margin_top", 28);
		contentMargin.AddThemeConstantOverride("margin_bottom", 28);
		contentPanel.AddChild(contentMargin);

		VBoxContainer root = new()
		{
			MouseFilter = MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		root.AddThemeConstantOverride("separation", 20);
		contentMargin.AddChild(root);
		return root;
	}

	private MegaLabel CreateTitleLabel()
	{
		MegaLabel title = new()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MaxFontSize = 48,
			MinFontSize = 30
		};
		HextechUiTheme.ApplyDefaultMegaLabelTheme(title);
		title.Modulate = new Color(0.96f, 0.97f, 0.99f, 0.98f);
		title.SetTextAutoSize(_titleOverride ?? new LocString(LocTable, "HEXTECH_SELECTION_TITLE").GetRawText());
		return title;
	}

	private static MegaLabel CreateContinueHint()
	{
		MegaLabel hint = new()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MaxFontSize = 24,
			MinFontSize = 16
		};
		HextechUiTheme.ApplyDefaultMegaLabelTheme(hint);
		hint.Modulate = new Color(0.88f, 0.92f, 0.97f, 0.86f);
		hint.SetTextAutoSize(new LocString(LocTable, "HEXTECH_NO_RUNE_OPTIONS_HINT").GetRawText());
		return hint;
	}

	private Button CreateEnemyOnlyConfirm()
	{
		bool confirmEnabled = _enemyHexControlsEnabled || _continueOnly;
		Button confirm = CreateConfirmButton("EnemyOnlyConfirm", new Vector2(260f, 60f), out MegaLabel confirmLabel);
		_enemyOnlyConfirm = confirm;
		confirmLabel.SetTextAutoSize(new LocString(LocTable, _continueOnly ? "HEXTECH_CONTINUE" : "HEXTECH_ENEMY_CONFIRM").GetRawText());
		confirm.Disabled = !confirmEnabled;
		confirm.Pressed += () =>
		{
			if (confirmEnabled && !IsSelectionConfirmGuardActive())
			{
				GetViewport()?.SetInputAsHandled();
				CompleteEnemyOnlySelection();
			}
		};
		return confirm;
	}

	private HBoxContainer CreatePlayerRuneSelectionActions()
	{
		HBoxContainer selectionActions = new()
		{
			Name = "PlayerRuneSelectionActions",
			Alignment = BoxContainer.AlignmentMode.Center,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore
		};
		selectionActions.AddThemeConstantOverride("separation", 16);

		_playerRuneConfirm = CreateConfirmButton("PlayerRuneConfirm", new Vector2(240f, 60f), out MegaLabel playerConfirmLabel);
		playerConfirmLabel.SetTextAutoSize(new LocString(LocTable, "HEXTECH_ENEMY_CONFIRM").GetRawText());
		_playerRuneConfirm.Disabled = true;
		_playerRuneConfirm.Pressed += OnPlayerRuneConfirmPressed;
		selectionActions.AddChild(_playerRuneConfirm);

		_playerRuneCancel = CreateConfirmButton("PlayerRuneCancel", new Vector2(240f, 60f), out MegaLabel playerCancelLabel, secondary: true);
		playerCancelLabel.SetTextAutoSize(new LocString(LocTable, "HEXTECH_CONFIG_CANCEL").GetRawText());
		_playerRuneCancel.Disabled = true;
		_playerRuneCancel.Pressed += OnPlayerRuneCancelPressed;
		selectionActions.AddChild(_playerRuneCancel);
		return selectionActions;
	}

	private void AddStatusLabel(VBoxContainer root)
	{
		_statusLabel = new MegaLabel()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MaxFontSize = 22,
			MinFontSize = 16,
			Visible = false
		};
		HextechUiTheme.ApplyDefaultMegaLabelTheme(_statusLabel);
		_statusLabel.Modulate = new Color(0.88f, 0.92f, 0.97f, 0.82f);
		root.AddChild(_statusLabel);
		if (_enemyOnly && !_enemyHexControlsEnabled && !_continueOnly)
		{
			ShowWaitingForRemotePlayers();
		}
	}

	private void RebuildEnemyPreview()
	{
		if (_enemyPreviewHost == null)
		{
			return;
		}

		foreach (Node child in _enemyPreviewHost.GetChildren())
		{
			_enemyPreviewHost.RemoveChild(child);
			child.QueueFree();
		}

		_enemyHexRerollButtons.Clear();
		_enemyHexRemoveButtons.Clear();
		_enemyPreviewHost.AddChild(CreateEnemyPreview());
		ConfigureControllerNavigation();
	}

	private void RebuildCards()
	{
		// 自选模式不展示候选卡与重随按钮。
		if (_cardsRow == null || SelfPickMode)
		{
			return;
		}

		// 先标记删除再摘下：摘下时卡牌特效在 TreeExiting 里据此判断是销毁而不是暂时摘下，主动释放网格与材质。
		// 还要接着播放的特效在重建前已经单独摘走（见 HextechRuneSelectionScreen.Vfx）。
		foreach (Node child in _cardsRow.GetChildren())
		{
			child.QueueFree();
			_cardsRow.RemoveChild(child);
		}

		_holders.Clear();
		_pendingSelectionOutlines.Clear();
		_rerollButtons.Clear();
		_goldenRerollVisuals.Clear();
		while (_playerRuneRerollCounts.Count < _relics.Count)
		{
			_playerRuneRerollCounts.Add(0);
		}
		if (_pendingPlayerRuneSlot is int pendingSlot
			&& (pendingSlot < 0 || pendingSlot >= _relics.Count))
		{
			_pendingPlayerRuneSlot = null;
		}

		for (int i = 0; i < _relics.Count; i++)
		{
			Control slot = CreateCardSlot(_relics[i], i);
			_cardsRow.AddChild(slot);
		}

		ConfigureControllerNavigation();
		UpdatePlayerRuneActionButtons();
	}

}
