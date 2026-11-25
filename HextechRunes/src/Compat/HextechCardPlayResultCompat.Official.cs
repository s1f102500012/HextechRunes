using MegaCrit.Sts2.Core.Hooks;

namespace HextechRunes;

/// <summary>出牌结果去向（结果堆/位置/归属玩家）的版本差异。0.109 起由 CardLocation 表达，可交给另一名玩家。</summary>
internal static class HextechCardPlayResultCompat
{
	internal static async Task<HextechFormCardResult> ResolveAutoPlayResult(
		HextechCombatState combatState,
		CardModel card,
		ResourceInfo resources)
	{
		CardLocation location = Hook.ModifyCardPlayResultLocation(
			combatState,
			card,
			isAutoPlay: true,
			resources,
			new CardLocation(card.Owner, PileType.None, CardPilePosition.Bottom),
			out IEnumerable<AbstractModel> modifiers);
		foreach (AbstractModel modifier in modifiers)
		{
			await modifier.AfterModifyingCardPlayResultLocation(card, location);
		}

		return new HextechFormCardResult(location.player, location.pileType, location.position);
	}

	/// <summary>结果去向是另一名玩家的非"移出战斗"堆时交给对方，返回 true；否则由调用方按本人结果堆处理。</summary>
	internal static async Task<bool> TryGiveToAnotherPlayer(CardModel card, HextechFormCardResult result)
	{
		if (result.Player == card.Owner || result.PileType == PileType.None)
		{
			return false;
		}

		await CardPileCmd.GiveToAnotherPlayer(card, result.Player, result.PileType, result.Position);
		return true;
	}
}
