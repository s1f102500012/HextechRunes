using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Helpers;

namespace HextechRunes;

public sealed class SignalNoiseConsoleCmd : AbstractConsoleCmd
{
	internal const string Name = "signalnoise";

	// 设了这个环境变量才保留指令；否则建好控制台后就摘掉，补全和帮助列表里都看不到。
	internal const string ConsoleEnvironmentVariable = "HEXTECH_SIGNAL_NOISE_CONSOLE";

	public override string CmdName => Name;

	public override string Args => "[1-12] [han|latin] | clear | reset | status | trial on|off | godslayer | finale";

	public override string Description => "调试选择界面的信号噪声效果。";

	// 纯本地表现，测试命令也只影响执行命令的客户端。
	public override bool IsNetworked => false;

	public override CmdResult Process(Player? issuingPlayer, string[] args)
	{
		string first = args.Length == 0 ? string.Empty : args[0].Trim().ToLowerInvariant();
		switch (first)
		{
			case "clear":
				HextechSignalNoise.ClearPendingDebug();
				return new CmdResult(success: true, "已取消待生效的信号噪声。");
			case "reset":
				HextechSignalNoise.ResetProgressForDebug();
				return new CmdResult(success: true, "已清空本机的信号噪声记录。");
			case "status":
				return new CmdResult(success: true, HextechSignalNoise.DescribeProgress());
			case "trial" when args.Length == 2 && args[1].Trim().ToLowerInvariant() is "on" or "off":
				bool enable = args[1].Trim().ToLowerInvariant() == "on";
				HextechRuneConfiguration.SetGodslayerTrialEnabled(enable);
				return new CmdResult(success: true, enable ? "已开启弑神试炼（下一局单人开局生效）。" : "已关闭弑神试炼。");
			case "godslayer":
				return GrantGodslayer(issuingPlayer);
			case "finale":
				TaskHelper.RunSafely(HextechGodslayerFinale.PlayAsync());
				return new CmdResult(success: true, "播放弑神终局演出（只看演出，不触发结算）。");
		}

		int? fragment = null;
		bool? latin = null;
		foreach (string raw in args)
		{
			string arg = raw.Trim().ToLowerInvariant();
			if (int.TryParse(arg, out int number) && number is >= 1 and <= 12)
			{
				fragment = number - 1;
			}
			else if (arg is "han" or "zh")
			{
				latin = false;
			}
			else if (arg is "latin" or "en")
			{
				latin = true;
			}
			else
			{
				return new CmdResult(success: false, GetUsage());
			}
		}

		bool appliedNow = HextechSignalNoise.ForceForDebug(new HextechSignalNoise.DebugRequest(fragment, latin));
		return new CmdResult(
			success: true,
			appliedNow ? "已在当前海克斯选择界面生成信号噪声。" : "下一次打开海克斯选择界面时生成信号噪声。");
	}

	public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
	{
		if (args.Length <= 1)
		{
			return CompleteArgument(
				[ "1", "7", "12", "han", "latin", "clear", "reset", "status", "trial", "godslayer", "finale" ],
				Array.Empty<string>(),
				args.FirstOrDefault() ?? "");
		}

		return base.GetArgumentCompletions(player, args);
	}

	// 直接把弑神给到本局；对话替换只在试炼局生效，测结局要先 trial on 再开新局。
	private static CmdResult GrantGodslayer(Player? player)
	{
		if (player == null)
		{
			return new CmdResult(success: false, "需要在对局中使用。");
		}

		if (player.GetRelic<GodslayerRune>() != null)
		{
			return new CmdResult(success: true, "已经持有弑神。");
		}

		TaskHelper.RunSafely(RelicCmd.Obtain(ModelDb.Relic<GodslayerRune>().ToMutable(), player));
		return new CmdResult(success: true, "已获得弑神（只有试炼局里才会替换建筑师对话）。");
	}

	private static string GetUsage()
	{
		return "用法: signalnoise [1-12] [han|latin] | clear | reset | status | trial on|off | godslayer | finale";
	}
}

// 模组模式下原版默认开放全部调试指令（DevConsole 构造时按 DebugOnly 过滤，管不到这里），所以在构造后单独摘掉。
[HarmonyPatch(typeof(DevConsole), MethodType.Constructor, typeof(bool))]
[HextechPatch("console.signal-noise.hide", "信号噪声调试指令")]
internal static class SignalNoiseConsoleHidePatch
{
	private static readonly FieldInfo? CommandsField = HextechHookReflection.TryGetField(typeof(DevConsole), "_commands");

	[HarmonyPrepare]
	private static bool Prepare() => CommandsField != null;

	[HarmonyPostfix]
	private static void Postfix(DevConsole __instance)
	{
		if (System.Environment.GetEnvironmentVariable(SignalNoiseConsoleCmd.ConsoleEnvironmentVariable) != "1"
			&& CommandsField!.GetValue(__instance) is Dictionary<string, AbstractConsoleCmd> commands)
		{
			commands.Remove(SignalNoiseConsoleCmd.Name);
		}
	}
}
