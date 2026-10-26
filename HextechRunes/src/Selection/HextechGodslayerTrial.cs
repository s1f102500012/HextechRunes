using MegaCrit.Sts2.Core.Context;

namespace HextechRunes;

/// <summary>
/// 弑神试炼：口令开启的本机开关。只在单人开局生效，本局按原版对局进行（模组内容关闭），
/// 唯一例外是第一幕的海克斯选择只给“弑神”一个选项；在建筑师面前弑神后自动关闭。联机局忽略开关。
/// </summary>
internal static class HextechGodslayerTrial
{
	private const string LogTag = "GodslayerTrial";

	/// <summary>开局：开关开着且是单人局时，把本局快照改成关闭模组的试炼局。</summary>
	internal static HextechRunConfigurationSnapshot ApplyToNewRun(HextechRunConfigurationSnapshot snapshot)
	{
		if (!HextechRuneConfiguration.IsGodslayerTrialEnabled() || HextechPlayerContextHelper.IsNetworkMultiplayerRun())
		{
			return snapshot;
		}

		HextechLog.Info(LogTag, "New run starts as a trial run.");
		return snapshot with
		{
			ModEnabled = false,
			GodslayerTrial = true
		};
	}

	internal static bool IsTrialRun(HextechMayhemModifier modifier)
	{
		return modifier.GetEffectiveRunConfigurationSnapshot().GodslayerTrial
			&& !HextechPlayerContextHelper.IsNetworkMultiplayerRun();
	}

	/// <summary>试炼局第一幕：本机玩家还没有弑神时，弹出只有它一个选项的选择界面并发放。</summary>
	internal static async Task GrantForFirstActAsync(RunState runState, HextechMayhemModifier modifier, int actIndex)
	{
		if (actIndex != 0
			|| !IsTrialRun(modifier)
			|| LocalContext.GetMe(runState) is not Player player
			|| player.GetRelic<GodslayerRune>() != null)
		{
			return;
		}

		RelicModel godslayer = ModelDb.Relic<GodslayerRune>().ToMutable();
		HextechRuneSelectionScreen screen = await HextechRuneSelectionCoordinator.CreateRuneSelectionScreenAsync([godslayer], monsterHexRelic: null);
		RelicModel selected = (await screen.RelicsSelected()).First();
		await RelicCmd.Obtain(selected, player);
		HextechLog.Info(LogTag, "Godslayer granted for the first act.");
	}

	/// <summary>在建筑师面前弑神后：关闭开关，并在本机记下“已弑神”。</summary>
	internal static void CompleteSlaying()
	{
		HextechRuneConfiguration.SetGodslayerTrialEnabled(false);
		HextechSignalNoise.MarkSlain();
		HextechLog.Info(LogTag, "Trial completed.");
	}
}
