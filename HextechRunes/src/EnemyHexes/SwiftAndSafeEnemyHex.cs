namespace HextechRunes;

internal sealed class SwiftAndSafeEnemyHex : DrawProgressEnemyHexBase
{
	internal override MonsterHexKind Kind => MonsterHexKind.SwiftAndSafe;

	private int GetCardsPerArtifact(HextechEnemyHexContext context)
	{
		return context.TierValue(Kind, 15, 12, 10);
	}

	protected override async Task AfterLocalCardDrawn(HextechEnemyHexContext context, Player owner, HextechCombatState combatState)
	{
		await ApplyArtifact(
			context,
			combatState,
			HextechEnemyDrawProgress.RecordDraw(context.Tracking.SwiftAndSafePlayerCardsDrawnThisCombat, GetCardsPerArtifact(context)));
	}

	protected override async Task ResolveDrawProgressFromHistory(HextechEnemyHexContext context, HextechCombatState combatState)
	{
		int pendingArtifact = HextechEnemyDrawProgress.ResolveFromHistory(
			context.Tracking.SwiftAndSafePlayerCardsDrawnThisCombat,
			combatState,
			GetCardsPerArtifact(context));
		await ApplyArtifact(context, combatState, pendingArtifact);
	}

	// 全队合计抽牌、阈值不随人数放大，所以每次只给精确的层数，绕过原版对敌方人工制品的联机缩放。
	private static async Task ApplyArtifact(HextechEnemyHexContext context, HextechCombatState combatState, int amount)
	{
		if (amount <= 0)
		{
			return;
		}

		foreach (Creature enemy in context.GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.ApplyExact<ArtifactPower>(enemy, amount, enemy, null);
		}
	}
}
