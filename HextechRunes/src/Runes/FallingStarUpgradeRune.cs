using MegaCrit.Sts2.Core.Entities.Relics;

namespace HextechRunes;

public sealed class FallingStarUpgradeRune : CardUpgradeRuneBase<FallingStar>
{
	// 每场战斗只有首次打出陨星或流星雨（两张共用一次）时击晕。
	private bool _triggeredThisCombat;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedTriggeredThisCombat
	{
		get => _triggeredThisCombat;
		set => _triggeredThisCombat = value;
	}

	internal override bool GrantsCardOnPickup => false;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("StunTurns", 1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		.. base.ExtraHoverTips,
		HoverTipFactory.FromCard<MeteorShower>()
	];

	internal override bool MeetsCardAvailabilityRequirement(IEnumerable<CardModel> deckCards)
	{
		return deckCards.Any(static card => card is FallingStar or MeteorShower);
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		_triggeredThisCombat = false;
		Status = RelicStatus.Normal;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_triggeredThisCombat = false;
		Status = RelicStatus.Normal;
		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner != Owner || !IsSupportedCard(cardPlay.Card) || _triggeredThisCombat)
		{
			return;
		}

		_triggeredThisCombat = true;
		Status = RelicStatus.Disabled;
		List<Creature> targets = GetTargets(cardPlay).Where(static target => !target.IsDead).ToList();
		if (targets.Count == 0)
		{
			return;
		}

		Flash(targets);
		foreach (Creature target in targets)
		{
			await CreatureCmd.Stun(target);
		}
	}

	private IEnumerable<Creature> GetTargets(CardPlay cardPlay)
	{
		if (cardPlay.Card is MeteorShower)
		{
			return Owner.Creature.CombatState?.HittableEnemies ?? [];
		}

		if (cardPlay.Target is Creature target && target.Side == CombatSide.Enemy)
		{
			return [target];
		}

		return Owner.Creature.CombatState?.HittableEnemies ?? [];
	}

	private static bool IsSupportedCard(CardModel card)
	{
		return card is FallingStar or MeteorShower;
	}
}
