namespace HextechRunes;

public sealed class TrickLicenseRune : HextechRelicBase
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromKeyword(CardKeyword.Sly)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldPlayForFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		bool free = ShouldPlayForFree(card);
		modifiedCost = free ? 0m : originalCost;
		return free;
	}

	private bool ShouldPlayForFree(CardModel card)
	{
		return card.Owner == Owner
			&& card.IsSlyThisTurn
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}
}
