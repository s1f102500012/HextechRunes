using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

/// <summary>
/// 战斗横切补丁的共享部分：跨分部文件使用的常量、每场战斗/每局重置的临时状态、私有成员访问与小工具。
/// 各分部文件只放自己的补丁与算法，不直接清理别的分部的状态。
/// </summary>
internal static partial class HextechCombatHooks
{
	// 与本模组在同一补丁点排序的第三方 Harmony owner（治疗封顶、无尽模式能力归一化都需要排在它们之后）。
	internal const string EndlessModeHarmonyId = "Natsuki.EndlessMode";
	internal const string RitsuLibCoreHarmonyId = "com.ritsukage.sts2-RitsuLib.framework-core";
	internal const string BaseLibHarmonyId = "BaseLib";

	// MoveState.Intents 的自动属性后备字段（0.107.1/0.111.0 原版只有 private set）。
	// 珠光护手临时替换显示意图、仪式兽/偷窃草蜢升级追加意图共用；缺失时两项功能各自降级
	// （珠光护手由 Prepare 停用，升级意图在 AddMonsterUpgradeIntents 入口判空）。
	private static readonly FieldInfo MoveStateIntentsField =
		TryGetField(typeof(MoveState), "<Intents>k__BackingField")!;

	// 出牌能量记账（PlayCost）：OnPlayWrapper 入栈、任务完成出栈；SpendResources 记下手动出牌的实付能量。
	private static readonly Dictionary<CardModel, Stack<int>> ActivePlayEnergyValues = new();
	private static readonly Dictionary<CardModel, int> PendingManualPlayEnergyValues = new();

	// 即死符文在血肉戏法/疫情响应链内不能同步 DoomKill(死亡处理与进行中的
	// power hook 链撞车会卡死游戏),先挂账,响应链退出后统一补杀（Outbreak）。
	private static readonly List<Creature> PendingInstantDeathDoomKills = [];

	/// <summary>跑局开始、读档与结束时清空本类的战斗临时状态。</summary>
	internal static void ResetTransientCombatState()
	{
		ActivePlayEnergyValues.Clear();
		ClearPendingManualPlayState();
		PendingInstantDeathDoomKills.Clear();
	}

	internal static void ClearPendingManualPlayState()
	{
		PendingManualPlayEnergyValues.Clear();
	}

	// 缩小、滑溜、人工制品这几处是原版机制的漏洞修正，海克斯内容让它们更容易触发；
	// 按设计哲学只在本局启用海克斯时生效，不改没开模组功能的对局。
	internal static bool IsVanillaFixActiveFor(Creature creature)
	{
		return HextechMayhemModifier.IsEnabledForRun(creature.CombatState?.RunState);
	}

	/// <summary>敌方生物所在的对局启用了指定敌方海克斯时，取出该局的 Modifier。</summary>
	private static bool TryGetActiveEnemyHexModifier(
		Creature? creature,
		MonsterHexKind kind,
		[NotNullWhen(true)] out HextechMayhemModifier? modifier)
	{
		modifier = creature?.Side == CombatSide.Enemy
			? HextechMayhemModifier.FindIn(creature.CombatState?.RunState)
			: null;
		if (modifier == null || !modifier.HasActiveMonsterHex(kind))
		{
			modifier = null;
			return false;
		}

		return true;
	}

	/// <summary>
	/// AsyncLocal 深度守卫的 Finalizer 收尾：原方法（或排在后面的补丁）同步抛异常时 Postfix 不会执行，
	/// 由这里在调用方执行流上同步出栈。正常路径由 Postfix 的 <see cref="HextechScopedDepthGuard.WrapEnteredTask(Task, Func{Task}?)"/>
	/// 出栈并把 <paramref name="entered"/> 清零，所以同一次调用不会重复出栈。
	/// </summary>
	private static Exception? ExitGuardAfterSynchronousFailure(HextechScopedDepthGuard guard, bool entered, Exception? exception)
	{
		if (entered && exception != null)
		{
			guard.Exit();
		}

		return exception;
	}
}
