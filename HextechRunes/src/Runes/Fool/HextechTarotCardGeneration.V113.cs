#if STS2_113_OR_NEWER
namespace HextechRunes;

/// <summary>
/// 塔罗组海克斯（水晶球、塔罗牌桌布）共用：判断一张牌是不是塔罗牌，以及用稳定随机挑本局允许的塔罗牌并生成进抽牌堆。
/// 候选与生成方式对照原版唯一的塔罗牌来源 MythicFormPower.BeforeHandDraw；挑选不消耗原版战斗随机流。
/// </summary>
internal static class HextechTarotCardGeneration
{
	// 原版塔罗牌没有 CardTag，只能按 TarotCards.All 的 22 种类型比对；神话形态、其他来源产出的同类型牌一样算。
	internal static bool IsTarotCard(CardModel card)
	{
		Type type = card.GetType();
		return TarotCards.All.Any(tarot => tarot.GetType() == type);
	}

	// 一次挑 count 张时不重复；本局允许的候选不足 count 张时，下一轮重新从全部候选里挑（允许与前一轮重复）。
	// saltParts 由调用方给出效果标签、持有者、回合号与触发序号。
	internal static List<CardModel> PickCanonicalTarotCards(Player owner, int count, params string?[] saltParts)
	{
		List<CardModel> candidates = TarotCards.All.Where(tarot => tarot.IsAllowedInRun(owner.RunState)).ToList();
		List<CardModel> picked = [];
		if (candidates.Count == 0)
		{
			return picked;
		}

		for (int round = 0; picked.Count < count; round++)
		{
			picked.AddRange(HextechStableRandom.PickDistinct(
				candidates,
				count - picked.Count,
				(RunState)owner.RunState,
				HextechStableRandom.CardKey,
				HextechStableRandom.AppendSalt(saltParts, "round", round.ToString())));
		}

		return picked;
	}

	// 照原版 MythicFormPower：combatState.CreateCard(规范模型, 持有者)，再以持有者为生成者加入抽牌堆随机位置
	// （同本模组"加入抽牌堆"的现有写法，例如 DizzySpinningRune），使其算作战斗中生成的牌。
	internal static async Task AddRandomTarotCardsToDrawPile(Player owner, int count, params string?[] saltParts)
	{
		if (count <= 0 || owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		List<CardModel> cards = PickCanonicalTarotCards(owner, count, saltParts)
			.Select(canonical => combatState.CreateCard(canonical, owner))
			.ToList();
		await HextechCardGeneration.AddGeneratedCardsToCombat(cards, PileType.Draw, addedByPlayer: true, CardPilePosition.Random);
	}
}
#endif
