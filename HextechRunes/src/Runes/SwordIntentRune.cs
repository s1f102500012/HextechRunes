namespace HextechRunes;

public sealed class SwordIntentRune : HextechRelicBase
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<SovereignBlade>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsRegentPlayer(player);
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldBladeBeFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldBladeBeFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	private bool ShouldBladeBeFree(CardModel card)
	{
		return card.Owner == Owner
			&& card is SovereignBlade
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}
}
