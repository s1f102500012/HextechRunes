namespace HextechRunes;

internal sealed class MasterOfDualityEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.MasterOfDuality;

	// 与 IsManualPlayerCardPlay 的统一口径不同：这里自动打出与同一张牌的重放也会触发。是否本意待设计确认，保持原行为。
	internal override async Task AfterCardPlayed(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner?.Creature.Side != CombatSide.Player)
		{
			return;
		}

		Creature playerCreature = cardPlay.Card.Owner.Creature;
		if (!playerCreature.IsAlive)
		{
			return;
		}

		if (HextechCardEffectTypes.IsSkillForEffects(cardPlay.Card))
		{
			await MasterOfDualityRune.ApplyTemporaryStat<HextechTemporaryStrengthPower, HextechTemporaryStrengthLossPower>(
				playerCreature, isGain: false, playerCreature, cardPlay.Card);
		}
		if (cardPlay.Card.Type == CardType.Attack)
		{
			await MasterOfDualityRune.ApplyTemporaryStat<HextechTemporaryDexterityPower, HextechTemporaryDexterityLossPower>(
				playerCreature, isGain: false, playerCreature, cardPlay.Card);
		}
	}
}
