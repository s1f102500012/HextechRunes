#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class OvertoneRune
{
	// 本回合已经给过能量的旋律种类数（0~3）。按持有者回合号懒清零（基类按战斗状态与持有者 TurnNumber 判断，
	// 持有者自己的额外回合重置、队友的不重置）。只在钩子里推进，各端一致；战斗内状态，不存档。
	private int _rewardedMelodiesThisTurn;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	private void ResetRewardedMelodiesThisTurn()
	{
		_rewardedMelodiesThisTurn = 0;
	}

	// "获得旋律"按持有者本回合旋律种类数增加计：种类数只会在出牌结算完（历史先记 CardPlayFinished 再调 AfterCardPlayed）
	// 或开嗓 / 引吭高歌直接给予（抽起手牌之前已生效）时增加，所以在这两处把种类数与本回合已奖励数比较，
	// 差多少种就给多少次能量。本回合种类数不会减少，每种旋律每回合至多奖励一次。
	public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		return player == Owner ? TryGainEnergyForNewMelodies() : Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return cardPlay.Player == Owner ? TryGainEnergyForNewMelodies() : Task.CompletedTask;
	}

	private async Task TryGainEnergyForNewMelodies()
	{
		EnsureTurnScopedStateCurrent(ResetRewardedMelodiesThisTurn);
		if (Owner.Creature.IsDead
			|| !HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			|| !IsFoolPlayer(Owner))
		{
			return;
		}

		int newMelodies = MelodyCmd.MelodiesThisTurn(Owner) - _rewardedMelodiesThisTurn;
		if (newMelodies <= 0)
		{
			return;
		}

		// 先记账再结算：获得能量的钩子链里即使再次进入本方法，也不会重复奖励同一种旋律。
		_rewardedMelodiesThisTurn += newMelodies;
		Flash();
		await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue * newMelodies, Owner);
	}
}
#endif
