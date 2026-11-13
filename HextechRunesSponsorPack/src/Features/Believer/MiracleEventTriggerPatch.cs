using Godot;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunesSponsorPack;

// 等战斗领奖流程完成、原版关闭领奖界面并打开地图后，再延迟一帧进入神迹。
// 不能在胜利 Hook 中直接换房，也不能跳过原方法的 UI 收尾，否则会丢奖励或留下拦截输入的界面。
// 仅单人、单层战斗房且并非末幕 Boss 时消费待触发计数；计数晚于战后存档在内存中消费，
// 读回那份存档后可再次触发。事件完成走 NEventRoom.Proceed，不重入本补丁。
// 0.113.0 起 ProceedFromTerminalRewardsScreen 是 void 包装（TaskHelper.RunSafely 调 ...Async），
// 领奖界面与宝箱房调包装，无奖励的战斗（NCombatUi.ProceedWithoutRewards）直接调 ...Async；
// 补丁挂在 ...Async 上，每次领奖结束恰好经过一次。0.111.0 及以前只有返回 Task 的同名方法，所有入口都调它。
// 极速模式的对局本体不启用，这里同样不触发：那里离开奖励界面直接推进到下一节点，不开地图。
[SponsorPatch("believer.miracle-trigger", "信徒·神迹事件")]
#if STS2_113_OR_NEWER
[HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreenAsync))]
#else
[HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen))]
#endif
internal static class MiracleEventTriggerPatch
{
	private const string LogTag = "Believer";

	[HarmonyPostfix]
	private static void Postfix(RunManager __instance, ref Task __result)
	{
		__result = ChainMiracleAfterProceed(__instance, __result);
	}

	private static async Task ChainMiracleAfterProceed(RunManager runManager, Task original)
	{
		await original;

		try
		{
			if (HextechRelicBase.IsNetworkMultiplayerRun())
			{
				return;
			}

			RunState? state = runManager.DebugOnlyGetState();
			if (state == null || state.CurrentRoomCount != 1 || SponsorV113ApiCompat.IsGameModeUnsupported(state))
			{
				return;
			}

			// 只在「战斗领奖→开图」的交接点注入(事件领奖走 NEventRoom.Proceed,不经过这里)。
			if (state.CurrentRoom is not CombatRoom combatRoom)
			{
				return;
			}

			// 末战守卫(bug:末战拿信徒无法结算):末幕 Boss 之后不插事件,留给 EnterNextAct → WinRun 正常结算。
			if (combatRoom.RoomType == RoomType.Boss && state.CurrentActIndex >= state.Acts.Count - 1)
			{
				return;
			}

			BelieverRune? believer = state.Players
				.SelectMany(static player => player.Relics)
				.OfType<BelieverRune>()
				.FirstOrDefault(static relic => relic.HasPendingMiracle);
			if (believer == null)
			{
				return;
			}

			believer.ConsumePendingMiracle();
			// deferred 到下一帧再 EnterRoom:脱离领奖屏 proceed 的协程栈(ExitCurrentRooms 会
			// 销毁正在跑这段代码的战斗 UI 节点),也让地图打开的收尾先落地。
			Callable.From(() =>
			{
				_ = TaskHelper.RunSafely(EnterMiracle(runManager));
			}).CallDeferred();
		}
		catch (Exception ex)
		{
			SponsorLog.Warn(LogTag, $"Miracle trigger postfix error: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static async Task EnterMiracle(RunManager runManager)
	{
		try
		{
			EventModel miracle = ModelDb.Event<MiracleEvent>();
			// EnterRoomDebug 即 dev console travel 的完整进房管线:ClearScreens(清掉残留的
			// 领奖屏/地图屏——裸 EnterRoom 缺这步,残留领奖按钮层会拦截事件选项点击)、
			// 同步等待、CreateRoom+EnterRoom、FadeIn 过场,与正常进房状态完全一致。
			await runManager.EnterRoomDebug(RoomType.Event, model: miracle, showTransition: true);
		}
		catch (Exception ex)
		{
			SponsorLog.Warn(LogTag, $"Failed to enter Miracle event: {ex.GetType().Name}: {ex.Message}");
			// 兜底:进事件失败时确保地图可用,玩家不至于卡死。
			try
			{
				NMapScreen.Instance?.SetTravelEnabled(true);
				NMapScreen.Instance?.Open();
			}
			catch (Exception mapException)
			{
				// 真实边界:Godot 节点可能已在换房中途销毁;兜底本身失败只记录,不再向上抛。
				SponsorLog.Warn(LogTag, $"Map fallback after the failed Miracle entry also failed: {mapException.GetType().Name}: {mapException.Message}");
			}
		}
	}
}
