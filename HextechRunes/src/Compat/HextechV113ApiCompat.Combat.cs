using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

/// <summary>
/// 0.111.0↔0.113.0 战斗侧 API 差异的集中适配：共享代码按 0.111.0 的语义调用，0.113.0 下换成等价的新写法。
/// </summary>
internal static partial class HextechV113ApiCompat
{
	// 君王之剑的连击段数：0.107.1/0.111.0 是 RepeatVar（DynamicVars.Repeat），0.113.0 改为 HitCountVar（DynamicVars.HitCount），
	// 原版 OnPlay 用它作 WithHitCount 的段数。0.113.0 的牌上已没有 "Repeat" 键，读旧变量会抛 KeyNotFoundException。
	internal static int GetSovereignBladeHitCount(SovereignBlade card)
	{
#if STS2_113_OR_NEWER
		return card.DynamicVars.HitCount.IntValue;
#else
		return card.DynamicVars.Repeat.IntValue;
#endif
	}

	// TreasureRoomRelicSynchronizer 的玩家集合私有字段：0.107.1/0.111.0 为 IPlayerCollection _playerCollection，
	// 0.113.0 改为 IRunState _runState（IRunState 继承 IPlayerCollection，按 IPlayerCollection 读取两版一致）。
	internal const string TreasureRelicSynchronizerPlayersFieldName =
#if STS2_113_OR_NEWER
		"_runState";
#else
		"_playerCollection";
#endif

	// 不能按"取规范模型再 ToMutable + PowerCmd.Apply"复制到别的生物身上的能力。
	// 0.113.0 的穿刺（ImpaledPower）把箭矢存在私有数据里，复制出的副本没有箭，命中计数归零时 RemoveFirstArrow 抛异常；
	// 它的 AfterApplied 还会读施加者的玩家 NetId。旧目标没有这个类型。
	internal static bool IsPowerExcludedFromReplication(PowerModel power)
	{
#if STS2_113_OR_NEWER
		return power is ImpaledPower;
#else
		return false;
#endif
	}

	// 怪物逃跑开始时关掉点选并隐藏状态栏，与同版本原版逃跑怪物（偷窃草蜢、胖地精的 EscapeMove）一致：
	// 0.111.0 用 ToggleIsInteractable(false) 立即隐藏；0.113.0 原版改为 SetIsInteractable(false, 不立即改可见性) 再渐隐状态栏。
	internal static void DisableCreatureInteractionForEscape(NCreature node)
	{
#if STS2_113_OR_NEWER
		node.SetIsInteractable(on: false, changeVisibilityImmediately: false);
		node.AnimateOutStateDisplay();
#else
		node.ToggleIsInteractable(false);
#endif
	}
}
