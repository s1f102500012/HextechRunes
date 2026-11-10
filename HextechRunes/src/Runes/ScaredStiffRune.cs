namespace HextechRunes;

public sealed class ScaredStiffRune : HextechRelicBase
{
	private bool _autoPlaying;
	private int _autoPlayTargetsThisCombat;

	// 稳定随机的本地序号（见 ConsumeCombatProcOrdinal）。单机不在战斗开始清零，按现状保留；
	// 联机每场清零，各端都从同一场的 0 开始，不受谁读过档影响。
	public override Task BeforeCombatStart()
	{
		if (IsNetworkMultiplayer())
		{
			_autoPlayTargetsThisCombat = 0;
		}

		return base.BeforeCombatStart();
	}

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsIroncladPlayer(player);
	}

	public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		// 发放闸门之外，外部接口(RelicBundleGrantHelper)、控制台或其他模组可把本符文直接给任意角色，触发时再判角色。
		if (_autoPlaying || Owner.Creature.IsDead || side != Owner.Creature.Side || !IsIroncladPlayer(Owner))
		{
			return;
		}

		List<CardModel> attacks = PileType.Hand.GetPile(Owner).Cards
			.Where(IsOwnedAttack)
			.ToList();
		if (attacks.Count == 0)
		{
			return;
		}

		_autoPlaying = true;
		try
		{
			Flash();
			for (int i = 0; i < attacks.Count; i++)
			{
				CardModel card = attacks[i];
				if (card.Pile?.Type != PileType.Hand)
				{
					continue;
				}

				card.ExhaustOnNextPlay = true;
				HextechCombatState combatState = Owner.Creature.CombatState!;
				int targetOrdinal = ConsumeCombatProcOrdinal(ref _autoPlayTargetsThisCombat);
				Creature? target = HextechRuneTargeting.PickRandomHittableEnemy(
					Owner,
					combatState,
					"scared-stiff",
					combatState.RoundNumber.ToString(),
					i.ToString(),
					targetOrdinal.ToString());
				await HextechAutoPlayHelper.AutoPlayOrMoveToResultPile(choiceContext, card, target);
			}
		}
		finally
		{
			_autoPlaying = false;
		}
	}
}
