using Godot;

namespace HextechRunes;

// 我方海克斯抽选池按海克斯池（通用、各角色、拓展包、外部模组）筛选：只重排已有格子，不重建图标与绑定。
internal static partial class HextechRuneConfigMenuHooks
{
	private const string HiddenSlotsNodeName = "HextechHiddenSlots";

	// 内置池的固定顺序；其余（外部模组自定义的池）按键名排在后面。
	private static readonly string[] BuiltInPoolFilterOrder =
	[
		"GENERIC",
		"IRONCLAD",
		"SILENT",
		"REGENT",
		"DEFECT",
		"NECROBINDER",
		"SPONSOR_PACK"
	];

	private static Control CreatePlayerPoolFilterBar(ConfigMenuContext context, IReadOnlyList<FilterableRarityCard> cards)
	{
		bool compactLayout = context.CompactLayout;
		HFlowContainer bar = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		bar.AddThemeConstantOverride("h_separation", compactLayout ? 4 : 6);
		bar.AddThemeConstantOverride("v_separation", compactLayout ? 4 : 6);

		List<string?> poolKeys = [ null, .. GetPlayerPoolFilterKeys(context.PlayerEntries) ];
		List<Button> buttons = [];
		for (int i = 0; i < poolKeys.Count; i++)
		{
			int index = i;
			string? poolKey = poolKeys[i];
			string text = poolKey == null ? L("HEXTECH_CONFIG_POOL_FILTER_ALL") : HextechRuneLabels.GetPoolText(poolKey);
			Button button = CreateTabButton(text, () =>
			{
				ApplyPlayerPoolFilter(compactLayout, cards, poolKey);
				UpdateTabButtonStates(buttons, index, compactLayout);
			}, compactLayout);
			WireControllerFocusScrolling(button);
			buttons.Add(button);
			bar.AddChild(button);
		}

		UpdateTabButtonStates(buttons, 0, compactLayout);
		return bar;
	}

	private static IReadOnlyList<string> GetPlayerPoolFilterKeys(IEnumerable<RuneConfigEntry> entries)
	{
		HashSet<string> present = entries.Select(static entry => entry.PoolKey).ToHashSet(StringComparer.Ordinal);
		return BuiltInPoolFilterOrder
			.Where(present.Contains)
			.Concat(present
				.Where(key => !BuiltInPoolFilterOrder.Contains(key, StringComparer.Ordinal))
				.OrderBy(static key => key, StringComparer.Ordinal))
			.ToArray();
	}

	/// <summary>poolKey 为 null 时显示全部；没有可见格子的来源分组与稀有度卡片整块隐藏。</summary>
	private static void ApplyPlayerPoolFilter(bool compactLayout, IReadOnlyList<FilterableRarityCard> cards, string? poolKey)
	{
		foreach (FilterableRarityCard card in cards)
		{
			if (!GodotObject.IsInstanceValid(card.Card))
			{
				continue;
			}

			bool anyVisible = false;
			foreach (FilterableRuneGrid grid in card.Grids)
			{
				List<CenterContainer> visibleSlots = grid.Slots
					.Where(slot => poolKey == null || string.Equals(slot.Entry.PoolKey, poolKey, StringComparison.Ordinal))
					.Select(static slot => slot.Slot)
					.ToList();
				ReflowRuneGrid(compactLayout, grid, visibleSlots);
				bool hasVisible = visibleSlots.Count > 0;
				grid.Grid.Visible = hasVisible;
				if (grid.SourceHeader != null)
				{
					grid.SourceHeader.Visible = hasVisible;
				}

				anyVisible |= hasVisible;
			}

			card.Card.Visible = anyVisible;
		}
	}

	// 隐藏的格子停在网格内一个不可见的容器里（随菜单一起释放，图标异步加载照常写进格子），可见的按列数重新成行。
	private static void ReflowRuneGrid(bool compactLayout, FilterableRuneGrid grid, IReadOnlyList<CenterContainer> visibleSlots)
	{
		VBoxContainer container = grid.Grid;
		Control hidden = container.GetNodeOrNull<Control>(HiddenSlotsNodeName) ?? CreateHiddenSlotsHolder(container);
		foreach ((_, CenterContainer slot) in grid.Slots)
		{
			if (slot.GetParent() != hidden)
			{
				slot.GetParent()?.RemoveChild(slot);
				hidden.AddChild(slot);
			}
		}

		foreach (Node child in container.GetChildren())
		{
			if (child != hidden)
			{
				container.RemoveChild(child);
				child.QueueFree();
			}
		}

		HBoxContainer? row = null;
		int column = 0;
		foreach (CenterContainer slot in visibleSlots)
		{
			if (column == 0)
			{
				row = CreateRuneRow(compactLayout);
				container.AddChild(row);
			}

			hidden.RemoveChild(slot);
			row!.AddChild(slot);
			column = (column + 1) % RuneConfigColumns;
		}

		if (row != null && column > 0)
		{
			for (; column < RuneConfigColumns; column++)
			{
				row.AddChild(CreateRuneSlot());
			}
		}
	}

	private static Control CreateHiddenSlotsHolder(VBoxContainer container)
	{
		Control hidden = new()
		{
			Name = HiddenSlotsNodeName,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		container.AddChild(hidden);
		return hidden;
	}
}
