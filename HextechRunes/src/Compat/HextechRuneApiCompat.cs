using MegaCrit.Sts2.Core.Hooks;

namespace HextechRunes;

/// <summary>
/// 符文内容用到的直接调用类 API 的统一入口，调用方不写行内 <c>#if</c>：
/// Hook.ModifyDamage 追加 CardPlay、CreatureCmd.LoseBlock 首参追加 PlayerChoiceContext 与移除者、CardPlay 新增 required Player。
/// </summary>
internal static class HextechRuneApiCompat
{
	/// <summary>0.108.0 起 Hook.ModifyDamage 在 cardSource 后追加 CardPlay 参数；此处不关联任何一次打出。</summary>
	internal static decimal ModifyDamage(
		IRunState runState,
		HextechCombatState? combatState,
		Creature? target,
		Creature? dealer,
		decimal damage,
		ValueProp props,
		CardModel? cardSource,
		ModifyDamageHookType modifyDamageHookType,
		CardPreviewMode previewMode)
	{
		return Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, null, modifyDamageHookType, previewMode, out _);
	}

	/// <summary>0.109.0 起 CreatureCmd.LoseBlock 首参新增 PlayerChoiceContext，并追加移除者参数。</summary>
	internal static Task LoseBlock(PlayerChoiceContext choiceContext, Creature target, decimal amount, Creature? remover)
	{
		return CreatureCmd.LoseBlock(choiceContext, target, amount, remover);
	}

	/// <summary>
	/// 构造一次不花费资源、结果堆为 None 的自动打出记录。0.109.0 起 CardPlay 新增 required Player(打出者)；
	/// 回放由卡牌所有者打出。
	/// </summary>
	internal static CardPlay CreateFreeAutoPlay(CardModel card, Creature? target)
	{
		return new CardPlay
		{
			Card = card,
			Player = card.Owner,
			Target = target,
			ResultPile = PileType.None,
			Resources = new ResourceInfo
			{
				EnergySpent = 0,
				EnergyValue = 0,
				StarsSpent = 0,
				StarValue = 0
			},
			IsAutoPlay = true,
			PlayIndex = 0,
			PlayCount = 1
		};
	}
}
