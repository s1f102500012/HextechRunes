using MegaCrit.Sts2.Core.Context;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Events;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

/// <summary>
/// 结局建筑师事件：弑神试炼局里持有弑神时换成本模组的对话，最后一个按钮改名，结尾由玩家出手，之后照常胜利结算。
/// 只认试炼局：普通局或联机局里经控制台、第三方模组拿到的弑神只是一枚无效果遗物，建筑师照常说原版台词。
/// 原版每个玩家各有一份 TheArchitect（未覆写 IsShared），按事件归属玩家自己的遗物判断。
/// 原版先照常挑完对话（随机数照常消耗）再替换；读不到本模组台词（ancients 表没合并上）时保持原版对话。
/// 最后玩家出手后，原版经 RunManager.WinRun 进入结算（0.111.0 由事件直接调用；0.107.1 是私有方法，经幕切换同步后由
/// EnterNextAct 调用）；在它前面插入终局演出，结算与记录本身不动。
/// </summary>
internal static class HextechArchitectGodslayerHooks
{
	private const string LocTable = "ancients";
	private const string KeyPrefix = "HEXTECH_ARCHITECT.talk.";
	private const string GenericSet = "GENERIC";

	private static readonly PropertyInfo? DialogueProperty = TryGetProperty(typeof(TheArchitect), "Dialogue");
	private static readonly MethodInfo? OptionTitleSetter = TryGetPropertySetter(typeof(EventOption), nameof(EventOption.Title));
	private static readonly MethodInfo? RunManagerWinRun = TryGetMethod(typeof(RunManager), "WinRun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly MethodInfo? OptionHistoryNameSetter = TryGetPropertySetter(typeof(EventOption), "HistoryName");

	// 已替换对话的事件 → 最后一个按钮的文字。
	private static readonly ConditionalWeakTable<TheArchitect, LocString> ReplacedEvents = new();

	// 本机弑神后、进入原版结算前要播演出的对局；只认这一局，避免中途退出后串到别的对局。
	private static WeakReference<RunState>? _pendingFinale;

	/// <summary>按角色取一套台词，取不到时用通用套；一行都没有时返回 null。</summary>
	internal static AncientDialogue? BuildDialogue(string characterEntry, out LocString? finalButton)
	{
		finalButton = null;
		string set = LineKey(characterEntry, 0) != null ? characterEntry : GenericSet;
		List<(string Key, AncientDialogueSpeaker Speaker, string Prefix)> lines = [];
		for (int i = 0; LineKey(set, i) is { } line; i++)
		{
			lines.Add(line);
		}

		if (lines.Count == 0)
		{
			return null;
		}

		AncientDialogue dialogue = new(Enumerable.Repeat(string.Empty, lines.Count).ToArray())
		{
			EndAttackers = ArchitectAttackers.Player
		};
		for (int i = 0; i < lines.Count; i++)
		{
			AncientDialogueLine target = dialogue.Lines[i];
			target.LineText = new LocString(LocTable, lines[i].Key);
			target.Speaker = lines[i].Speaker;
			string nextKey = lines[i].Prefix + ".next";
			target.NextButtonText = LocString.Exists(LocTable, nextKey) ? new LocString(LocTable, nextKey) : null;
		}

		finalButton = dialogue.Lines[^1].NextButtonText;
		return dialogue;
	}

	private static (string Key, AncientDialogueSpeaker Speaker, string Prefix)? LineKey(string set, int index)
	{
		string prefix = $"{KeyPrefix}{set}.{index}";
		if (LocString.Exists(LocTable, prefix + ".ancient"))
		{
			return (prefix + ".ancient", AncientDialogueSpeaker.Ancient, prefix);
		}

		if (LocString.Exists(LocTable, prefix + ".char"))
		{
			return (prefix + ".char", AncientDialogueSpeaker.Character, prefix);
		}

		return null;
	}

	[HarmonyPatch(typeof(TheArchitect), "LoadDialogue")]
	[HextechPatch("event.architect.godslayer-dialogue", "弑神")]
	private static class LoadDialoguePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => DialogueProperty != null;

		[HarmonyPostfix]
		private static void Postfix(TheArchitect __instance)
		{
			if (__instance.Owner?.GetRelic<GodslayerRune>() == null
				|| !HextechGodslayerTrial.IsTrialRun(__instance.Owner.RunState)
				|| BuildDialogue(__instance.Owner.Character.Id.Entry, out LocString? finalButton) is not { } dialogue)
			{
				return;
			}

			DialogueProperty!.SetValue(__instance, dialogue);
			if (finalButton != null)
			{
				ReplacedEvents.AddOrUpdate(__instance, finalButton);
			}

			HextechLog.Info("GodslayerTrial", $"Architect dialogue replaced: player={__instance.Owner.NetId} lines={dialogue.Lines.Count}");
		}
	}

	// 原版最后一行不读按钮文字，固定用 PROCEED；这里只换标题，回调仍是原版的胜利结算。
	[HarmonyPatch(typeof(TheArchitect), "CreateProceedOption")]
	[HextechPatch("event.architect.godslayer-proceed", "弑神")]
	private static class CreateProceedOptionPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => OptionTitleSetter != null;

		[HarmonyPostfix]
		private static void Postfix(TheArchitect __instance, EventOption __result)
		{
			if (!ReplacedEvents.TryGetValue(__instance, out LocString? title))
			{
				return;
			}

			OptionTitleSetter!.Invoke(__result, [title]);
			OptionHistoryNameSetter?.Invoke(__result, [title]);
		}
	}

	[HarmonyPatch(typeof(TheArchitect), "WinRun")]
	[HextechPatch("event.architect.godslayer-complete", "弑神")]
	private static class WinRunPatch
	{
		[HarmonyPrefix]
		private static void Prefix(TheArchitect __instance)
		{
			if (ReplacedEvents.TryGetValue(__instance, out _)
				&& LocalContext.IsMe(__instance.Owner)
				&& RunManager.Instance?.DebugOnlyGetState() is RunState runState)
			{
				_pendingFinale = new WeakReference<RunState>(runState);
			}
		}
	}

	// 原版只有建筑师胜利会走到 RunManager.WinRun。先播演出、关闭试炼，再调用原方法（此时已无待播演出，直接放行）。
	[HarmonyPatch(typeof(RunManager), "WinRun")]
	[HextechPatch("event.architect.godslayer-finale", "弑神")]
	private static class RunManagerWinRunPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RunManagerWinRun != null;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(RunManager __instance, ref Task __result)
		{
			if (!TryTakePendingFinale(__instance))
			{
				return true;
			}

			__result = PlayFinaleThenWinAsync(__instance);
			return false;
		}
	}

	private static bool TryTakePendingFinale(RunManager runManager)
	{
		WeakReference<RunState>? pending = _pendingFinale;
		_pendingFinale = null;
		return pending != null
			&& pending.TryGetTarget(out RunState? runState)
			&& ReferenceEquals(runManager.DebugOnlyGetState(), runState);
	}

	private static async Task PlayFinaleThenWinAsync(RunManager runManager)
	{
		// 演出或记录失败都不能卡住结算。
		try
		{
			await HextechGodslayerFinale.PlayAsync();
			HextechGodslayerTrial.CompleteSlaying();
		}
		catch (Exception exception)
		{
			HextechLog.Warn("GodslayerTrial", $"Finale step failed: {exception}");
		}

		await (Task)RunManagerWinRun!.Invoke(runManager, null)!;
	}
}
