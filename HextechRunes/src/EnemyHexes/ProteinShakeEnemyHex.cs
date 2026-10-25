using MegaCrit.Sts2.Core.Hooks;

namespace HextechRunes;

internal sealed class ProteinShakeEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.ProteinShake;

	// 敌人获得格挡时改为回复等量生命：combat.protein-shake.gain-block 前缀接管原版 CreatureCmd.GainBlock，
	// 按原版口径算出最终格挡量（各种格挡加成都已计入），不加格挡、不触发获得格挡的钩子，改为回复这么多生命。
	internal static async Task<decimal> HealInsteadOfBlock(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay)
	{
		if (creature.CombatState is not { } combatState)
		{
			return 0m;
		}

		decimal converted = Math.Max(0m, Hook.ModifyBlock(combatState, creature, amount, props, cardPlay?.Card, cardPlay, out IEnumerable<AbstractModel> modifiers));
		await Hook.AfterModifyingBlockAmount(combatState, converted, cardPlay?.Card, cardPlay, modifiers);
		if (converted > 0m && !creature.IsDead)
		{
			await CreatureCmd.Heal(creature, converted);
		}

		return 0m;
	}
}
