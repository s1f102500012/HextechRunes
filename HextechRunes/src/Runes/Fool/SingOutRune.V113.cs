#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class SingOutRune
{
	// 本回合直接给予的旋律与给予时的持有者回合号；战斗内状态，不存档（战斗中不存档，读档会重开战斗）。
	private CardTag? _grantedMelody;
	private int _grantedTurnNumber = -1;

	// 本回合给予的旋律还没结算原版"每当你获得旋律时"的效果。
	private bool _melodyGainedEffectsPending;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.WahMelody),
		HoverTipFactory.Static(StaticHoverTip.ChoMelody),
		HoverTipFactory.Static(StaticHoverTip.KiMelody)
	];

	internal bool GrantsMelodyThisTurn(CardTag melodyTag)
	{
		return Owner != null
			&& _grantedMelody == melodyTag
			&& Owner.PlayerCombatState?.TurnNumber == _grantedTurnNumber
			&& HextechGrantedMelodies.IsOwnersSideTurn(Owner);
	}

	public override Task BeforeCombatStart()
	{
		ClearGrantedMelody();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ClearGrantedMelody();
		return Task.CompletedTask;
	}

	// 抽起手牌之前给予，同回合的抽牌、AfterPlayerTurnStart（弦外之音、三重唱的回合开始检查）都已能看到。
	// 优先从尚未拥有的旋律里选（回合开始时通常三选一）；与开嗓同持的第一回合三种都已拥有，不再给予。
	public override Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		if (player != Owner
			|| Owner.Creature.IsDead
			|| Owner.PlayerCombatState == null
			|| !IsFoolPlayer(Owner))
		{
			return Task.CompletedTask;
		}

		ClearGrantedMelody();
		List<CardTag> missing = HextechGrantedMelodies.MelodyTags
			.Where(tag => !MelodyCmd.PlayedThisTurn(Owner, tag))
			.ToList();
		if (missing.Count == 0)
		{
			return Task.CompletedTask;
		}

		int turnNumber = Owner.PlayerCombatState.TurnNumber;
		_grantedMelody = HextechStableRandom.Pick(
			missing,
			(RunState)Owner.RunState,
			static tag => tag.ToString(),
			"fool-sing-out-melody",
			HextechStableRandom.PlayerKey(Owner),
			turnNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
		_grantedTurnNumber = turnNumber;
		_melodyGainedEffectsPending = true;
		Flash();
		return Task.CompletedTask;
	}

	// 给予旋律算"获得旋律"：抽完起手牌后，按持有者能力列表的顺序结算原版四个"每当你获得旋律时"的能力。
	// 这些能力自己只在出牌结算后按出牌历史判断，给予旋律没有出牌记录，所以这里照它们的效果各调用一次原版命令（数值读能力层数）。
	// 之后打出同一旋律的第一张牌，原版仍按自己的规则再触发一次；不为此改原版能力。
	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner
			|| !_melodyGainedEffectsPending
			|| _grantedMelody is not { } melody
			|| !GrantsMelodyThisTurn(melody))
		{
			return;
		}

		_melodyGainedEffectsPending = false;
		if (Owner.Creature.IsDead || Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		foreach (PowerModel power in GetMelodyGainedPowers(melody))
		{
			switch (power)
			{
				case MotifPower:
					await CardPileCmd.Draw(choiceContext, power.Amount, Owner);
					break;
				case LostLutePower:
					await CardPileCmd.AutoPlayFromDrawPile(choiceContext, Owner, power.Amount, CardPilePosition.Top, forceExhaust: false);
					break;
				case ViolentChordPower:
					await CreatureCmd.Damage(choiceContext, combatState.HittableEnemies, power.Amount, ValueProp.Unpowered, Owner.Creature);
					break;
				case SongPathPower:
					await CreatureCmd.GainBlock(Owner.Creature, power.Amount, ValueProp.Unpowered, null);
					break;
			}
		}
	}

	// 曲中意、失落鲁特琴对任意旋律生效；暴力和弦只认哇，歌之道只认啾。
	internal List<PowerModel> GetMelodyGainedPowers(CardTag melody)
	{
		return Owner.Creature.Powers
			.Where(power => power is MotifPower or LostLutePower
				|| (power is ViolentChordPower && melody == CardTag.Wah)
				|| (power is SongPathPower && melody == CardTag.Cho))
			.ToList();
	}

	private void ClearGrantedMelody()
	{
		_grantedMelody = null;
		_grantedTurnNumber = -1;
		_melodyGainedEffectsPending = false;
	}
}
#endif
