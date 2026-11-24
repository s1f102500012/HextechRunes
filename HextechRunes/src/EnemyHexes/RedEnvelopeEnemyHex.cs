namespace HextechRunes;

/// <summary>红包（敌方，黄金）：玩家战后不再自然掉落金币；遗物、海克斯、锻造器等其他来源给的金币不受影响。</summary>
internal sealed class RedEnvelopeEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.RedEnvelope;

	// 原版 RewardsSet.WithRewardsFromRoom 先放房间自己生成的奖励（含遭遇战金币），再追加 CombatRoom.ExtraRewards
	//（遗物、海克斯、锻造器、被偷后夺回的金币都经 AddExtraReward 进这张表），之后才调用 Hook.ModifyRewards。
	// 所以“自然掉落的金币”就是列表里不属于该玩家 ExtraRewards 的 GoldReward；追加时用的是同一批对象，按引用比较。
	internal override bool TryModifyRewards(HextechEnemyHexContext context, Player player, List<Reward> rewards, AbstractRoom? room)
	{
		if (room is not CombatRoom combatRoom || player.RunState != context.RunState)
		{
			return false;
		}

		combatRoom.ExtraRewards.TryGetValue(player, out List<Reward>? extraRewards);
		return RemoveNaturalGoldRewards(rewards, extraRewards) > 0;
	}

	internal static int RemoveNaturalGoldRewards(List<Reward> rewards, IReadOnlyList<Reward>? extraRewards)
	{
		return rewards.RemoveAll(reward => reward is GoldReward && !IsExtraReward(reward, extraRewards));
	}

	private static bool IsExtraReward(Reward reward, IReadOnlyList<Reward>? extraRewards)
	{
		if (extraRewards == null)
		{
			return false;
		}

		foreach (Reward extra in extraRewards)
		{
			if (ReferenceEquals(extra, reward))
			{
				return true;
			}
		}

		return false;
	}
}
