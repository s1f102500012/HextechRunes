using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace HextechRunes;

/// <summary>
/// 战斗界面「隐藏遗物」开关:勾选后隐藏遗物栏与联机玩家状态栏,并压掉遗物闪光特效。
/// 开关本身只在配置菜单打开「显示隐藏遗物开关」后出现;偏好与勾选状态存在 <see cref="HextechUiPreferences"/>。
/// </summary>
internal static partial class HextechRelicVisibilityHooks
{
	private const string ToggleRootNodeName = "HextechHideRelicsToggleRoot";
	private const string ToggleColumnNodeName = "HextechHideRelicsToggleColumn";
	private const string ToggleBoxNodeName = "HextechHideRelicsToggleBox";
	private const string ToggleVisualsNodeName = "TickboxVisuals";
	private const string ToggleButtonNodeName = "HextechHideRelicsToggleButton";
	private const string ToggleLabelNodeName = "HextechHideRelicsToggleLabel";
	private const string PositionTimerNodeName = "HextechHideRelicsTogglePositionTimer";
	private const string TickboxVisualScenePath = "res://scenes/ui/tickbox.tscn";
	private static readonly Vector2 ToggleRootSize = new(72f, 80f);
	private static readonly Vector2 ToggleBoxSize = new(64f, 64f);
	private const float DrawPileGap = 10f;
	private const float BottomFallbackPadding = 34f;
	private const float LeftFallbackPadding = 150f;

	private static bool _multiplayerStateHiddenByToggle;
	private static NDrawPileButton? _drawPileAnchor;

	private static void OnGlobalUiInitialized(NGlobalUi globalUiNode)
	{
		try
		{
			InstallToggle(globalUiNode);
			ApplyHiddenState(globalUiNode);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Relic visibility toggle install failed: {ex.Message}");
		}
	}

	private static void OnCombatUiShown(NCombatUi combatUi)
	{
		try
		{
			_drawPileAnchor = combatUi.DrawPile;
			NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
			if (globalUi == null || !GodotObject.IsInstanceValid(globalUi))
			{
				return;
			}

			InstallToggle(globalUi);
			ApplyHiddenState(globalUi);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Relic visibility toggle refresh failed: {ex.Message}");
		}
	}

	private static void OnCombatUiHidden()
	{
		_drawPileAnchor = null;
		RefreshToggleRootPosition();
	}

	private static void OnRelicInventoryRefreshed(NRelicInventory inventory)
	{
		try
		{
			ApplyHiddenState(inventory);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Relic visibility refresh failed: {ex.Message}");
		}
	}

	private static void InstallToggle(NGlobalUi globalUi)
	{
		if (!HextechUiPreferences.ShowHiddenRelicsToggle)
		{
			RemoveToggleRoot(globalUi);
			return;
		}

		// 开关根节点只由 CreateToggleRoot 整体建出并挂在 GlobalUi 末尾,找到了就一定带着按钮。
		Control? root = FindToggleRoot(globalUi);
		if (root == null)
		{
			root = CreateToggleRoot();
			globalUi.AddChild(root);
		}

		Button button = root.GetNode<Button>($"{ToggleColumnNodeName}/{ToggleBoxNodeName}/{ToggleButtonNodeName}");
		bool hideRelics = HextechUiPreferences.HideRelics;
		button.SetPressedNoSignal(hideRelics);
		UpdateToggleVisualState(root, hideRelics);
		root.Visible = true;
		EnsurePositionTimer(globalUi, root);
		PositionToggleRoot(root);
		Callable.From(() => PositionToggleRoot(root)).CallDeferred();
	}

	private static void OnToggleChanged(bool hideUi)
	{
		HextechUiPreferences.SetHideRelics(hideUi);
		NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
		if (globalUi?.GetNodeOrNull<Control>(ToggleRootNodeName) is { } root && GodotObject.IsInstanceValid(root))
		{
			UpdateToggleVisualState(root, hideUi);
		}

		ApplyHiddenState(globalUi);
	}

	private static void RemoveToggleRoot(NGlobalUi globalUi)
	{
		if (FindToggleRoot(globalUi) is { } root)
		{
			root.GetParent()?.RemoveChild(root);
			root.QueueFree();
		}
	}

	private static void ApplyHiddenState(NRelicInventory? inventory)
	{
		if (inventory == null || !GodotObject.IsInstanceValid(inventory))
		{
			return;
		}

		bool showRelics = !ShouldHideUi();
		// RelicNodes 只含在场的持有者:原版移除遗物时先从列表摘掉再释放节点。
		foreach (NRelicInventoryHolder holder in inventory.RelicNodes)
		{
			holder.Visible = showRelics;
		}
	}

	private static void ApplyHiddenState(NGlobalUi? globalUi)
	{
		if (globalUi == null || !GodotObject.IsInstanceValid(globalUi))
		{
			return;
		}

		ApplyHiddenState(globalUi.RelicInventory);
		ApplyMultiplayerStateVisibility(globalUi);
	}

	private static void ApplyMultiplayerStateVisibility(NGlobalUi globalUi)
	{
		if (globalUi.MultiplayerPlayerContainer == null
			|| !GodotObject.IsInstanceValid(globalUi.MultiplayerPlayerContainer))
		{
			return;
		}

		if (ShouldHideUi())
		{
			globalUi.MultiplayerPlayerContainer.HideImmediately();
			_multiplayerStateHiddenByToggle = true;
		}
		else if (_multiplayerStateHiddenByToggle)
		{
			globalUi.MultiplayerPlayerContainer.ShowImmediately();
			_multiplayerStateHiddenByToggle = false;
		}
	}

	private static bool ShouldHideUi()
	{
		return HextechUiPreferences.ShowHiddenRelicsToggle && HextechUiPreferences.HideRelics;
	}

	private const string Feature = "遗物栏隐藏开关";

	[HarmonyPatch(typeof(NGlobalUi), nameof(NGlobalUi.Initialize), typeof(RunState))]
	[HextechPatch("ui.relic-visibility.global-ui-init", Feature)]
	private static class GlobalUiInitializePatch
	{
		[HarmonyPostfix]
		private static void Postfix(NGlobalUi __instance) => OnGlobalUiInitialized(__instance);
	}

	[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready), new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-ready", Feature)]
	private static class CombatUiReadyPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCombatUi __instance) => OnCombatUiShown(__instance);
	}

	[HarmonyPatch(typeof(NCombatUi), "AnimIn", new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-anim-in", Feature)]
	private static class CombatUiAnimInPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCombatUi __instance) => OnCombatUiShown(__instance);
	}

	[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Enable), new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-enable", Feature)]
	private static class CombatUiEnablePatch
	{
		[HarmonyPostfix]
		private static void Postfix(NCombatUi __instance) => OnCombatUiShown(__instance);
	}

	[HarmonyPatch(typeof(NCombatUi), "AnimOut", new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-anim-out", Feature)]
	private static class CombatUiAnimOutPatch
	{
		[HarmonyPostfix]
		private static void Postfix() => OnCombatUiHidden();
	}

	[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Disable), new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-disable", Feature)]
	private static class CombatUiDisablePatch
	{
		[HarmonyPostfix]
		private static void Postfix() => OnCombatUiHidden();
	}

	[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._ExitTree), new Type[0])]
	[HextechPatch("ui.relic-visibility.combat-ui-exit-tree", Feature)]
	private static class CombatUiExitTreePatch
	{
		[HarmonyPostfix]
		private static void Postfix() => OnCombatUiHidden();
	}

	[HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.Initialize), typeof(RunState))]
	[HextechPatch("ui.relic-visibility.inventory-init", Feature)]
	private static class RelicInventoryInitializePatch
	{
		[HarmonyPostfix]
		private static void Postfix(NRelicInventory __instance) => OnRelicInventoryRefreshed(__instance);
	}

	[HarmonyPatch(typeof(NRelicInventory), "Add", typeof(RelicModel), typeof(bool), typeof(int))]
	[HextechPatch("ui.relic-visibility.inventory-add", Feature)]
	private static class RelicInventoryAddPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NRelicInventory __instance) => OnRelicInventoryRefreshed(__instance);
	}

	[HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.AnimShow), new Type[0])]
	[HextechPatch("ui.relic-visibility.inventory-anim-show", Feature)]
	private static class RelicInventoryAnimShowPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NRelicInventory __instance) => OnRelicInventoryRefreshed(__instance);
	}

	[HarmonyPatch(typeof(NRelicInventory), nameof(NRelicInventory.ShowImmediately), new Type[0])]
	[HextechPatch("ui.relic-visibility.inventory-show-immediately", Feature)]
	private static class RelicInventoryShowImmediatelyPatch
	{
		[HarmonyPostfix]
		private static void Postfix(NRelicInventory __instance) => OnRelicInventoryRefreshed(__instance);
	}

	/// <summary>隐藏遗物时压掉遗物闪光特效。</summary>
	/// <remarks>
	/// 跳过型前缀:原版私有 <c>NRelicInventoryHolder.DoFlash</c> 把闪光粒子实例化进 <c>GlobalUi.AboveTopBarVfxContainer</c>
	/// (不是持有者的子节点),隐藏持有者挡不住它,原版也没有 Hook 或开关能阻止这一次实例化。
	/// 激活条件:玩家打开了「显示隐藏遗物开关」并勾选隐藏(<see cref="ShouldHideUi"/>);未勾选时原样执行原版。
	/// 版本:0.107.1 / 0.111.0 原方法一致,已进原版拷贝守卫;<see cref="Priority.Low"/> 让他人前缀先跑。
	/// </remarks>
	[HarmonyPatch(typeof(NRelicInventoryHolder), "DoFlash", new Type[0])]
	[HextechPatch("ui.relic-visibility.holder-do-flash", Feature)]
	private static class RelicHolderDoFlashPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix() => !ShouldHideUi();
	}
}
