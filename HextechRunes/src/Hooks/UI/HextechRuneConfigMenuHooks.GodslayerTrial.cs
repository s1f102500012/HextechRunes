using Godot;

namespace HextechRunes;

// 弑神试炼的开关只在这里改：口令开启，按钮手动退出；不进编辑中的配置，所以“保存/重置/导入”都不会动它。
internal static partial class HextechRuneConfigMenuHooks
{
	private static Button CreateGodslayerTrialExitButton(ConfigMenuContext context)
	{
		Button button = CreateActionButton(L("HEXTECH_GODSLAYER_TRIAL_EXIT"), () => ExitGodslayerTrial(context), context.CompactLayout);
		button.Visible = HextechRuneConfiguration.IsGodslayerTrialEnabled();
		context.GodslayerTrialExitButton = button;
		return button;
	}

	private static void EnterGodslayerTrial(ConfigMenuContext context)
	{
		HextechRuneConfiguration.SetGodslayerTrialEnabled(true);
		SetGodslayerTrialExitVisible(context, visible: true);
		context.ShowSummaryNotice(L("HEXTECH_GODSLAYER_TRIAL_ON"));
	}

	private static void ExitGodslayerTrial(ConfigMenuContext context)
	{
		HextechRuneConfiguration.SetGodslayerTrialEnabled(false);
		SetGodslayerTrialExitVisible(context, visible: false);
		context.ShowSummaryNotice(L("HEXTECH_GODSLAYER_TRIAL_OFF"));
	}

	private static void SetGodslayerTrialExitVisible(ConfigMenuContext context, bool visible)
	{
		if (context.GodslayerTrialExitButton is { } button && GodotObject.IsInstanceValid(button))
		{
			button.Visible = visible;
		}
	}
}
