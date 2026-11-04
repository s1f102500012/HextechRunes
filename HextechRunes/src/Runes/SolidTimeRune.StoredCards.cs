using System.Text.Json;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

public sealed partial class SolidTimeRune
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private string? _lastLoggedCorruptStoredCardsJson;

	private void AppendStoredCard(CardModel deckCard, Creature? playTarget)
	{
		List<StoredCard> cards = DecodeStoredCards();
		StoredCard stored = StoredCard.From(deckCard);
		// 只能指定队友的牌（灵魂绑定）记下第一次打出时绑定的队友，之后每场开战都给同一个人。
		if (deckCard.TargetType == TargetType.AnyAlly
			&& playTarget?.Player is Player targetPlayer
			&& targetPlayer != Owner)
		{
			stored = stored with { TargetPlayerNetId = targetPlayer.NetId };
		}

		cards.Add(stored);
		_removedCardsJson = JsonSerializer.Serialize(cards, JsonOptions);
		_lastLoggedCorruptStoredCardsJson = null;
	}

	private List<StoredCard> DecodeStoredCards()
	{
		if (string.IsNullOrWhiteSpace(_removedCardsJson))
		{
			_lastLoggedCorruptStoredCardsJson = null;
			return [];
		}

		try
		{
			List<StoredCard> cards = (JsonSerializer.Deserialize<List<StoredCard>>(_removedCardsJson, JsonOptions) ?? [])
				.Where(IsStoredPowerCard)
				.ToList();
			_lastLoggedCorruptStoredCardsJson = null;
			return cards;
		}
		catch (Exception ex)
		{
			if (!string.Equals(_lastLoggedCorruptStoredCardsJson, _removedCardsJson, StringComparison.Ordinal))
			{
				_lastLoggedCorruptStoredCardsJson = _removedCardsJson;
				string preview = _removedCardsJson[..Math.Min(80, _removedCardsJson.Length)];
				HextechLog.Warn(
					"SolidTime", $"Stored cards JSON is corrupted; dropping {_removedCardsJson.Length} chars: "
					+ $"{ex.GetType().Name}: {ex.Message} json={preview}");
			}

			return [];
		}
	}

	private static bool IsStoredPowerCard(StoredCard stored)
	{
		CardModel? canonical = TryGetCanonical(stored);
		return IsStoredAsPowerCard(canonical, stored);
	}

	// 以下两个入口只接收 DecodeStoredCards 的结果，那里已滤掉找不到模型或不是能力牌的记录。
	private static CardModel CreatePreviewCard(StoredCard stored)
	{
		CardModel preview = TryGetCanonical(stored)!.ToMutable();
		ApplyStoredCardState(preview, stored);
		ApplyUpgradeLevels(preview, stored.Upgrades);
		return preview;
	}

	// 开战特效用的展示牌：与悬停提示同样只 ToMutable，不进战斗状态；归属设为持有者，飞入动画按归属找角色节点。
	private List<CardModel> CreateDisplayCards(IReadOnlyList<StoredCard> cards)
	{
		List<CardModel> displayCards = new(cards.Count);
		foreach (StoredCard stored in cards)
		{
			CardModel preview = CreatePreviewCard(stored);
			preview.Owner = Owner;
			displayCards.Add(preview);
		}

		return displayCards;
	}

	private CardModel CreateCombatCard(HextechCombatState combatState, StoredCard stored)
	{
		CardModel card = combatState.CreateCard(TryGetCanonical(stored)!, Owner);
		ApplyStoredCardState(card, stored);
		ApplyUpgradeLevels(card, stored.Upgrades);
		SaveManager.Instance.MarkCardAsSeen(card);
		return card;
	}

	private static bool IsStoredAsPowerCard(CardModel? canonical, StoredCard stored)
	{
		if (canonical is MadScience)
		{
			return stored.GetMadScienceCardType() == CardType.Power;
		}

		return canonical?.Type == CardType.Power;
	}

	private static CardModel? TryGetCanonical(StoredCard stored)
	{
		// 存档 JSON 缺字段时反序列化出 null，不构造 ModelId；已卸载内容或类别不是卡牌时按缺失处理。
		if (string.IsNullOrEmpty(stored.Category) || string.IsNullOrEmpty(stored.Entry))
		{
			return null;
		}

		return ModelDb.GetByIdOrNull<AbstractModel>(new ModelId(stored.Category, stored.Entry)) as CardModel;
	}

	private static void ApplyUpgradeLevels(CardModel card, int upgrades)
	{
		int count = Math.Clamp(upgrades, 0, card.MaxUpgradeLevel);
		for (int i = 0; i < count; i++)
		{
			card.UpgradeInternal();
			card.FinalizeUpgradeInternal();
		}
	}

	private static void ApplyStoredCardState(CardModel card, StoredCard stored)
	{
		if (card is MadScience madScience)
		{
			madScience.TinkerTimeType = stored.GetMadScienceCardType();
			madScience.TinkerTimeRider = stored.GetMadScienceRider();
		}
	}

	private sealed record StoredCard(
		string Category,
		string Entry,
		int Upgrades,
		int? MadScienceCardType = null,
		int? MadScienceRider = null,
		ulong? TargetPlayerNetId = null)
	{
		public static StoredCard From(CardModel card)
		{
			ModelId id = card.CanonicalInstance.Id;
			if (card is MadScience madScience)
			{
				return new StoredCard(
					id.Category,
					id.Entry,
					card.CurrentUpgradeLevel,
					(int)madScience.TinkerTimeType,
					(int)madScience.TinkerTimeRider);
			}

			return new StoredCard(id.Category, id.Entry, card.CurrentUpgradeLevel);
		}

		public CardType GetMadScienceCardType()
		{
			return MadScienceCardType.HasValue
				? (CardType)MadScienceCardType.Value
				: CardType.Power;
		}

		public TinkerTime.RiderEffect GetMadScienceRider()
		{
			return MadScienceRider.HasValue
				? (TinkerTime.RiderEffect)MadScienceRider.Value
				: TinkerTime.RiderEffect.None;
		}
	}
}
