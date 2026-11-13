#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class OvertoneRune
{
	private bool _triggeredThisTurn;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	// 每回合一次的标记按持有者回合号懒清零（基类按战斗状态与持有者 TurnNumber 判断，持有者自己的额外回合重置、队友的不重置）。
	// 只在钩子里推进，各端一致；战斗内状态，不存档。
	private void ResetTriggeredThisTurn()
	{
		_triggeredThisTurn = false;
	}

	// "获得旋律"按持有者本回合旋律种类数增加计：种类数只会在出牌结算完（历史先记 CardPlayFinished 再调 AfterCardPlayed）
	// 或开嗓 / 引吭高歌直接给予（抽起手牌之前已生效）时增加，所以在这两处检查种类数是否已达 1 即等价于"首次从 0 变为 ≥1"。
	public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		return player == Owner ? TryGainEnergyOnFirstMelody() : Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return cardPlay.Player == Owner ? TryGainEnergyOnFirstMelody() : Task.CompletedTask;
	}

	private async Task TryGainEnergyOnFirstMelody()
	{
		EnsureTurnScopedStateCurrent(ResetTriggeredThisTurn);
		if (_triggeredThisTurn
			|| Owner.Creature.IsDead
			|| !HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			|| !IsFoolPlayer(Owner)
			|| MelodyCmd.MelodiesThisTurn(Owner) < 1
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		Flash();
		await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
	}
}
#endif
