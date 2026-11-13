#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class TrioRune
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

	// 与弦外之音同一口径：在回合开始（直接给予的旋律已生效）与持有者每次出牌结算后检查种类数是否已达 3，每回合一次。
	// 与开嗓同持时第一回合抽完起手牌即触发。
	public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		return player == Owner ? TryRewardAllMelodies(choiceContext) : Task.CompletedTask;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return cardPlay.Player == Owner ? TryRewardAllMelodies(choiceContext) : Task.CompletedTask;
	}

	private async Task TryRewardAllMelodies(PlayerChoiceContext choiceContext)
	{
		EnsureTurnScopedStateCurrent(ResetTriggeredThisTurn);
		if (_triggeredThisTurn
			|| Owner.Creature.IsDead
			|| !HextechGrantedMelodies.IsOwnersSideTurn(Owner)
			|| !IsFoolPlayer(Owner)
			|| MelodyCmd.MelodiesThisTurn(Owner) < HextechGrantedMelodies.MelodyTags.Length
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		Flash();
		await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, Owner);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}
}
#endif
