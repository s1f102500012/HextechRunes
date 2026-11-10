namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
	{
		return HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterCardExhausted(context, choiceContext, card, causedByEthereal));
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (TrackPlayerAttackCardPlayedThisTurn(cardPlay)
			&& cardPlay.Card.Owner?.Creature.CombatState is HextechCombatState combatState)
		{
			RefreshPlayerAttackCostDoublingPreviews(HextechCombatCreatureHelper.GetAlivePlayerSideCreatures(combatState));
		}

		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, enemyHexContext) => effect.AfterCardPlayed(enemyHexContext, context, cardPlay));
	}

	// 洗牌/抽牌类敌方海克斯都只对本局战斗中的玩家生效，在分发层统一过滤。
	public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
	{
		if (shuffler.Creature.Side != CombatSide.Player
			|| shuffler.Creature.IsDead
			|| shuffler.Creature.CombatState?.RunState != RunState)
		{
			return;
		}

		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterShuffle(context, choiceContext, shuffler));
	}

	public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
	{
		if (card.Owner?.Creature.Side != CombatSide.Player
			|| card.Owner.Creature.CombatState?.RunState != RunState)
		{
			return Task.CompletedTask;
		}

		return HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterCardDrawn(context, choiceContext, card, fromHandDraw));
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		StormUpgradeRune.FindForCardPlay(cardPlay, RunState)?.RecordStormBeforeCardPlayed(cardPlay);
		return HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.BeforeCardPlayed(context, cardPlay));
	}

	public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterCardPlayedLate(context, choiceContext, cardPlay));

		// 升级雷暴在所有监听者之后补发闪电；为何不由符文自己覆写钩子见 StormUpgradeRune。
		if (StormUpgradeRune.FindForCardPlay(cardPlay, RunState) is StormUpgradeRune stormRune)
		{
			await stormRune.ChannelRecordedLightningAsync(choiceContext, cardPlay);
		}
	}

	public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
	{
		await HextechEnemyHexDispatcher.ForEachActive(
			this,
			(effect, context) => effect.AfterPlayerTurnStartLate(context, choiceContext, player));
	}

	public override Task AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext choiceContext, Player player)
	{
		CombatTracking.EnterPlayerPlayPhase(player.NetId);
		return Task.CompletedTask;
	}
}
