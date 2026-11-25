using MegaCrit.Sts2.Core.Models.CardPools;

namespace HextechRunes;

/// <summary>
/// 直接调用类 API 的统一入口(0.108.0 起的签名,CardPlay 等新参数可省略):
/// CreatureCmd.Damage 带 cardSource 的重载与 AttackCommand.FromCard 的 CardPlay、
/// CardCreationOptions 以"池+过滤器"表达指定卡列表、WithCardPools 与过滤器分离。
/// 共享代码不写行内 #if,版本差异都收在这里。
/// </summary>
internal static class HextechGameApiCompat
{
	internal static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay = null)
	{
		return CreatureCmd.Damage(choiceContext, target, amount, props, dealer, cardSource, cardPlay);
	}

	internal static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext choiceContext, IEnumerable<Creature> targets, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay = null)
	{
		return CreatureCmd.Damage(choiceContext, targets, amount, props, dealer, cardSource, cardPlay);
	}

	internal static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext choiceContext, Creature target, DamageVar damageVar, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay = null)
	{
		return CreatureCmd.Damage(choiceContext, target, damageVar, dealer, cardSource, cardPlay);
	}

	internal static Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext choiceContext, IEnumerable<Creature> targets, DamageVar damageVar, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay = null)
	{
		return CreatureCmd.Damage(choiceContext, targets, damageVar, dealer, cardSource, cardPlay);
	}

	internal static AttackCommand FromCardCompat(this AttackCommand command, CardModel card, CardPlay? cardPlay = null)
	{
		return command.FromCard(card, cardPlay);
	}

	internal static IEnumerable<PotionModel> GetPotionOptions(Player player)
	{
		return PotionFactory.GetPotionOptions(player);
	}

	/// <summary>
	/// 原版没有"自定义卡列表"构造(0.108 起移除):用"全部角色池+无色池"超集配 Contains 过滤
	/// 等价表达(过滤集精确圈定目标卡,池只需覆盖列表来源)。
	/// </summary>
	internal static CardCreationOptions CreateOptionsFromCards(Player player, IEnumerable<CardModel> cards, CardCreationSource source, CardRarityOddsType rarityOdds)
	{
		HashSet<CardModel> allowed = [.. cards];
		IEnumerable<CardPoolModel> pools = player.UnlockState.CharacterCardPools
			.Concat<CardPoolModel>([ModelDb.CardPool<ColorlessCardPool>()]);
		return new CardCreationOptions(pools, source, rarityOdds, allowed.Contains);
	}

	/// <summary>只出指定这一张牌的创建选项:用该卡所属池 + 按 Id 过滤等价表达。</summary>
	internal static CardCreationOptions CreateOptionsForSingleCard(CardModel canonicalCard, CardCreationSource source, CardRarityOddsType rarityOdds)
	{
		ModelId cardId = canonicalCard.Id;
		return new CardCreationOptions([canonicalCard.Pool], source, rarityOdds, card => card.Id.Equals(cardId));
	}

	internal static CardCreationOptions WithCardPoolsCompat(this CardCreationOptions options, IEnumerable<CardPoolModel> pools, Func<CardModel, bool>? cardPoolFilter)
	{
		CardCreationOptions result = options.WithCardPools(pools);
		return cardPoolFilter != null ? result.WithFilter(cardPoolFilter) : result;
	}
}
