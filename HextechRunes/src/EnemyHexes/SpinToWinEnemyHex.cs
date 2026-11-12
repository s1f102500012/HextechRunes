namespace HextechRunes;

/// <summary>
/// 敌方转出胜利：玩家在回合内获得的抽牌与能量改为下回合抽牌/下回合能量。
/// 持有我方"转出胜利"（把下回合资源改为即时生效）的玩家不受影响，以我方效果为准，也避免两边来回转换。
/// </summary>
internal sealed class SpinToWinEnemyHex : HextechEnemyHexEffect
{
	// 原版 GainEnergy 里 ModifyEnergyGain 之后紧接着 AfterModifyingEnergyGain，这里只暂存这一次获得的量。
	// 查询钩子可能被本机模拟（如随机数预测模组）单独调用而没有后续结算，所以每次查询都覆盖暂存，
	// 不累加，避免模拟留下的量在下一次真实获得时只在这一端多结算。不保存：两次调用之间不会存档。
	private readonly Dictionary<Player, decimal> _pendingEnergy = new();

	internal override MonsterHexKind Kind => MonsterHexKind.SpinToWin;

	internal override void ResetRunScopedState()
	{
		_pendingEnergy.Clear();
	}

	internal override bool ShouldDraw(HextechEnemyHexContext context, Player player, bool fromHandDraw)
	{
		return !ShouldDeferDraw(context, player, fromHandDraw, HextechDrawRequest.GetRequestedCount(player));
	}

	internal override async Task<bool> AfterPreventingDraw(HextechEnemyHexContext context, Player player, bool fromHandDraw, int requestedCount)
	{
		if (!ShouldDeferDraw(context, player, fromHandDraw, requestedCount))
		{
			return false;
		}

		await PowerCmd.Apply<DrawCardsNextTurnPower>(player.Creature, requestedCount, null, null);
		return true;
	}

	internal override decimal ModifyEnergyGain(HextechEnemyHexContext context, Player player, decimal amount)
	{
		if (amount <= 0m || !AppliesTo(context, player))
		{
			return amount;
		}

		_pendingEnergy.Clear();
		_pendingEnergy[player] = amount;
		return 0m;
	}

	internal override async Task AfterModifyingEnergyGain(HextechEnemyHexContext context)
	{
		foreach ((Player player, decimal amount) in _pendingEnergy.ToList())
		{
			_pendingEnergy.Remove(player);
			if (!player.Creature.IsDead)
			{
				await PowerCmd.Apply<EnergyNextTurnPower>(player.Creature, amount, null, null);
			}
		}
	}

	private static bool ShouldDeferDraw(HextechEnemyHexContext context, Player player, bool fromHandDraw, int requestedCount)
	{
		return !fromHandDraw && requestedCount > 0 && AppliesTo(context, player);
	}

	private static bool AppliesTo(HextechEnemyHexContext context, Player player)
	{
		return player.Creature.Side == CombatSide.Player
			&& player.Creature.CombatState?.RunState == context.RunState
			&& player.GetRelic<SpinToWinRune>() == null
			&& HextechTurnPhase.IsDuringOwnTurn(player);
	}
}
