using MegaCrit.Sts2.Core.Map;

namespace HextechRunes;

internal abstract class HextechEnemyHexEffect
{
	/// <summary>敌人当前生命低于最大生命的这个比例时分发 <see cref="AfterEnemyHealthThreshold"/>（逃跑计划、排斥、黎明等共用）。</summary>
	internal const decimal EnemyHealthThresholdPercent = 0.5m;

	internal abstract MonsterHexKind Kind { get; }

	internal virtual bool AffectsPlayerAttackCostPreview => false;

	internal virtual int PersistentOrder => 0;

	internal virtual int EnemyHealOrder => 0;

	internal virtual void ResetRunScopedState()
	{
	}

	internal virtual decimal ModifyDamageMultiplicative(HextechEnemyHexContext context, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return 1m;
	}

	internal virtual decimal ModifyBlockMultiplicative(HextechEnemyHexContext context, Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return 1m;
	}

	internal virtual decimal ModifyEnemyHealMultiplicative(HextechEnemyHexContext context, Creature creature, decimal amount)
	{
		return 1m;
	}

	internal virtual decimal ModifyEnemyHealAmount(HextechEnemyHexContext context, Creature creature, decimal amount)
	{
		return amount;
	}

	internal virtual decimal ModifyHpLostAfterOsty(HextechEnemyHexContext context, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return amount;
	}

	// 只在原版真实伤害里、紧接着 Mayhem 改动了 ModifyHpLostAfterOsty 的那次查询之后调用（见 HextechMayhemModifier.AfterModifyingHpLostAfterOsty）。
	internal virtual void AfterModifyingHpLostAfterOsty(HextechEnemyHexContext context)
	{
	}

	internal virtual decimal ModifyHandDraw(HextechEnemyHexContext context, Player player, decimal count)
	{
		return count;
	}

	internal virtual bool ShouldDraw(HextechEnemyHexContext context, Player player, bool fromHandDraw)
	{
		return true;
	}

	// 只在 Mayhem 拦下一次真实抽牌之后调用（见 HextechMayhemModifier.AfterPreventingDraw）；ShouldDraw 本身只读。
	// 返回 true 表示这次被拦下的抽牌由本效果认领，排在后面的效果不再处理；requestedCount 是本次请求的张数。
	internal virtual Task<bool> AfterPreventingDraw(HextechEnemyHexContext context, Player player, bool fromHandDraw, int requestedCount)
	{
		return Task.FromResult(false);
	}

	internal virtual decimal ModifyEnergyGain(HextechEnemyHexContext context, Player player, decimal amount)
	{
		return amount;
	}

	// 紧跟在改动过能量获得量的 ModifyEnergyGain 之后调用（原版 PlayerCmd.GainEnergy）。
	internal virtual Task AfterModifyingEnergyGain(HextechEnemyHexContext context)
	{
		return Task.CompletedTask;
	}

	internal virtual bool ShouldPlay(HextechEnemyHexContext context, CardModel card, AutoPlayType autoPlayType)
	{
		return true;
	}

	internal virtual bool ShouldFlush(HextechEnemyHexContext context, Player player)
	{
		return true;
	}

	/// <summary>给该敌人的命中计数 +1,返回这一次是否恰好凑满一个阈值(每满 threshold 次返回一次 true)。</summary>
	internal static bool ReachesHitThreshold(Dictionary<uint, int> counters, uint combatId, int threshold)
	{
		int hits = counters.GetValueOrDefault(combatId, 0) + 1;
		counters[combatId] = hits;
		return threshold > 0 && hits % threshold == 0;
	}

	internal virtual decimal ModifyPlayerAttackEnergyCostMultiplier(HextechEnemyHexContext context, CardModel card, decimal originalCost)
	{
		return 1m;
	}

	/// <summary>
	/// 视同加在卡牌基础费用上的增量(优先级最低):本回合/直到打出的临时定费会覆盖它,
	/// 常规阶段排在它之前的遗物/能力减费仍叠加,Late 阶段的定费与下限最后生效。返回的是已折算临时修正后的实际增量。
	/// </summary>
	internal virtual int GetBaseEnergyCostIncrease(HextechEnemyHexContext context, CardModel card)
	{
		return 0;
	}

	internal virtual decimal ModifyEnergyCostInCombatLate(HextechEnemyHexContext context, CardModel card, decimal cost)
	{
		return cost;
	}

	internal virtual (PileType, CardPilePosition)? ModifyCardPlayResultPileTypeAndPosition(
		HextechEnemyHexContext context,
		CardModel card,
		bool isAutoPlay,
		ResourceInfo resources,
		PileType pileType,
		CardPilePosition position)
	{
		return null;
	}

	internal virtual Task ApplyPersistentToEnemy(HextechEnemyHexContext context, Creature creature, int? maxHpBaseOverride, bool replayOneShotPowers)
	{
		return Task.CompletedTask;
	}

	internal virtual Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		return Task.CompletedTask;
	}

	// 只在首次 BeforeCombatStart 与明确的首领阶段重放中分发；后续普通敌人入场不会进入此钩子。
	internal virtual Task ApplyOpeningCombatStartToEnemy(
		HextechEnemyHexContext context,
		Creature enemy,
		CombatRoom room,
		bool replayOneShotPowers)
	{
		return Task.CompletedTask;
	}

	internal virtual Task ApplyCombatStartPlayerDebuffs(HextechEnemyHexContext context, CombatRoom room, IReadOnlyList<Creature> players)
	{
		return Task.CompletedTask;
	}

	internal virtual ActMap ModifyGeneratedMapLate(HextechEnemyHexContext context, IRunState runState, ActMap map, int actIndex)
	{
		return map;
	}

	internal virtual Task BeforeSideTurnStart(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	internal virtual Task BeforePlayerSideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		return Task.CompletedTask;
	}

	internal virtual Task BeforeEnemySideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players, IReadOnlyList<Creature> enemies)
	{
		return Task.CompletedTask;
	}

	internal virtual Task BeforeCardPlayed(HextechEnemyHexContext context, CardPlay cardPlay)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCardPlayed(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterShuffle(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, Player shuffler)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCardExhausted(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
	{
		return Task.CompletedTask;
	}

	internal virtual decimal ModifyPowerAmountReceived(HextechEnemyHexContext context, PowerModel canonicalPower, Creature target, decimal amount, Creature? applier)
	{
		return amount;
	}

	internal virtual Task AfterCardDrawn(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterBlockGained(HextechEnemyHexContext context, Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCardPlayedLate(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterPlayerTurnStartLate(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, Player player)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterAutoPrePlayPhaseEnteredLate(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, Player player)
	{
		return Task.CompletedTask;
	}

	internal virtual Task BeforeTurnEnd(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, CombatSide side, CombatRoom? combatRoom)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyDamageReceived(HextechEnemyHexContext context, Creature target, uint combatId, DamageResult result, Creature? dealer, CardModel? cardSource)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyDamageReceivedAny(HextechEnemyHexContext context, Creature target, DamageResult result, Creature? dealer, CardModel? cardSource)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyHealthThreshold(HextechEnemyHexContext context, Creature target, uint combatId)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCurrentHpChanged(HextechEnemyHexContext context, Creature creature, decimal delta)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyDamageGivenImmediate(HextechEnemyHexContext context, Creature dealer, DamageResult result, Creature target, CardModel? cardSource)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyDamageGivenPlayerHit(HextechEnemyHexContext context, Creature dealer, Creature target)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterPowerAmountChanged(HextechEnemyHexContext context, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterEnemyDebuffReceived(HextechEnemyHexContext context, Creature target)
	{
		return Task.CompletedTask;
	}

	internal virtual Task BeforeDeath(HextechEnemyHexContext context, Creature creature)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterDeath(HextechEnemyHexContext context, PlayerChoiceContext choiceContext, Creature target, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCombatVictory(HextechEnemyHexContext context, CombatRoom room)
	{
		return Task.CompletedTask;
	}

	internal virtual Task AfterCombatEnd(HextechEnemyHexContext context, CombatRoom room)
	{
		return Task.CompletedTask;
	}

	internal virtual bool TryModifyRewards(HextechEnemyHexContext context, Player player, List<Reward> rewards, AbstractRoom? room)
	{
		return false;
	}

	internal virtual Task AfterGoldGained(HextechEnemyHexContext context, Player player)
	{
		return Task.CompletedTask;
	}

	internal virtual CardCreationOptions ModifyCardRewardCreationOptions(HextechEnemyHexContext context, Player player, CardCreationOptions options)
	{
		return options;
	}

	internal virtual bool TryModifyCardRewardOptions(HextechEnemyHexContext context, Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		return false;
	}

	internal virtual bool TryModifyCardRewardOptionsLate(HextechEnemyHexContext context, Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		return false;
	}
}
