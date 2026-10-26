using Godot;

namespace HextechRunes;

internal static partial class HextechRuneConfigMenuHooks
{
	// Godot 节点 meta 键:只在本菜单自建的节点上使用。
	private const string OpenerMetaKey = "hextech_opener";
	private const string ClosingMetaKey = "hextech_closing";
	private const string FocusScrollMetaKey = "hextech_focus_scroll";

	// 条目 PoolKey:我方符文用海克斯池键,敌方海克斯与锻造器各用一个固定键。
	private const string EnemyPoolKey = "ENEMY";
	private const string ForgePoolKey = "FORGE";

	/// <summary>配置菜单的四个页签,数值即页签顺序。</summary>
	private enum ConfigPage
	{
		Counts = 0,
		RunePools = 1,
		Forges = 2,
		Details = 3
	}

	private sealed record RuneConfigEntry(
		string Id,
		RelicModel Relic,
		string Title,
		string RarityText,
		int RarityOrder,
		string PoolKey,
		string TagKey,
		string SourceKey,
		string SourceText);

	/// <summary>一个待加载的符文格子:加载完成后把绑定放进 <see cref="Bindings"/>(该格所属分组的绑定列表)。</summary>
	private sealed record RuneConfigLoadTarget(
		RuneConfigEntry Entry,
		Container Grid,
		HashSet<string> PendingDisabledIds,
		List<RuneIconBinding> Bindings);

	/// <summary>按海克斯池筛选时重排的一块网格:来源小标题与它下面的格子(格子节点复用,不重建图标)。</summary>
	private sealed record FilterableRuneGrid(
		VBoxContainer Grid,
		Label? SourceHeader,
		IReadOnlyList<(RuneConfigEntry Entry, CenterContainer Slot)> Slots);

	/// <summary>一张稀有度卡片:筛选后没有可见格子时整张隐藏。</summary>
	private sealed record FilterableRarityCard(
		PanelContainer Card,
		IReadOnlyList<FilterableRuneGrid> Grids);

	private sealed record RuneIconBinding(
		string Id,
		Control Root,
		Control Holder,
		Label Title);

	private sealed record NumericValueBinding(
		Func<string> GetText,
		Label Number);

	private sealed record BooleanValueBinding(
		Func<bool> GetValue,
		BaseButton Toggle,
		Action<bool>? ApplyVisual = null);

	/// <summary>
	/// 配置界面可开关的条目 ID 集合,打开菜单时按网格实际列出的条目算一次;
	/// 页脚计数、稀有度角标与社区配置摘要共用这一口径(禁用集合里不在此列的 ID 不计入)。
	/// </summary>
	private sealed record ConfigPoolIds(
		IReadOnlySet<string> Player,
		IReadOnlySet<string> Enemy,
		IReadOnlySet<string> Forge)
	{
		internal static ConfigPoolIds FromEntries(
			IEnumerable<RuneConfigEntry> playerEntries,
			IEnumerable<RuneConfigEntry> enemyEntries,
			IEnumerable<RuneConfigEntry> forgeEntries)
		{
			return new ConfigPoolIds(ToIdSet(playerEntries), ToIdSet(enemyEntries), ToIdSet(forgeEntries));
		}

		internal static int CountEnabled(IReadOnlySet<string> ids, IEnumerable<string> disabledIds)
		{
			return Math.Max(0, ids.Count - disabledIds.Count(ids.Contains));
		}

		private static HashSet<string> ToIdSet(IEnumerable<RuneConfigEntry> entries)
		{
			return entries.Select(static entry => entry.Id).ToHashSet(StringComparer.Ordinal);
		}
	}

	/// <summary>
	/// 一次打开的配置菜单的共享状态:编辑态、条目、各类控件绑定、页脚摘要与布局模式。
	/// 集合在菜单生命周期内只就地增改(绑定持有同一实例)。
	/// </summary>
	private sealed record ConfigMenuContext(
		HextechControllerOverlay Overlay,
		PendingConfig Pending,
		IReadOnlyList<RuneConfigEntry> PlayerEntries,
		IReadOnlyList<RuneConfigEntry> EnemyEntries,
		IReadOnlyList<RuneConfigEntry> ForgeEntries,
		ConfigPoolIds PoolIds,
		Label Summary,
		bool CompactLayout)
	{
		internal List<NumericValueBinding> NumericBindings { get; } = [];

		internal List<BooleanValueBinding> BooleanBindings { get; } = [];

		internal List<RuneIconBinding> PlayerIconBindings { get; } = [];

		internal List<RuneIconBinding> EnemyIconBindings { get; } = [];

		internal List<RuneIconBinding> ForgeIconBindings { get; } = [];

		internal List<RuneConfigLoadTarget> LoadTargets { get; } = [];

		internal List<Action> BadgeRefreshers { get; } = [];

		internal ConfigPage SelectedPage { get; set; } = ConfigPage.Counts;

		internal Button? GodslayerTrialExitButton { get; set; }

		/// <summary>刷新页脚摘要与各稀有度分组的启用角标。</summary>
		internal void UpdateSummary()
		{
			SetLabelText(Summary, BuildSummaryText(SelectedPage, PoolIds, Pending));
			foreach (Action refresh in BadgeRefreshers)
			{
				refresh();
			}
		}

		/// <summary>编辑态被整体改写(重置/导入)后,把所有控件刷回编辑态。</summary>
		internal void RefreshAllControls()
		{
			UpdateNumericLabels(NumericBindings);
			UpdateBooleanToggles(BooleanBindings);
			UpdateAllRuneIcons(PlayerIconBindings, Pending.DisabledPlayerRuneIds);
			UpdateAllRuneIcons(EnemyIconBindings, Pending.DisabledMonsterHexIds);
			UpdateAllRuneIcons(ForgeIconBindings, Pending.DisabledForgeIds);
		}

		/// <summary>在页脚摘要行显示一条临时提示(下一次刷新摘要时被覆盖)。</summary>
		internal void ShowSummaryNotice(string text)
		{
			UpdateSummary();
			SetLabelText(Summary, text);
		}
	}

	private sealed record RuneConfigOverlayState(
		ConfigMenuContext Context,
		Control InitialFocus,
		IReadOnlyList<Button> TabButtons);
}
