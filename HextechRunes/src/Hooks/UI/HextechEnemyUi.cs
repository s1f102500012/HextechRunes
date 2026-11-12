using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.sts2.Core.Nodes.TopBar;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechEnemyUi
{
	private const string EnemyHexRootName = "HextechEnemyHexStrip";
	private const string EnemyHexPanelName = "HextechEnemyHexPanel";
	private const string EnemyHexIconsName = "HextechEnemyHexIcons";
	private const string EnemyHexHolderNamePrefix = "EnemyHex-";
	private const int EnemyHexSeparation = 2;
	private const float EnemyHexScale = 0.72f;

	// 原版 NTopBar._modifiersContainer(0.107.1 / 0.111.0);缺失时由 TryGetField 进启动摘要,顶栏海克斯整体停用。
	private static readonly FieldInfo? ModifiersContainerField = TryGetField(
		typeof(NTopBar),
		"_modifiersContainer",
		BindingFlags.Instance | BindingFlags.NonPublic);

	// 原版 NTopBarModifier._modifier(同上版本)。
	private static readonly FieldInfo? TopBarModifierModelField = TryGetField(
		typeof(NTopBarModifier),
		"_modifier",
		BindingFlags.Instance | BindingFlags.NonPublic);

	public static void Refresh(HextechMayhemModifier modifier)
	{
		// 纯表现层硬保证:本方法被多个 lockstep 同步钩子(BeforeCombatStart 等)调用,
		// 任何 UI/资源/节点异常都绝不能冒泡进同步路径——否则单端中断、与另一端命令流分叉被踢。
		// 模型钩子不一定跑在主线程,节点读写一律在主线程执行;已在主线程时同步刷新,保持调用方看到的先后顺序。
		try
		{
			if (NGame.IsMainThread())
			{
				RefreshOnMainThread(modifier);
			}
			else
			{
				Callable.From(() => RefreshOnMainThread(modifier)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			LogRefreshFailure(ex);
		}
	}

	private static void RefreshOnMainThread(HextechMayhemModifier modifier)
	{
		try
		{
			RefreshInternal(modifier);
		}
		catch (Exception ex)
		{
			LogRefreshFailure(ex);
		}
	}

	private static void LogRefreshFailure(Exception ex)
	{
		HextechLog.Warn("Mayhem", $"EnemyUi.Refresh suppressed (UI-only failure, multiplayer sync protected): {ex}");
	}

	private static void RefreshInternal(HextechMayhemModifier modifier)
	{
		Control? container = GetModifiersContainer();
		if (container == null)
		{
			return;
		}

		HideMayhemModifierBadge(container);

		IReadOnlyList<MonsterHexKind> activeHexes = modifier.GetActiveMonsterHexes();

		// 折叠模式将敌方海克斯收进独立按钮和展开面板，避免大量图标挤出顶栏。
		if (HextechUiPreferences.CollapseEnemyHexes)
		{
			RemoveAllEnemyHexStrips(container);
			UpdateContainerVisibility(container);
			// 折叠面板按获得批次分行。阶段序号跨额外幕与无尽轮次单调递增，不能再按三幕配置数组截断。
			IReadOnlyList<IReadOnlyList<MonsterHexKind>> hexRows = modifier.GetMonsterHexRows();
			HextechEnemyHexCollapseView.Show(hexRows, ComputeReservedColumns(modifier, hexRows));
			return;
		}

		// 平铺模式先移除折叠视图，避免两套界面同时存在。
		HextechEnemyHexCollapseView.Remove();

		if (activeHexes.Count == 0)
		{
			RemoveAllEnemyHexStrips(container);
			UpdateContainerVisibility(container);
			return;
		}

		HBoxContainer strip = GetOrCreateStrip(container);
		if (!IsStripCurrent(strip, activeHexes))
		{
			RebuildStrip(strip, activeHexes);
		}

		UpdateContainerVisibility(container);
	}

	public static bool IsTopBarReady()
	{
		return GetModifiersContainer() != null;
	}

	public static void Clear()
	{
		HextechEnemyHexCollapseView.Remove();

		Control? container = GetModifiersContainer();
		if (container == null)
		{
			return;
		}

		RemoveAllEnemyHexStrips(container);
		HideMayhemModifierBadge(container);
		UpdateContainerVisibility(container);
	}

	// 深色底保留列数 = 各幕海克斯数量的最大值(既看配置每幕数量,也兜住实际行长),钳到 [1,6]。
	private static int ComputeReservedColumns(HextechMayhemModifier modifier, IReadOnlyList<IReadOnlyList<MonsterHexKind>> rows)
	{
		int max = 1;
		foreach (int count in modifier.EnemyHexCountsByAct)
		{
			max = Math.Max(max, count);
		}

		foreach (IReadOnlyList<MonsterHexKind> row in rows)
		{
			max = Math.Max(max, row.Count);
		}

		return Math.Clamp(max, 1, 6);
	}

	public static void HideMayhemModifierBadge()
	{
		if (GetModifiersContainer() is { } container)
		{
			HideMayhemModifierBadge(container);
		}
	}

	private static void HideMayhemModifierBadge(Control container)
	{
		foreach (Node child in container.GetChildren())
		{
			// GetModifiersContainer 非空即两个反射字段都已解析。
			if (child is NTopBarModifier topBarModifier
				&& TopBarModifierModelField!.GetValue(topBarModifier) is HextechMayhemModifier)
			{
				topBarModifier.QueueFree();
			}
		}
	}

	// 两个私有字段缺一即停用顶栏海克斯:缺失已由 TryGetField 记入启动摘要,这里不再重复告警。
	private static Control? GetModifiersContainer()
	{
		if (ModifiersContainerField == null || TopBarModifierModelField == null)
		{
			return null;
		}

		NTopBar? topBar = NRun.Instance?.GlobalUi?.TopBar;
		return topBar == null ? null : ModifiersContainerField.GetValue(topBar) as Control;
	}

	private static HBoxContainer GetOrCreateStrip(Control container)
	{
		// 海克斯条只由下面整体建出,同名兄弟节点至多一个(移除时先 RemoveChild 再释放)。
		if (container.GetNodeOrNull<Control>(EnemyHexRootName) is { } existingRoot)
		{
			// 原版之后追加的修饰图标会排到海克斯条后面,把海克斯条挪回最右。
			container.MoveChild(existingRoot, container.GetChildCount() - 1);
			return existingRoot.GetNode<HBoxContainer>($"{EnemyHexPanelName}/{EnemyHexIconsName}");
		}

		MarginContainer root = new()
		{
			Name = EnemyHexRootName,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		PanelContainer panel = new()
		{
			Name = EnemyHexPanelName,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		panel.AddThemeStyleboxOverride("panel", CreateEnemyHexStripStyle());

		HBoxContainer strip = new()
		{
			Name = EnemyHexIconsName,
			Alignment = BoxContainer.AlignmentMode.Begin,
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		strip.AddThemeConstantOverride("separation", EnemyHexSeparation);

		panel.AddChild(strip);
		root.AddChild(panel);
		container.AddChild(root);
		return strip;
	}

	private static StyleBoxFlat CreateEnemyHexStripStyle()
	{
		StyleBoxFlat style = new()
		{
			BgColor = new Color(0.035f, 0.045f, 0.07f, 0.72f),
			BorderColor = new Color(0.36f, 0.42f, 0.52f, 0.24f)
		};
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(10);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 0;
		return style;
	}

	private static void RebuildStrip(HBoxContainer strip, IReadOnlyList<MonsterHexKind> activeHexes)
	{
		// 图标的提示由 CreateEnemyHexHolder 挂的 TreeExiting 在 RemoveChild 时收起。
		foreach (Node child in strip.GetChildren())
		{
			strip.RemoveChild(child);
			child.QueueFree();
		}

		foreach (MonsterHexKind hex in activeHexes)
		{
			try
			{
				Control holder = CreateEnemyHexHolder(hex);
				strip.AddChild(holder);
			}
			catch (Exception ex)
			{
				// 单个图标解析/实例化失败只跳过该图标,不影响其余图标,更不冒泡进同步路径。
				HextechLog.Warn("Mayhem", $"EnemyUi: skipped enemy hex icon {hex}: {ex.Message}");
			}
		}
	}

	private static void RemoveAllEnemyHexStrips(Control container)
	{
		foreach (Node child in container.GetChildren())
		{
			if (child.Name == EnemyHexRootName)
			{
				container.RemoveChild(child);
				child.QueueFree();
			}
		}
	}

	private static void UpdateContainerVisibility(Control container)
	{
		container.Visible = container.GetChildren().Any(static child => !child.IsQueuedForDeletion());
	}

	internal static Control CreateEnemyHexHolder(MonsterHexKind hex)
	{
		RelicModel relic = MonsterHexCatalog.GetIconRelicForMonsterHex(hex).ToMutable();
		NRelicBasicHolder holder = NRelicBasicHolder.Create(relic)
			?? throw new InvalidOperationException("Failed to create top bar enemy hex holder.");
		holder.Name = $"{EnemyHexHolderNamePrefix}{hex}";
		holder.Scale = Vector2.One * EnemyHexScale;
		holder.MouseFilter = Control.MouseFilterEnum.Stop;
		holder.TreeExiting += () => NHoverTipSet.Remove(holder);
		return holder;
	}

	private static bool TryGetHexFromHolder(Control holder, out MonsterHexKind hex)
	{
		string name = holder.Name.ToString();
		if (name.StartsWith(EnemyHexHolderNamePrefix, StringComparison.Ordinal)
			&& Enum.TryParse(name[EnemyHexHolderNamePrefix.Length..], out hex))
		{
			return true;
		}

		hex = default;
		return false;
	}

	private static void ShowEnemyHexHoverTip(Control holder, MonsterHexKind hex)
	{
		NHoverTipSet.Remove(holder);
		NHoverTipSet? hoverTipSet = NHoverTipSet.CreateAndShow(holder, MonsterHexCatalog.GetEnemyHexHoverTips(hex));
		hoverTipSet?.SetAlignment(holder, HoverTip.GetHoverTipAlignment(holder));
	}

	private static bool IsStripCurrent(HBoxContainer strip, IReadOnlyList<MonsterHexKind> activeHexes)
	{
		Godot.Collections.Array<Node> children = strip.GetChildren();
		if (children.Count != activeHexes.Count)
		{
			return false;
		}

		for (int i = 0; i < activeHexes.Count; i++)
		{
			if (children[i] is not Control control || !TryGetHexFromHolder(control, out MonsterHexKind hex) || hex != activeHexes[i])
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// 顶栏/折叠面板里的敌方海克斯图标(节点名以 <c>EnemyHex-</c> 开头、由本模组创建)聚焦时,展示敌方海克斯的提示集合。
	/// </summary>
	/// <remarks>
	/// 跳过型前缀:原版 <c>NRelicBasicHolder.OnFocus</c> 固定展示 <c>_relic.Model.HoverTips</c>(图标遗物自己的提示),
	/// 没有 Hook 或虚成员能换成敌方海克斯的提示;被跳过的原版步骤只有图标放大 tween 与提示展示。
	/// 激活条件:仅本模组的敌方海克斯图标节点,其它遗物持有者完全交给原版。
	/// 版本:0.107.1 / 0.111.0 原方法一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NRelicBasicHolder), "OnFocus", new Type[0])]
	[HextechPatch("ui.enemy-hex.holder-focus", "敌方海克斯顶栏悬浮")]
	private static class HolderFocusPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NRelicBasicHolder __instance)
		{
			if (!TryGetHexFromHolder(__instance, out MonsterHexKind hex))
			{
				return true;
			}

			ShowEnemyHexHoverTip(__instance, hex);
			return false;
		}
	}

	/// <summary>敌方海克斯图标失焦时收起提示。</summary>
	/// <remarks>
	/// 跳过型前缀:与 <see cref="HolderFocusPatch"/> 成对——聚焦时跳过了原版放大 tween,失焦时也跳过原版的缩回 tween,
	/// 只保留原版同样会做的 <c>NHoverTipSet.Remove</c>。激活条件、版本与优先级同上。
	/// </remarks>
	[HarmonyPatch(typeof(NRelicBasicHolder), "OnUnfocus", new Type[0])]
	[HextechPatch("ui.enemy-hex.holder-unfocus", "敌方海克斯顶栏悬浮")]
	private static class HolderUnfocusPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NRelicBasicHolder __instance)
		{
			if (!TryGetHexFromHolder(__instance, out _))
			{
				return true;
			}

			NHoverTipSet.Remove(__instance);
			return false;
		}
	}
}
