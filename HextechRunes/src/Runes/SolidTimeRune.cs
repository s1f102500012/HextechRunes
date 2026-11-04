using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

public sealed partial class SolidTimeRune : HextechRelicBase
{
	private bool _startedThisCombat;
	private string _removedCardsJson = "";

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedRemovedPowerCardsJson
	{
		get => _removedCardsJson;
		set => _removedCardsJson = value ?? "";
	}

	public override Task BeforeCombatStart()
	{
		_startedThisCombat = false;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_startedThisCombat = false;
		return Task.CompletedTask;
	}

	public override Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
	{
		return TriggerStoredPowersAtCombatStart(choiceContext, player);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner != Owner
			|| cardPlay.Card.Type != CardType.Power
			|| !TryGetDeckPower(cardPlay.Card, out CardModel? deckCard))
		{
			return;
		}

		AppendStoredCard(deckCard, cardPlay.Target);
		Flash();
		await CardPileCmd.RemoveFromDeck(deckCard, showPreview: false);
	}

	private async Task TriggerStoredPowersAtCombatStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (_startedThisCombat
			|| player != Owner
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		_startedThisCombat = true;
		List<StoredCard> cards = DecodeStoredCards();
		if (cards.Count == 0)
		{
			return;
		}

		Flash();
		await HextechCombatVfx.SolidTimeResume(Owner.Creature, CreateDisplayCards(cards));
		for (int i = 0; i < cards.Count; i++)
		{
			if (CombatManager.Instance.IsOverOrEnding || Owner.Creature.IsDead)
			{
				break;
			}

			CardModel card = CreateCombatCard(combatState, cards[i]);
			card.SetToFreeThisCombat();
			Creature? target = PickTarget(card, cards[i], combatState, i);
			if (target == null && card.TargetType == TargetType.AnyAlly)
			{
				HextechLog.Info("SolidTime", $"Skipped stored ally power without a living teammate: card={card.Id}");
				continue;
			}

			await ApplyStoredPowerDirectly(choiceContext, card, target);
		}
	}

	private bool TryGetDeckPower(CardModel combatCard, [NotNullWhen(true)] out CardModel? deckCard)
	{
		deckCard = combatCard.DeckVersion;
		return deckCard != null
			&& deckCard.Owner == Owner
			&& deckCard.Pile?.Type == PileType.Deck
			&& deckCard.Type == CardType.Power
			&& Owner.Deck.Cards.Contains(deckCard);
	}
}
