using MegaCrit.Sts2.Core.Models.Afflictions;

namespace HextechRunes;

// 玩家侧恶咒：原版 HexPower 绑定施加者（幽灵骑士死亡即解除）。这一版持续到被移除为止，不可叠层。
// 虚无直接按持有者判定、不看牌上有没有“邪咒”：牌上已有别的附着（如污染）时也照样虚无；
// 邪咒附着只负责与原版相同的外观，被原版恶咒移除时清掉后，在下次玩家回合开始时补回。
public sealed class HextechHexPower : HextechPowerBase
{
	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromAffliction<Hexed>(1);

	public override bool TryModifyKeywordsInCombat(CardModel card, ISet<CardKeyword> keywords)
	{
		return card.Owner != null
			&& card.Owner == Owner.Player
			&& keywords.Add(CardKeyword.Ethereal);
	}

	public override Task AfterApplied(Creature? applier, CardModel? cardSource) => AfflictOwnerCards();

	public override async Task AfterCardEnteredCombat(CardModel card)
	{
		if (card.Owner != null && card.Owner == Owner.Player)
		{
			await Afflict(card);
		}
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		return side == CombatSide.Player ? AfflictOwnerCards() : Task.CompletedTask;
	}

	public override Task AfterRemoved(Creature oldOwner)
	{
		// 原版恶咒仍在时邪咒外观归它管。
		if (oldOwner.HasPower<HexPower>() || oldOwner.Player?.PlayerCombatState is not { } combatState)
		{
			return Task.CompletedTask;
		}

		foreach (CardModel card in combatState.AllCards.ToList())
		{
			if (card.Affliction is Hexed)
			{
				CardCmd.ClearAffliction(card);
			}
		}

		return Task.CompletedTask;
	}

	internal static bool KeepsHexedAffliction(CardModel card)
	{
		return card.Owner?.Creature?.HasPower<HextechHexPower>() == true;
	}

	private async Task AfflictOwnerCards()
	{
		if (Owner.Player?.PlayerCombatState is not { } combatState)
		{
			return;
		}

		foreach (CardModel card in combatState.AllCards.ToList())
		{
			await Afflict(card);
		}
	}

	private static async Task Afflict(CardModel card)
	{
		if (card.Affliction == null)
		{
			await CardCmd.Afflict<Hexed>(card, 1m);
		}
	}
}
