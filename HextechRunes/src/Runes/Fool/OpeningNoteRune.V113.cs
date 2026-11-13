#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class OpeningNoteRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	// 持有者回合号为 1 的己方回合内拥有全部三种旋律，完全由各端一致的回合号推出，不需要字段。
	// 持有者在第一回合后的额外回合回合号已是 2，不再拥有。
	internal bool GrantsMelodyThisTurn()
	{
		return Owner != null
			&& IsOwnersFirstTurn
			&& HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			&& IsFoolPlayer(Owner);
	}

	// 拥有本身从回合开始就生效；这里只在抽完起手牌后闪光提示。
	public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player == Owner && !Owner.Creature.IsDead && GrantsMelodyThisTurn())
		{
			Flash();
		}

		return Task.CompletedTask;
	}
}
#endif
