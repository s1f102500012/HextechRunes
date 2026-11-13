#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class FullQuiverRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForArrow(null)];

	// 原版在 CombatManager.SetUpCombat → Player.ResetCombatState 里按角色的 BaseArrowSlotCount 重建箭矢槽，
	// 早于 Hook.BeforeCombatStart；在这里加的槽不会被初始化覆盖，并在本场战斗内一直保留（与箭袋同一条命令）。
	// 每场战斗新建 PlayerCombatState，所以不需要自己的计数或清零。各端在同一钩子里对称执行，无随机。
	public override Task BeforeCombatStart()
	{
		if (Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		int arrows = DynamicVars["Arrows"].IntValue;
		if (arrows <= 0)
		{
			return Task.CompletedTask;
		}

		for (int i = 0; i < arrows; i++)
		{
			ArrowCmd.GainArrowSlot(Owner);
		}

		Flash();
		return Task.CompletedTask;
	}
}
#endif
