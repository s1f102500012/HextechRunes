namespace HextechRunes;

public sealed class ScapegoatRune : HextechRelicBase
{
	private int _transferCount;

	public override Task BeforeCombatStart()
	{
		_transferCount = 0;
		return Task.CompletedTask;
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner || Owner.Creature.IsDead
			|| Owner.Creature.CombatState == null || CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		PowerModel[] debuffs = SnapshotDebuffs(Owner.Creature.Powers);
		if (debuffs.Length == 0)
		{
			return;
		}

		int ordinal = ConsumeCombatProcOrdinal(ref _transferCount);
		Creature? target = HextechRuneTargeting.PickRandomHittableEnemy(Owner,
			Owner.Creature.CombatState, "scapegoat-target", ordinal.ToString());
		if (target == null)
		{
			return;
		}

		Flash();
		foreach (PowerModel debuff in debuffs)
		{
			if (CombatManager.Instance.IsOverOrEnding || target.IsDead || Owner.Creature.IsDead)
			{
				break;
			}

			if (!Owner.Creature.Powers.Contains(debuff) || !IsDebuff(debuff))
			{
				continue;
			}

			int amount = debuff.Amount;
			PowerModel? transferred = CreateEnemyTransfer(debuff);
			await PowerCmd.Remove(debuff);
			if (transferred == null)
			{
				continue;
			}

			await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply(choiceContext, transferred,
				target, amount, Owner.Creature, null);
		}
	}

	internal static PowerModel[] SnapshotDebuffs(IEnumerable<PowerModel> powers) =>
		powers.Where(IsDebuff).ToArray();

	internal static PowerModel? CreateEnemyTransfer(PowerModel power)
	{
		// PowerModel 没有目标适用性契约。Hex、Ringing 等会直接访问 Owner.Player，
		// 卡牌/玩家专属效果和未核对的外部 Power 只净化，不在敌人侧运行其生命周期。
		// 此处仅接受已核对、可由敌人持有的具体类型，不能放行任意 Debuff 或基类派生项。
		if (!IsDebuff(power) || power is not (WeakPower or VulnerablePower or FrailPower
			or PoisonPower or DoomPower or DebilitatePower or ConstrictPower or SlowPower
			or StrengthPower or DexterityPower or FocusPower
			or HextechBurnPower or HextechNextTurnDamagePower))
		{
			return null;
		}

		PowerModel transferred = (PowerModel)power.ClonePreservingMutability();
		// 玩家侧的持续时间豁免不能带到敌人侧，否则会额外延长弱化等效果。
		transferred.SkipNextDurationTick = false;
		return transferred;
	}

	private static bool IsDebuff(PowerModel power) =>
		power.GetTypeForAmount(power.Amount) == PowerType.Debuff;
}
