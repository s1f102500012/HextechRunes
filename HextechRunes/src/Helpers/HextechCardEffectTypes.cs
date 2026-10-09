namespace HextechRunes;

/// <summary>
/// 符文效果口径下的技能牌判定：按规范实例的类型判定，不受战斗中临时改类型影响。
/// </summary>
internal static class HextechCardEffectTypes
{
	internal static bool IsOriginalOwnedSkill(CardModel? card, Player owner)
	{
		return card?.Owner == owner && IsSkillForEffects(card);
	}

	internal static bool IsSkillForEffects(CardModel? card)
	{
		if (card == null)
		{
			return false;
		}

		// CanonicalInstance 的 getter 在规范模型上返回自身,不会断言可变。
		return (card.CanonicalInstance?.Type ?? card.Type) == CardType.Skill;
	}
}
