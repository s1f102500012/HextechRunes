using Godot;

namespace HextechRunes;

/// <summary>
/// 一次海克斯选择的对外视图，交给 <see cref="IHextechSelectionPresenter"/>。所有操作都走本体选择界面原有的按钮逻辑，
/// 因此重随次数、敌方海克斯权限、联机同步与存档的行为与本体界面完全一致。
/// 下标 slot 对应 <see cref="Options"/> 或敌方海克斯的位置；越界或当前不允许的操作返回 false / null，不抛异常。
/// </summary>
public sealed class HextechSelectionSession
{
	private readonly HextechRuneSelectionScreen _screen;

	internal HextechSelectionSession(HextechRuneSelectionScreen screen)
	{
		_screen = screen;
	}

	/// <summary>本体的选择界面节点（不可见、不构建界面）。离开场景树即表示本次选择结束。</summary>
	public Control Screen => _screen;

	/// <summary>玩家的候选海克斯。重随后对应位置会换成新的实例。</summary>
	public IReadOnlyList<RelicModel> Options => _screen.PresenterOptions;

	/// <summary>本次选择的品级（开选时玩家候选的品级；只有敌方海克斯时取敌方海克斯的品级）。</summary>
	public HextechRarityTier Rarity => _screen.PresenterRarity;

	/// <summary>某个候选自己的品级。金色重随后，被重随的位置可能比 <see cref="Rarity"/> 高一级。</summary>
	public HextechRarityTier? GetOptionRarity(int slot) => _screen.PresenterOptionRarity(slot);

	/// <summary>金色重随生效中：下一次重随会换成 <see cref="GoldenRerollRarity"/> 品级的候选，用掉后变回 false。</summary>
	public bool IsGoldenRerollActive => _screen.PresenterGoldenRerollActive;

	/// <summary>本次选择可能出现的金色重随品级；本次不可能出现金色重随时为 null。</summary>
	public HextechRarityTier? GoldenRerollRarity => _screen.PresenterGoldenRerollRarity;

	/// <summary>
	/// 手柄 / 键盘方向导航：<paramref name="root"/> 下的节点获得焦点时，本体界面不会把焦点抢回；
	/// 需要默认焦点时（进入手柄模式、地图预览结束等）聚焦 <paramref name="defaultFocus"/>。
	/// 本体界面接管时没有可聚焦的节点，表现层不设置时焦点保持不动。
	/// </summary>
	public void SetControllerFocus(Control? root, Control? defaultFocus) => _screen.PresenterSetControllerFocus(root, defaultFocus);

	/// <summary>已经选定（或已确认敌方海克斯），之后的操作都会被忽略。</summary>
	public bool IsLocked => _screen.PresenterLocked;

	/// <summary>本次没有玩家候选，只需确认敌方海克斯（<see cref="ConfirmEnemyHexes"/>）。</summary>
	public bool IsEnemyOnly => _screen.PresenterEnemyOnly;

	/// <summary>本端能否调整敌方海克斯。联机时只有权威玩家可以，其他人只能查看。</summary>
	public bool CanAdjustEnemyHexes => _screen.PresenterEnemyControls;

	public int EnemyHexCount => _screen.PresenterEnemyHexCount;

	/// <summary>
	/// 敌方海克斯在其他端被调整（联机时权威玩家的重随、移除或恢复同步过来）。本端自己的操作不会触发。
	/// 在主线程上触发。
	/// </summary>
	public event Action? EnemyHexesChangedRemotely
	{
		add => _screen.PresenterEnemyHexesChanged += value;
		remove => _screen.PresenterEnemyHexesChanged -= value;
	}

	/// <summary>选择界面离开场景树（已选定、已取消或场景重建）。</summary>
	public event Action? Closed
	{
		add => _screen.TreeExited += value;
		remove => _screen.TreeExited -= value;
	}

	public bool CanRerollOption(int slot) => _screen.PresenterCanRerollOption(slot);

	/// <summary>重随玩家候选。成功时返回新的候选，否则返回 null。</summary>
	public RelicModel? RerollOption(int slot) => _screen.PresenterRerollOption(slot);

	/// <summary>候选卡在本体界面上的附加说明（例如会给予的卡牌），没有则为 null。</summary>
	public string? GetOptionFooter(int slot) => _screen.PresenterOptionFooter(slot);

	/// <summary>选定玩家候选，结束本次选择。</summary>
	public void Choose(int slot) => _screen.PresenterChoose(slot);

	/// <summary>没有玩家候选时，确认敌方海克斯并结束本次选择。</summary>
	public void ConfirmEnemyHexes() => _screen.PresenterConfirmEnemyOnly();

	/// <summary>
	/// 敌方海克斯的显示信息。已被移除的位置仍返回原来的海克斯（<see cref="HextechEnemyHexInfo.Removed"/> 为 true），
	/// 以便显示并恢复。位置为空时返回 null。
	/// </summary>
	public HextechEnemyHexInfo? GetEnemyHex(int slot) => _screen.PresenterEnemyHex(slot);

	public bool CanRerollEnemyHex(int slot) => _screen.PresenterCanRerollEnemyHex(slot);

	/// <summary>重随敌方海克斯。成功时返回新的信息，否则返回 null。</summary>
	public HextechEnemyHexInfo? RerollEnemyHex(int slot) => _screen.PresenterRerollEnemyHex(slot);

	/// <summary>移除敌方海克斯，已移除时恢复。返回状态是否改变。</summary>
	public bool ToggleEnemyHexRemoved(int slot) => _screen.PresenterToggleEnemyHex(slot);
}

/// <summary>敌方海克斯的显示信息，与本体界面上使用的文字、图标和悬停提示一致。</summary>
public sealed class HextechEnemyHexInfo
{
	internal HextechEnemyHexInfo(string id, string title, string description, Texture2D? icon, HextechRarityTier rarity, IReadOnlyList<IHoverTip> extraHoverTips, bool removed)
	{
		Id = id;
		Title = title;
		Description = description;
		Icon = icon;
		Rarity = rarity;
		ExtraHoverTips = extraHoverTips;
		Removed = removed;
	}

	/// <summary>稳定的标识（枚举名），可用于本地化以外的映射，例如自定义图标。</summary>
	public string Id { get; }

	public string Title { get; }

	/// <summary>已格式化的描述（含 BBCode）。</summary>
	public string Description { get; }

	public Texture2D? Icon { get; }

	public HextechRarityTier Rarity { get; }

	/// <summary>描述里提到的关键词、卡牌等附加悬停提示（不含海克斯本身）。</summary>
	public IReadOnlyList<IHoverTip> ExtraHoverTips { get; }

	public bool Removed { get; }
}

/// <summary>联机时各玩家本幕海克斯选择的完成情况，供表现层显示“谁已选好”。</summary>
public static class HextechSelectionProgress
{
	private static readonly HashSet<ulong> Completed = new();

	/// <summary>某名玩家完成选择、或新一轮选择开始时触发。可能在非主线程触发，更新界面前请自行切回主线程。</summary>
	public static event Action? Changed;

	public static bool HasCompleted(ulong playerNetId)
	{
		lock (Completed)
		{
			return Completed.Contains(playerNetId);
		}
	}

	internal static void Reset(IEnumerable<ulong> alreadyCompleted)
	{
		lock (Completed)
		{
			Completed.Clear();
			Completed.UnionWith(alreadyCompleted);
		}

		Changed?.Invoke();
	}

	internal static void MarkCompleted(ulong playerNetId)
	{
		lock (Completed)
		{
			if (!Completed.Add(playerNetId))
			{
				return;
			}
		}

		Changed?.Invoke();
	}
}
