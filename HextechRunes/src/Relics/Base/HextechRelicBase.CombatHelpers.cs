using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Models.Exceptions;

namespace HextechRunes;

public abstract partial class HextechRelicBase
{
	protected static int FloorToInt(decimal value)
	{
		return (int)decimal.Floor(value);
	}

	// "每 N 次触发一次"的进度从 previous 推进到 current 时跨过的阈值个数；读档或联机历史回放时一次可能跨过多个。
	internal static int CountThresholdCrossings(int previous, int current, int threshold)
	{
		int step = Math.Max(1, threshold);
		return Math.Max(0, Math.Max(0, current) / step - Math.Max(0, previous) / step);
	}

	// 旧版本按"本场累计、战后发放"记金币，新触发已改为直接发放；SavedCountThisCombat 只剩旧存档里尚未领取的值。
	// 战后把它补进奖励并清零，战斗开始时直接清零（传 null）。
	protected bool IsOwnedCard(CardModel? card)
	{
		return card?.Owner == Owner;
	}

	protected bool IsOwnedAttack(CardModel? card)
	{
		return card != null && card.Owner == Owner && card.Type == CardType.Attack;
	}

	protected bool IsOwnedSkill(CardModel? card)
	{
		return card != null && card.Owner == Owner && HextechCardEffectTypes.IsSkillForEffects(card);
	}

	protected bool IsAttackDamageForRuneEffects(ValueProp props, CardModel? cardSource)
	{
		if (props.IsPoweredAttack())
		{
			return true;
		}

		return HextechCardEffectTypes.IsOriginalOwnedSkill(cardSource, Owner);
	}

	protected int CountOwnedAttackCardsPlayedFromHistory()
	{
		return HextechCombatHistoryHelper.CountOwnedAttackCardsPlayed(Owner);
	}

	protected int CountOwnedCardsDrawnFromHistory()
	{
		return HextechCombatHistoryHelper.CountOwnedCardsDrawn(Owner);
	}

	protected bool IsOwnedCardWithEffectiveCostAtLeast(CardModel? card, decimal minimumCost)
	{
		// 含 X 费卡:X 费卡按本次实付的 X(GetEnergyCostForCurrentCardPlay 在打出期间返回实付能量)参与判定,
		// 不再用 !CostsX 排除。本方法仅由 5 个"费用≥N"符文(终极刷新/终极不可阻挡/最终形态/妖精魔法/碰不到我)共用,
		// 放开 X 费卡正是期望行为;其它 CostsX 排除逻辑都各自直接判 card.EnergyCost.CostsX,不经本方法。
		// 实付 X 由同步的打出动作决定,故各端判定一致,不引入联机分叉。
		return card != null
			&& card.Owner == Owner
			&& HextechCombatHooks.GetEnergyCostForCurrentCardPlay(card) >= minimumCost;
	}

	protected bool IsOwnerOrPet(Creature? dealer)
	{
		return HextechCombatHistoryHelper.IsOwnerOrPet(Owner, dealer);
	}

	protected bool IsDamageFromOwner(Creature? dealer, CardModel? cardSource)
	{
		return HextechCombatHistoryHelper.IsDamageFromOwner(Owner, dealer, cardSource);
	}

	protected bool IsDamageFromOwnerToEnemyOrPreview(Creature? target, Creature? dealer, CardModel? cardSource)
	{
		return (target == null || target.Side == CombatSide.Enemy)
			&& IsDamageFromOwner(dealer, cardSource);
	}

	protected bool IsPotionUseOwnedByOrTargetingOwner(PotionModel? potion, Creature? target)
	{
		if (target == Owner.Creature)
		{
			return true;
		}

		try
		{
			return potion?.Owner == Owner;
		}
		catch (CanonicalModelException)
		{
			return false;
		}
	}

	protected bool TryGetOwnedEnemyDebuffTarget(PowerModel power, decimal amount, Creature? applier, [NotNullWhen(true)] out Creature? target)
	{
		target = power.Owner;
		return amount > 0m
			&& target?.Side == CombatSide.Enemy
			&& applier == Owner.Creature
			&& power.GetTypeForAmount(amount) == PowerType.Debuff
			&& power is not ITemporaryPower;
	}

	// 与上面对称：持有者自己实际收到负面效果，来源不限（敌人、敌方海克斯、自己的牌或海克斯）。
	// 同样排除临时属性的包装 Power，它们到期会自行回收，不算一次真正的负面效果。
	protected bool TryGetOwnerReceivedDebuff(PowerModel power, decimal amount, [NotNullWhen(true)] out Creature? target)
	{
		target = power.Owner;
		return amount > 0m
			&& target == Owner.Creature
			&& !target.IsDead
			&& power.GetTypeForAmount(amount) == PowerType.Debuff
			&& power is not ITemporaryPower;
	}

	// 持有者自己实际获得增益效果，来源不限。排除临时属性的包装 Power（其内层力量/敏捷会单独计一次），
	// 也排除隐藏的内部记账能力。
	protected bool TryGetOwnerReceivedBuff(PowerModel power, decimal amount, [NotNullWhen(true)] out Creature? target)
	{
		target = power.Owner;
		return amount > 0m
			&& target == Owner.Creature
			&& !target.IsDead
			&& power.IsVisible
			&& power.GetTypeForAmount(amount) == PowerType.Buff
			&& power is not ITemporaryPower;
	}
}
