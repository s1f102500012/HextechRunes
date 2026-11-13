#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class LimberUpRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StretchPower>()];

	// 与原版隐秘之根、运动垫同一时机（己方阵营回合开始后）与同一命令；基类已按参与者过滤，
	// 队友的额外回合不触发，持有者自己的额外回合触发。
	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Creature.Side
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		int stretch = DynamicVars["StretchPower"].IntValue;
		if (stretch <= 0)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StretchPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, stretch, Owner.Creature, null);
	}
}
#endif
