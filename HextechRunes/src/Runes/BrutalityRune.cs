namespace HextechRunes;

public sealed class BrutalityRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("HpLoss", 2m),
		new CardsVar(2)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsIroncladPlayer(player);
	}

	public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		// 走原版失去生命流程(与放血相同的伤害属性)，撕裂等按本场失去生命次数计数的牌和能力才会算上它；
		// 失去量截到当前生命 - 1，保持不会自杀。
		decimal hpLoss = Math.Min(DynamicVars["HpLoss"].BaseValue, Owner.Creature.CurrentHp - 1);
		if (hpLoss > 0m)
		{
			await CreatureCmd.Damage(choiceContext, Owner.Creature, hpLoss, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
		}

		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner, fromHandDraw: false);
	}
}
