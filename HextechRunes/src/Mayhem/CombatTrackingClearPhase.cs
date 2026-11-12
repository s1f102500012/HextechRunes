namespace HextechRunes;

// 战斗追踪字段的清空时机。一个字段可同时属于多个边界(Flags),由 Prepare* 在对应回合边界
// 反射清空,新增 *ThisTurn 字段只需标注,不必在 Prepare* 里手写 Clear。
[Flags]
internal enum CombatTrackingClearPhase
{
	None = 0,
	PlayerTurnStart = 1 << 0,
	PlayerTurnEnd = 1 << 1,
	EnemyTurnStart = 1 << 2,
	EveryTurnBoundary = PlayerTurnStart | PlayerTurnEnd | EnemyTurnStart,
}

// 标注一个战斗追踪字段在哪些回合边界被清空。与 [CombatTrackingTransient] 正交:
// 字段可以既不存档(transient)又每回合清。
[AttributeUsage(AttributeTargets.Field)]
internal sealed class CombatTrackingClearAttribute : Attribute
{
	public CombatTrackingClearAttribute(CombatTrackingClearPhase phases)
	{
		Phases = phases;
	}

	public CombatTrackingClearPhase Phases { get; }
}
