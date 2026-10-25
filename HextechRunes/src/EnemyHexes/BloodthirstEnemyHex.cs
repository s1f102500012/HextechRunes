namespace HextechRunes;

internal sealed class BloodthirstEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Bloodthirst;

	internal override async Task AfterEnemyDamageGivenImmediate(HextechEnemyHexContext context, Creature dealer, DamageResult result, Creature target, CardModel? cardSource)
	{
		// 与敌方吸血习性同口径：敌人对玩家造成的未格挡伤害，按比例回复自身生命。
		if (result.UnblockedDamage <= 0 || target.Player == null || !dealer.IsAlive)
		{
			return;
		}

		decimal fraction = context.TierValue(Kind, 0.20m, 0.35m, 0.50m);
		int heal = Math.Max(1, (int)Math.Floor(result.UnblockedDamage * fraction));
		await CreatureCmd.Heal(dealer, heal);
	}
}
