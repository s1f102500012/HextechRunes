using MegaCrit.Sts2.Core.Hooks;

namespace HextechRunes;

internal sealed class ProteinShakeEnemyHex : HextechEnemyHexEffect
{
	// 延迟格挡（再生抑制把敌人回血改记成的下回合格挡）兑现时不再转换：那笔格挡本来就是回血换来的，
	// 再转一次会重新记成延迟格挡，逐回合循环。只在兑现的异步作用域内生效。
	private static readonly AsyncLocal<bool> BypassConversion = new();

	internal override MonsterHexKind Kind => MonsterHexKind.ProteinShake;

	internal static async Task RunWithoutConversion(Func<Task> action)
	{
		bool previous = BypassConversion.Value;
		BypassConversion.Value = true;
		try
		{
			await action();
		}
		finally
		{
			BypassConversion.Value = previous;
		}
	}

	// 敌人获得格挡时先回复生命，回满后剩余的部分仍为格挡。生命已满时 combat.protein-shake.gain-block 前缀直接放行原版。
	internal static bool ShouldConvert(Creature creature)
	{
		return !BypassConversion.Value && creature.CurrentHp < creature.MaxHp;
	}

	// 照原版走前置回调与格挡修正算出最终格挡量（各种格挡加成都已计入），先拿它回复已损失的生命；
	// 没能回复的部分（回满，或治疗被禁止、被削减）照常获得格挡：后半段照搬原版 CreatureCmd.GainBlock（0.107.1 / 0.111.0 相同）。
	// 回血被改记成延迟格挡时，那部分已经换成了别的收益，算作已转换，不再作为格挡返还。
	internal static async Task<decimal> HealThenBlock(HextechMayhemModifier modifier, Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay, bool fast)
	{
		if (creature.CombatState is not { } combatState)
		{
			return 0m;
		}

		// 保留原版的前置回调：可能有模型在这里为本次格挡设倍率；等待之后重新确认还在战斗中、还活着。
		await Hook.BeforeBlockGained(combatState, creature, amount, props, cardPlay?.Card);
		if (!IsStillInCombat(creature, combatState))
		{
			return 0m;
		}

		decimal total = Math.Max(0m, Hook.ModifyBlock(combatState, creature, amount, props, cardPlay?.Card, cardPlay, out IEnumerable<AbstractModel> modifiers));
		await Hook.AfterModifyingBlockAmount(combatState, total, cardPlay?.Card, cardPlay, modifiers);
		decimal healRequest = Math.Min(total, Math.Max(0m, creature.MaxHp - creature.CurrentHp));
		decimal healed = 0m;
		if (healRequest > 0m && IsStillInCombat(creature, combatState))
		{
			int hpBefore = creature.CurrentHp;
			int delayedBefore = DelayedHealingBlock(modifier, creature);
			await CreatureCmd.Heal(creature, healRequest);
			int converted = creature.CurrentHp - hpBefore + DelayedHealingBlock(modifier, creature) - delayedBefore;
			healed = Math.Clamp(converted, 0m, healRequest);
		}

		decimal block = total - healed;
		if (block <= 0m || !IsStillInCombat(creature, combatState))
		{
			return 0m;
		}

		SfxCmd.Play("event:/sfx/block_gain");
		VfxCmd.PlayOnCreatureCenter(creature, "vfx/vfx_block");
		creature.GainBlockInternal(block);
		CombatManager.Instance.History.BlockGained(combatState, creature, (int)block, props, cardPlay);
		await (fast ? Cmd.CustomScaledWait(0f, 0.03f) : Cmd.CustomScaledWait(0.1f, 0.25f));
		await Hook.AfterBlockGained(combatState, creature, block, props, cardPlay?.Card);
		return block;
	}

	private static int DelayedHealingBlock(HextechMayhemModifier modifier, Creature creature)
	{
		return creature.CombatId is uint combatId
			? modifier.CombatTracking.DelayedEnemyHealingBlock.GetValueOrDefault(combatId, 0)
			: 0;
	}

	private static bool IsStillInCombat(Creature creature, HextechCombatState combatState)
	{
		return !CombatManager.Instance.IsOverOrEnding && !creature.IsDead && creature.CombatState == combatState;
	}
}
