namespace HextechRunes;

internal sealed class AncientWineEnemyHex : HextechEnemyHexEffect
{
	// 全队每手动打出 N 张技能牌（N=联机人数）触发一次；描述侧由 MonsterHexCatalog 的 SkillsNeeded 阈值同步显示。
	internal const int SkillsPerTriggerPerPlayer = 1;
	// 计数记在本场战斗的全局计数上：两端一致、随战斗快照恢复，战斗结束清零。
	private const string SkillsPlayedKey = "ancient-wine-skills";

	internal override MonsterHexKind Kind => MonsterHexKind.AncientWine;

	internal override async Task AfterCardPlayed(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (!context.IsManualPlayerCardPlay(cardPlay, out _, out HextechCombatState? combatState)
			|| !HextechCardEffectTypes.IsSkillForEffects(cardPlay.Card))
		{
			return;
		}

		int skillsPlayed = HextechCombatProcTracker.ConsumeGlobalProcInCombat(context.Tracking, SkillsPlayedKey) + 1;
		if (skillsPlayed % (SkillsPerTriggerPerPlayer * context.ScalingPlayerCount) != 0)
		{
			return;
		}

		decimal healFraction = context.TierValue(Kind, 0.01m, 0.02m, 0.03m);
		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await CreatureCmd.Heal(enemy, HextechEnemyHexContext.FractionOfMaxHp(enemy, healFraction));
		}
	}
}
