using Godot;

namespace HextechRunes;

// 四个页签的页面内容:数量页、符文池页、锻造页、杂项页。通用控件在 Widgets,样式在 Styles。
internal static partial class HextechRuneConfigMenuHooks
{
	private static Control CreateSelectionPage(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		VBoxContainer page = CreatePageContainer(context.CompactLayout);
		page.AddChild(CreateActCountSection(
			context,
			L("HEXTECH_PLAYER_COUNT_TITLE"),
			L("HEXTECH_PLAYER_COUNT_DESCRIPTION"),
			pending.PlayerHexCounts));
		page.AddChild(CreateActCountSection(
			context,
			L("HEXTECH_ENEMY_COUNT_TITLE"),
			L("HEXTECH_ENEMY_COUNT_DESCRIPTION"),
			pending.EnemyHexCounts));
		page.AddChild(CreateRerollLimitSection(context));
		page.AddChild(CreateGoldenRerollChanceSection(context));
		if (HextechRuneGeneration.ChaosAvailable)
		{
			page.AddChild(CreateChaosRuneChanceSection(context));
		}

		return page;
	}

	private static Control CreateGoldenRerollChanceSection(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		return CreateStepperCard(
			context,
			L("HEXTECH_GOLDEN_REROLL_CHANCE_LABEL"),
			L("HEXTECH_GOLDEN_REROLL_CHANCE_DESCRIPTION"),
			spacedRow: false,
			CreateNumericStepper(
				context,
				L("HEXTECH_GOLDEN_REROLL_CHANCE_VALUE_LABEL"),
				() => pending.GoldenRerollChancePercent,
				value => pending.GoldenRerollChancePercent = HextechRuneConfiguration.ClampGoldenRerollChancePercent(value),
				getDisplayText: () => $"{pending.GoldenRerollChancePercent}%"));
	}

	private static Control CreateChaosRuneChanceSection(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		return CreateStepperCard(
			context,
			L("HEXTECH_CHAOS_RUNE_CHANCE_LABEL"),
			L("HEXTECH_CHAOS_RUNE_CHANCE_DESCRIPTION"),
			spacedRow: false,
			CreateNumericStepper(
				context,
				L("HEXTECH_CHAOS_RUNE_CHANCE_VALUE_LABEL"),
				() => pending.ChaosRuneChancePercent,
				value => pending.ChaosRuneChancePercent = HextechRuneConfiguration.ClampChaosRuneChancePercent(value),
				getDisplayText: () => $"{pending.ChaosRuneChancePercent}%"));
	}

	/// <summary>每幕一个步进器;幕数取自编辑态数组长度。</summary>
	private static Control CreateActCountSection(
		ConfigMenuContext context,
		string titleText,
		string descriptionText,
		int[] counts)
	{
		Control[] steppers = new Control[counts.Length];
		for (int act = 0; act < counts.Length; act++)
		{
			int index = act;
			steppers[act] = CreateNumericStepper(
				context,
				GetActLabel(index),
				() => counts[index],
				value => counts[index] = HextechRuneConfiguration.ClampActHexCount(value));
		}

		return CreateStepperCard(context, titleText, descriptionText, spacedRow: true, steppers);
	}

	private static Control CreateRerollLimitSection(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		return CreateStepperCard(
			context,
			L("HEXTECH_REROLL_LIMIT_TITLE"),
			L("HEXTECH_REROLL_LIMIT_DESCRIPTION"),
			spacedRow: true,
			CreateRerollLimitStepper(
				context,
				L("HEXTECH_PLAYER_REROLL_LIMIT_LABEL"),
				() => pending.PlayerRuneRerollLimit,
				value => pending.PlayerRuneRerollLimit = HextechRuneConfiguration.ClampRerollLimit(value)),
			CreateRerollLimitStepper(
				context,
				L("HEXTECH_MONSTER_REROLL_LIMIT_LABEL"),
				() => pending.MonsterHexRerollLimit,
				value => pending.MonsterHexRerollLimit = HextechRuneConfiguration.ClampRerollLimit(value)));
	}

	/// <summary>第 <paramref name="actIndex"/>(0 起)幕的行/列标题。</summary>
	private static string GetActLabel(int actIndex)
	{
		return L($"HEXTECH_ENEMY_COUNT_ACT{actIndex + 1}");
	}

	private static Control CreateRunePoolPage(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		VBoxContainer page = CreatePageContainer(context.CompactLayout);
		page.AddChild(CreatePoolGroupHeader(L("HEXTECH_PLAYER_POOL_TITLE"), context.CompactLayout));
		List<FilterableRarityCard> playerCards = [];
		Control filterBar = CreatePlayerPoolFilterBar(context, playerCards);
		page.AddChild(filterBar);
		AddIconPoolEntries(context, page, context.PlayerEntries, pending.DisabledPlayerRuneIds, context.PlayerIconBindings, playerCards);
		page.AddChild(CreatePoolGroupHeader(L("HEXTECH_ENEMY_POOL_TITLE"), context.CompactLayout));
		AddIconPoolEntries(context, page, context.EnemyEntries, pending.DisabledMonsterHexIds, context.EnemyIconBindings);
		return page;
	}

	private static Control CreateIconPoolPage(
		ConfigMenuContext context,
		IReadOnlyList<RuneConfigEntry> entries,
		HashSet<string> pendingDisabledIds,
		List<RuneIconBinding> bindings,
		string title)
	{
		VBoxContainer page = CreatePageContainer(context.CompactLayout);
		page.AddChild(CreatePoolGroupHeader(title, context.CompactLayout));
		AddIconPoolEntries(context, page, entries, pendingDisabledIds, bindings);
		return page;
	}

	/// <summary>
	/// 按稀有度分卡片、卡内按来源分组铺格子。格子只占位,图标由 <see cref="PopulateRuneIconsAsync"/> 分帧填入,
	/// 建好的绑定进 <paramref name="bindings"/>。
	/// </summary>
	private static void AddIconPoolEntries(
		ConfigMenuContext context,
		VBoxContainer page,
		IReadOnlyList<RuneConfigEntry> entries,
		HashSet<string> pendingDisabledIds,
		List<RuneIconBinding> bindings,
		List<FilterableRarityCard>? filterableCards = null)
	{
		bool compactLayout = context.CompactLayout;
		foreach (IGrouping<int, RuneConfigEntry> rarityGroup in entries.GroupBy(static entry => entry.RarityOrder))
		{
			List<RuneConfigEntry> groupEntries = rarityGroup.ToList();
			Color accent = GetRarityAccentColor((HextechRarityTier)rarityGroup.Key);
			VBoxContainer card = CreateCardSection(string.Empty, accent, compactLayout, out PanelContainer cardNode);
			page.AddChild(cardNode);
			card.AddChild(CreateRarityGroupHeaderRow(context, groupEntries.First().RarityText, accent, groupEntries, pendingDisabledIds));

			List<IGrouping<string, RuneConfigEntry>> sourceGroups = rarityGroup
				.GroupBy(static entry => entry.SourceKey)
				.ToList();
			List<FilterableRuneGrid> grids = [];
			foreach (IGrouping<string, RuneConfigEntry> sourceGroup in sourceGroups)
			{
				Label? sourceHeader = null;
				if (sourceGroups.Count > 1)
				{
					sourceHeader = CreateSourceHeader(sourceGroup.First().SourceText, compactLayout);
					card.AddChild(sourceHeader);
				}

				VBoxContainer grid = CreateRuneGrid(compactLayout);
				card.AddChild(grid);
				grids.Add(new FilterableRuneGrid(grid, sourceHeader, AddRuneSlots(context, grid, sourceGroup, pendingDisabledIds, bindings)));
			}

			filterableCards?.Add(new FilterableRarityCard(cardNode, grids));
		}
	}

	private static List<(RuneConfigEntry Entry, CenterContainer Slot)> AddRuneSlots(
		ConfigMenuContext context,
		VBoxContainer grid,
		IEnumerable<RuneConfigEntry> entries,
		HashSet<string> pendingDisabledIds,
		List<RuneIconBinding> bindings)
	{
		List<(RuneConfigEntry Entry, CenterContainer Slot)> slots = [];
		HBoxContainer? currentRow = null;
		int column = 0;
		foreach (RuneConfigEntry entry in entries)
		{
			if (column == 0)
			{
				currentRow = CreateRuneRow(context.CompactLayout);
				grid.AddChild(currentRow);
			}

			CenterContainer slot = CreateRuneSlot();
			currentRow?.AddChild(slot);
			slots.Add((entry, slot));
			context.LoadTargets.Add(new RuneConfigLoadTarget(entry, slot, pendingDisabledIds, bindings));

			column++;
			if (column == RuneConfigColumns)
			{
				column = 0;
			}
		}

		// 末行补空格,保持每行等宽列。
		if (currentRow != null && column > 0)
		{
			for (; column < RuneConfigColumns; column++)
			{
				currentRow.AddChild(CreateRuneSlot());
			}
		}

		return slots;
	}

	private static Control CreateRarityGroupHeaderRow(
		ConfigMenuContext context,
		string rarityText,
		Color accent,
		IReadOnlyList<RuneConfigEntry> groupEntries,
		HashSet<string> pendingDisabledIds)
	{
		bool compactLayout = context.CompactLayout;
		HBoxContainer row = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		row.AddThemeConstantOverride("separation", compactLayout ? 8 : 12);

		Label title = CreateLabel(rarityText, compactLayout ? 16 : 18, accent);
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		title.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(title);

		string[] groupIds = groupEntries.Select(static entry => entry.Id).ToArray();
		int total = groupIds.Length;
		Label badge = CreateLabel(string.Empty, compactLayout ? 13 : 14, accent);
		badge.HorizontalAlignment = HorizontalAlignment.Right;
		badge.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(badge);

		void Refresh()
		{
			int disabled = groupIds.Count(pendingDisabledIds.Contains);
			int enabled = Math.Max(0, total - disabled);
			SetLabelText(badge, $"{enabled}/{total}");
		}

		Refresh();
		context.BadgeRefreshers.Add(Refresh);
		return row;
	}

	private static Control CreateDetailsPage(ConfigMenuContext context)
	{
		VBoxContainer page = CreatePageContainer(context.CompactLayout);
		page.AddChild(CreateMiscUiSection(context));
		page.AddChild(CreateShareSection(context));
		page.AddChild(CreatePriceSection(context));
		page.AddChild(CreateWeightMatrixSection(context));
		return page;
	}

	private static Control CreateMiscUiSection(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		VBoxContainer section = CreateCardSection(L("HEXTECH_MISC_UI_TITLE"), null, context.CompactLayout, out PanelContainer card);
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_MOD_ENABLED_TOGGLE_TITLE"),
			L("HEXTECH_MOD_ENABLED_TOGGLE_DESCRIPTION"),
			() => pending.ModEnabled,
			value => pending.ModEnabled = value));
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_SHOW_UPDATE_NOTICE_TOGGLE_TITLE"),
			L("HEXTECH_SHOW_UPDATE_NOTICE_TOGGLE_DESCRIPTION"),
			() => pending.ShowUpdateNotice,
			value => pending.ShowUpdateNotice = value));
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_COLLAPSE_ENEMY_HEXES_TOGGLE_TITLE"),
			L("HEXTECH_COLLAPSE_ENEMY_HEXES_TOGGLE_DESCRIPTION"),
			() => pending.CollapseEnemyHexes,
			value => pending.CollapseEnemyHexes = value));
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_CONFIRM_RUNE_SELECTION_TOGGLE_TITLE"),
			L("HEXTECH_CONFIRM_RUNE_SELECTION_TOGGLE_DESCRIPTION"),
			() => pending.ConfirmRuneSelection,
			value => pending.ConfirmRuneSelection = value));
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_SHOW_HIDDEN_RELICS_TOGGLE_TITLE"),
			L("HEXTECH_SHOW_HIDDEN_RELICS_TOGGLE_DESCRIPTION"),
			() => pending.ShowHiddenRelicsToggle,
			value => pending.ShowHiddenRelicsToggle = value));
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_RANDOM_FORGE_TOGGLE_TITLE"),
			L("HEXTECH_RANDOM_FORGE_TOGGLE_DESCRIPTION"),
			() => pending.RandomForgeDirectGrant,
			value => pending.RandomForgeDirectGrant = value));
		return card;
	}

	// 「杂项」页的配置分享区:导出/导入配置码 + 社区配置入口。
	private static Control CreateShareSection(ConfigMenuContext context)
	{
		bool compactLayout = context.CompactLayout;
		VBoxContainer section = CreateCardSection(L("HEXTECH_CONFIG_SHARE_TITLE"), null, compactLayout, out PanelContainer card);

		Label hint = CreateLabel(L("HEXTECH_CONFIG_SHARE_HINT"), 12, HextechUiTheme.HintText);
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		section.AddChild(hint);

		HBoxContainer buttons = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
		};
		buttons.AddThemeConstantOverride("separation", compactLayout ? 8 : 12);
		buttons.AddChild(CreateActionButton(L("HEXTECH_CONFIG_EXPORT_CODE"), () => ExportShareCodeToClipboard(context), compactLayout));
		buttons.AddChild(CreateActionButton(L("HEXTECH_CONFIG_IMPORT_CODE"), () => ImportShareCodeFromClipboard(context), compactLayout));
		buttons.AddChild(CreateActionButton(L("HEXTECH_CONFIG_FEATURED"), () => OpenCommunityConfigsPanel(context), compactLayout));
		buttons.AddChild(CreateGodslayerTrialExitButton(context));
		section.AddChild(buttons);

		return card;
	}

	private static Control CreatePriceSection(ConfigMenuContext context)
	{
		PendingConfig pending = context.Pending;
		VBoxContainer section = CreateCardSection(L("HEXTECH_FORGE_PRICE_TITLE"), null, context.CompactLayout, out PanelContainer card);
		HBoxContainer row = new()
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		row.AddChild(CreateNumericStepper(
			context,
			L("HEXTECH_FORGE_PRICE_LABEL"),
			() => pending.ForgePrice,
			value => pending.ForgePrice = HextechRuneConfiguration.ClampRandomForgeShopPrice(value),
			step: 10));
		section.AddChild(row);
		return card;
	}

	/// <summary>稀有度权重矩阵:每幕一行 + 锻造一行,列为银/金/棱彩,每格显示权重与所占百分比。</summary>
	private static Control CreateWeightMatrixSection(ConfigMenuContext context)
	{
		bool compactLayout = context.CompactLayout;
		PendingConfig pending = context.Pending;
		VBoxContainer section = CreateCardSection(L("HEXTECH_RARITY_WEIGHTS_TITLE"), null, compactLayout, out PanelContainer card);
		Label description = CreateLabel(L("HEXTECH_RARITY_WEIGHTS_DESCRIPTION"), compactLayout ? 12 : 13, HextechUiTheme.OptionDescriptionText);
		description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		section.AddChild(description);

		HextechRarityTier[] rarityColumns = [ HextechRarityTier.Silver, HextechRarityTier.Gold, HextechRarityTier.Prismatic ];
		GridContainer grid = new()
		{
			Columns = rarityColumns.Length + 1,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		grid.AddThemeConstantOverride("h_separation", compactLayout ? 8 : 16);
		grid.AddThemeConstantOverride("v_separation", compactLayout ? 8 : 14);
		section.AddChild(grid);

		// 表头:空角 + 各稀有度列名。
		grid.AddChild(new Control { CustomMinimumSize = new Vector2(compactLayout ? 76f : 110f, 0f) });
		foreach (HextechRarityTier rarity in rarityColumns)
		{
			grid.AddChild(CreateRarityColumnHeader(L("HEXTECH_RARITY_" + rarity.ToString().ToUpperInvariant()), rarity, compactLayout));
		}

		for (int act = 0; act < pending.RuneWeightsByAct.Length; act++)
		{
			AddWeightMatrixRow(context, grid, GetActLabel(act), pending.RuneWeightsByAct[act]);
		}

		AddWeightMatrixRow(context, grid, L("HEXTECH_RARITY_WEIGHTS_ROW_FORGE"), pending.ForgeWeights);
		section.AddChild(CreateBooleanOption(
			context,
			L("HEXTECH_PREVENT_CONSECUTIVE_SILVER_TOGGLE_TITLE"),
			L("HEXTECH_PREVENT_CONSECUTIVE_SILVER_TOGGLE_DESCRIPTION"),
			() => pending.PreventConsecutiveSilverRunes,
			value => pending.PreventConsecutiveSilverRunes = value));
		return card;
	}

	private static Label CreateRarityColumnHeader(string text, HextechRarityTier rarity, bool compactLayout)
	{
		Label label = CreateLabel(text, compactLayout ? 14 : 16, GetRarityAccentColor(rarity));
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return label;
	}

	private static void AddWeightMatrixRow(ConfigMenuContext context, GridContainer grid, string rowLabel, int[] weights)
	{
		bool compactLayout = context.CompactLayout;
		Label label = CreateLabel(rowLabel, compactLayout ? 12 : 14, HextechUiTheme.StepperLabelText);
		label.HorizontalAlignment = HorizontalAlignment.Left;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		grid.AddChild(label);

		// 同一行任一格变动都要刷新整行的百分比;格子建完后再把各格的刷新串起来。
		List<Action> refreshPercents = [];
		void RefreshRowPercents()
		{
			foreach (Action refresh in refreshPercents)
			{
				refresh();
			}
		}

		for (int column = 0; column < weights.Length; column++)
		{
			grid.AddChild(CreateWeightMatrixCell(
				context,
				weights,
				column,
				GetRarityAccentColor((HextechRarityTier)column),
				RefreshRowPercents,
				out Action refreshThisCell));
			refreshPercents.Add(refreshThisCell);
		}
	}

	private static Control CreateWeightMatrixCell(
		ConfigMenuContext context,
		int[] weights,
		int index,
		Color accent,
		Action refreshRow,
		out Action refreshPercent)
	{
		bool compactLayout = context.CompactLayout;
		VBoxContainer cell = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		cell.AddThemeConstantOverride("separation", compactLayout ? 1 : 3);

		HBoxContainer controls = new()
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		controls.AddThemeConstantOverride("separation", compactLayout ? 5 : 7);
		cell.AddChild(controls);

		Label number = CreateLabel(weights[index].ToString(), compactLayout ? 16 : 18, HextechUiTheme.NumberText);
		number.HorizontalAlignment = HorizontalAlignment.Center;
		number.VerticalAlignment = VerticalAlignment.Center;
		number.CustomMinimumSize = compactLayout ? new Vector2(36f, 30f) : new Vector2(46f, 34f);
		context.NumericBindings.Add(new NumericValueBinding(() => weights[index].ToString(), number));

		string PercentText()
		{
			int total = weights.Sum();
			float percent = total > 0 ? weights[index] * 100f / total : 0f;
			return $"{percent:0.#}%";
		}

		Color percentColor = accent;
		percentColor.A = 0.78f;
		Label percent = CreateLabel(PercentText(), compactLayout ? 11 : 12, percentColor);
		percent.HorizontalAlignment = HorizontalAlignment.Center;
		context.NumericBindings.Add(new NumericValueBinding(PercentText, percent));
		refreshPercent = () => SetLabelText(percent, PercentText());

		void Step(int delta)
		{
			weights[index] = HextechRuneConfiguration.ClampRarityWeight(weights[index] + delta);
			SetLabelText(number, weights[index].ToString());
			refreshRow();
		}

		Button minus = CreateStepButton("-", compactLayout);
		Button plus = CreateStepButton("+", compactLayout);
		AttachRepeatingStep(minus, () => Step(-1));
		AttachRepeatingStep(plus, () => Step(1));

		controls.AddChild(minus);
		controls.AddChild(number);
		controls.AddChild(plus);
		cell.AddChild(percent);
		return cell;
	}
}
