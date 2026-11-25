namespace HextechRunes;

internal sealed class PandorasBoxEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.PandorasBox;

	internal override CardCreationOptions ModifyCardRewardCreationOptions(HextechEnemyHexContext context, Player player, CardCreationOptions options)
	{
		if (player.RunState != context.RunState
			|| options.Source != CardCreationSource.Encounter
			|| options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications)
			|| options.CardPools.All(static pool => pool.IsColorless))
		{
			return options;
		}

		// 只保留其他角色的卡池(排除玩家自身角色),使奖励「只会包含其他颜色」。
		ModelId ownerPoolId = player.Character.CardPool.Id;
		List<CardPoolModel> otherPools = player.UnlockState.CharacterCardPools
			.Where(pool => !pool.Id.Equals(ownerPoolId))
			.ToList();
		return otherPools.Count > 0
			? options.WithCardPoolsCompat(otherPools, options.CardPoolFilter)
			: options;
	}
}
