#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class UnbrokenSongRune
{
	// 本回合已经给过拉伸的旋律种类数（0~3）。按持有者回合号懒清零（同弦外之音：持有者自己的额外回合重置、队友的不重置）。
	// 只在钩子里推进，各端一致；战斗内状态，不存档。
	private int _rewardedMelodiesThisTurn;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StretchPower>(),
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	private void ResetRewardedMelodiesThisTurn()
	{
		_rewardedMelodiesThisTurn = 0;
	}

	// "获得旋律"口径同弦外之音：按持有者本回合旋律种类数（MelodyCmd.MelodiesThisTurn，含开嗓直接给予的）的增加量计，
	// 每回合每种旋律一次，一张牌带来多种就一次给多份。种类数只会在出牌结算完或回合开始前直接给予时增加，所以在这两处比较。
	// 出牌后获得的拉伸不会被这张牌扣掉：原版拉伸按出牌开始时的层数整笔扣除。
	public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		return player == Owner ? TryGainStretchForNewMelodies(choiceContext) : Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return cardPlay.Player == Owner ? TryGainStretchForNewMelodies(choiceContext) : Task.CompletedTask;
	}

	private async Task TryGainStretchForNewMelodies(PlayerChoiceContext choiceContext)
	{
		EnsureTurnScopedStateCurrent(ResetRewardedMelodiesThisTurn);
		if (Owner.Creature.IsDead
			|| !HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		int newMelodies = MelodyCmd.MelodiesThisTurn(Owner) - _rewardedMelodiesThisTurn;
		int stretch = DynamicVars["StretchPower"].IntValue;
		if (newMelodies <= 0 || stretch <= 0)
		{
			return;
		}

		// 先记账再结算：施加拉伸的钩子链里即使再次进入本方法，也不会重复奖励同一种旋律。
		_rewardedMelodiesThisTurn += newMelodies;
		Flash();
		await PowerCmd.Apply<StretchPower>(choiceContext, Owner.Creature, stretch * newMelodies, Owner.Creature, null);
	}
}
#endif
