using Godot;

namespace HextechRunes;

internal static partial class HextechRuneConfigMenuHooks
{
	/// <summary>
	/// 页脚:摘要行 + 操作按钮(重置 / 全部启用 / 全部禁用 靠左,保存并关闭 / 取消 靠右)。
	/// <paramref name="updatePageActions"/> 按页签切换批量按钮的可见性。
	/// </summary>
	private static Control CreateBottomBar(ConfigMenuContext context, out Action<ConfigPage> updatePageActions)
	{
		bool compactLayout = context.CompactLayout;
		VBoxContainer bar = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		bar.AddThemeConstantOverride("separation", compactLayout ? 6 : 9);
		bar.AddChild(CreateHairline());

		Button enableAll = CreateActionButton(L("HEXTECH_CONFIG_ENABLE_ALL"), () => EnableAllOnPage(context), compactLayout);
		Button disableAll = CreateActionButton(L("HEXTECH_CONFIG_DISABLE_ALL"), () => DisableAllOnPage(context), compactLayout);
		Button reset = CreateActionButton(L("HEXTECH_CONFIG_RESET"), () => ResetPage(context), compactLayout);
		Button save = CreateActionButton(L("HEXTECH_CONFIG_SAVE_CLOSE"), () => SaveAndClose(context), compactLayout);
		Button cancel = CreateActionButton(L("HEXTECH_CONFIG_CANCEL"), () => CloseWithoutSaving(context.Overlay), compactLayout);

		// 摘要独占一行、居中换行,长短不影响面板宽度;始终保留一行高度,切页签时面板高度不跳。
		Label summary = context.Summary;
		summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		summary.HorizontalAlignment = HorizontalAlignment.Center;
		summary.VerticalAlignment = VerticalAlignment.Center;
		summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		summary.CustomMinimumSize = new Vector2(0f, compactLayout ? 18f : 20f);
		bar.AddChild(summary);

		HBoxContainer row = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		row.AddThemeConstantOverride("separation", compactLayout ? 7 : 12);
		bar.AddChild(row);

		Control spacer = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		row.AddChild(reset);
		row.AddChild(enableAll);
		row.AddChild(disableAll);
		row.AddChild(spacer);
		row.AddChild(save);
		row.AddChild(cancel);

		updatePageActions = page =>
		{
			bool showPoolBulkActions = page is ConfigPage.RunePools or ConfigPage.Forges;
			enableAll.Visible = showPoolBulkActions;
			disableAll.Visible = showPoolBulkActions;
		};
		return bar;
	}

	private static ColorRect CreateHairline()
	{
		return new ColorRect
		{
			Color = HextechUiTheme.Hairline,
			CustomMinimumSize = new Vector2(0f, 1f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
	}

	private static void EnableAllOnPage(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		switch (context.SelectedPage)
		{
			case ConfigPage.RunePools:
				pending.DisabledPlayerRuneIds.Clear();
				pending.DisabledMonsterHexIds.Clear();
				UpdateAllRuneIcons(context.PlayerIconBindings, pending.DisabledPlayerRuneIds);
				UpdateAllRuneIcons(context.EnemyIconBindings, pending.DisabledMonsterHexIds);
				break;
			case ConfigPage.Forges:
				pending.DisabledForgeIds.Clear();
				UpdateAllRuneIcons(context.ForgeIconBindings, pending.DisabledForgeIds);
				break;
		}

		context.UpdateSummary();
	}

	private static void DisableAllOnPage(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		switch (context.SelectedPage)
		{
			case ConfigPage.RunePools:
				ReplaceDisabledIds(pending.DisabledPlayerRuneIds, context.PlayerEntries);
				ReplaceDisabledIds(pending.DisabledMonsterHexIds, context.EnemyEntries);
				UpdateAllRuneIcons(context.PlayerIconBindings, pending.DisabledPlayerRuneIds);
				UpdateAllRuneIcons(context.EnemyIconBindings, pending.DisabledMonsterHexIds);
				break;
			case ConfigPage.Forges:
				ReplaceDisabledIds(pending.DisabledForgeIds, context.ForgeEntries);
				UpdateAllRuneIcons(context.ForgeIconBindings, pending.DisabledForgeIds);
				break;
		}

		context.UpdateSummary();
	}

	private static void ResetPage(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		HextechRunConfigurationSnapshot defaults = HextechRuneConfiguration.GetDefaultSnapshot();
		switch (context.SelectedPage)
		{
			case ConfigPage.Counts:
				pending.LoadFrom(defaults, PendingFields.ActCounts);
				UpdateNumericLabels(context.NumericBindings);
				break;
			case ConfigPage.RunePools:
				pending.LoadFrom(defaults, PendingFields.RunePools);
				UpdateAllRuneIcons(context.PlayerIconBindings, pending.DisabledPlayerRuneIds);
				UpdateAllRuneIcons(context.EnemyIconBindings, pending.DisabledMonsterHexIds);
				break;
			case ConfigPage.Forges:
				pending.LoadFrom(defaults, PendingFields.ForgePool);
				UpdateAllRuneIcons(context.ForgeIconBindings, pending.DisabledForgeIds);
				break;
			case ConfigPage.Details:
				// UI 偏好不在运行配置快照里,默认值单独从 HextechUiPreferences 取。
				pending.LoadFrom(
					defaults,
					PendingFields.Details | PendingFields.ModEnabled | PendingFields.UiPreferences,
					HextechUiPreferences.DefaultShowHiddenRelicsToggle,
					HextechUiPreferences.DefaultShowUpdateNotice,
					HextechUiPreferences.DefaultCollapseEnemyHexes,
					HextechUiPreferences.DefaultConfirmRuneSelection);
				UpdateNumericLabels(context.NumericBindings);
				UpdateBooleanToggles(context.BooleanBindings);
				break;
		}

		context.UpdateSummary();
	}

	private static void SaveAndClose(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		HextechRuneConfiguration.SaveSnapshot(pending.ToSnapshot());
		HextechUiPreferences.SaveMenuPreferences(
			pending.ShowHiddenRelicsToggle,
			pending.ShowUpdateNotice,
			pending.CollapseEnemyHexes,
			pending.ConfirmRuneSelection);
		HextechRelicVisibilityHooks.RefreshToggleForCurrentRun();
		HextechUpdateChecker.ApplyNoticeVisibility(context.Overlay);
		// 不用刷新遗物图鉴:它和配置菜单不会同时打开,原版每次打开图鉴都会重新 LoadRelics。
		string runeWeights = string.Join("/", pending.RuneWeightsByAct.Select(static weights => string.Join(",", weights)));
		HextechLog.Info("RuneConfig", $"Saved run config: playerDisabled={pending.DisabledPlayerRuneIds.Count} enemyDisabled={pending.DisabledMonsterHexIds.Count} forgeDisabled={pending.DisabledForgeIds.Count} playerCounts={string.Join(",", pending.PlayerHexCounts)} enemyCounts={string.Join(",", pending.EnemyHexCounts)} playerRerolls={pending.PlayerRuneRerollLimit} monsterRerolls={pending.MonsterHexRerollLimit} runeWeightsByAct={runeWeights} preventConsecutiveSilver={pending.PreventConsecutiveSilverRunes} goldenRerollChance={pending.GoldenRerollChancePercent}% forgePrice={pending.ForgePrice} showHiddenUiToggle={pending.ShowHiddenRelicsToggle} showUpdateNotice={pending.ShowUpdateNotice} randomForgeDirect={pending.RandomForgeDirectGrant} modEnabled={pending.ModEnabled}");
		CloseOverlayAnimated(context.Overlay);
	}

	/// <summary>
	/// 配置分享码导出:把当前编辑中的配置(pending 态)编码进剪贴板。
	/// 导入(<see cref="ImportShareCodeFromClipboard"/>)从剪贴板解析并填充 pending 态
	/// (界面即预览,可继续修改,「取消」可放弃)——真正落盘仍走「保存并关闭」。社区配置的「应用」走同一条导入路径。
	/// </summary>
	private static void ExportShareCodeToClipboard(ConfigMenuContext context)
	{
		DisplayServer.ClipboardSet(BuildPendingShareCode(context));
		context.ShowSummaryNotice(L("HEXTECH_CONFIG_EXPORT_DONE"));
	}

	private static void ImportShareCodeFromClipboard(ConfigMenuContext context)
	{
		string clipboard = DisplayServer.ClipboardGet();
		// 口令不改动编辑中的配置，只打开弑神试炼；其余内容照常按分享码解析。
		if (HextechSignalNoise.TryEcho(clipboard))
		{
			EnterGodslayerTrial(context);
			return;
		}

		HextechConfigShareCodec.ImportPreview? preview = HextechConfigShareCodec.TryParse(clipboard);
		if (preview == null)
		{
			context.ShowSummaryNotice(L("HEXTECH_CONFIG_IMPORT_INVALID"));
			return;
		}

		ApplyImportPreview(context, preview);
	}

	private static string BuildPendingShareCode(ConfigMenuContext context)
	{
		return HextechConfigShareCodec.Export(context.Pending.ToSnapshot());
	}

	/// <summary>把分享码/社区配置的解析结果填进 pending 编辑态并刷新全部控件。</summary>
	private static void ApplyImportPreview(ConfigMenuContext context, HextechConfigShareCodec.ImportPreview preview)
	{
		// PendingFields.ShareCode 刻意不含 ModEnabled 与 UI 偏好(折叠/隐藏遗物开关等),它们不随导入改变。
		context.Pending.LoadFrom(preview.Snapshot, PendingFields.ShareCode);
		context.RefreshAllControls();
		context.ShowSummaryNotice(string.Format(L("HEXTECH_CONFIG_IMPORT_DONE"), preview.IgnoredUnknownCount));
	}

	private static void ReplaceDisabledIds(HashSet<string> target, IEnumerable<RuneConfigEntry> entries)
	{
		target.Clear();
		foreach (RuneConfigEntry entry in entries)
		{
			target.Add(entry.Id);
		}
	}
}
