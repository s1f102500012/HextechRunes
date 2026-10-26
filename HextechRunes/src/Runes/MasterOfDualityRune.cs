namespace HextechRunes;

public sealed class MasterOfDualityRune : HextechRelicBase
{
	private const decimal TemporaryStatGain = 1m;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(1m),
		new PowerVar<DexterityPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<DexterityPower>()
	];

	public override async Task BeforeCombatStart()
	{
		if (Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, null);
	}

	// 敏捷也加到自己的攻击伤害上：条件照原版 StrengthPower（持有者是伤害来源、且是有力的攻击）。
	public override decimal ModifyDamageAdditiveCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return dealer == Owner.Creature && props.IsPoweredAttack()
			? Owner.Creature.GetPowerAmount<DexterityPower>()
			: 0m;
	}

	// 力量也加到自己的格挡上：条件照原版 DexterityPower（来源牌归持有者，无来源牌时目标是持有者，且是有力的格挡）。
	public override decimal ModifyBlockAdditive(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		bool ownsBlock = cardSource != null
			? cardSource.Owner?.Creature == Owner.Creature
			: target == Owner.Creature;
		return ownsBlock && props.IsPoweredCardOrMonsterMoveBlock()
			? Owner.Creature.GetPowerAmount<StrengthPower>()
			: 0m;
	}

	// 敌方物法皆修用的临时力量/敏捷增减（我方新版不再使用）。施加前先扣减对向的隐藏临时能力，不让两份并存。
	// 并存时任一份被净化、转移或提前移除，另一份回合末的回收就会变成永久的力量/敏捷变化。
	// 施加减益一侧遇到人工制品时仍走原施加路径，由人工制品整体抵挡，不去扣减增益。
	internal static async Task ApplyTemporaryStat<TGain, TLoss>(
		Creature creature,
		bool isGain,
		Creature? applier,
		CardModel? cardSource)
		where TGain : PowerModel
		where TLoss : PowerModel
	{
		// 人工制品挡下的施加会留下 0 层实例；原版临时能力在"变化量等于当前层数"时视为首次施加、不再补属性，
		// 0 层实例被再次叠加会漏掉这次力量/敏捷变化，先移除让下次重新施加。
		PowerModel? stale = isGain ? creature.GetPower<TGain>() : creature.GetPower<TLoss>();
		if (stale is { Amount: <= 0 })
		{
			await HextechPowerCmdCompat.Remove(stale);
		}

		PowerModel? opposite = isGain ? creature.GetPower<TLoss>() : creature.GetPower<TGain>();
		bool artifactBlocksLoss = !isGain && creature.GetPowerAmount<ArtifactPower>() > 0;
		if (opposite is { Amount: > 0 } && !artifactBlocksLoss)
		{
			await HextechPowerCmdCompat.ModifyAmount(opposite, -TemporaryStatGain, applier, cardSource);
			return;
		}

		if (isGain)
		{
			await PowerCmd.Apply<TGain>(creature, TemporaryStatGain, applier, cardSource);
		}
		else
		{
			await PowerCmd.Apply<TLoss>(creature, TemporaryStatGain, applier, cardSource);
		}
	}
}
