namespace HextechRunes;

public sealed class SpeedDemonRune : TurnScopedRelicBase
{
	// 文案写的是字面值，改数值要同步九语言。
	private const decimal CardsToDraw = 2m;

	private bool _triggeredThisTurn;

	// 旧版本存档兼容占位：回合内状态不进存档、不进联机校验；名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedTriggeredThisTurn
	{
		get => false;
		set { }
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		EnsureTurnScopedStateCurrent();
		if (target.Side != CombatSide.Enemy
			|| result.UnblockedDamage <= 0
			|| (!IsOwnerOrPet(dealer) && cardSource?.Owner != Owner)
			|| !TryConsumeTurnProc(ref _triggeredThisTurn))
		{
			return;
		}

		Flash([target]);
		await CardPileCmd.Draw(choiceContext, CardsToDraw, Owner);
	}

	protected override void ResetTurnScopedState()
	{
		_triggeredThisTurn = false;
	}
}
