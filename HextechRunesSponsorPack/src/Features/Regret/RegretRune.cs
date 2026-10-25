using HextechRunes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunesSponsorPack;

public sealed class RegretRune : HextechRelicBase
{
	private bool _pendingPlayerRevive;
	private bool _freeCardsUntilOwnerTurnEnd;
	private int _revivesUsed;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedDamageBonusPercent
	{
		get => 0;
		set
		{
			// 旧档兼容:重做后的符文不再有永久增伤。属性名与类型必须保留(SavedProperty 集合决定联机 net-id 布局),
			// 读档时丢弃旧值、恒为 0。
		}
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedRevivesUsed
	{
		get => _revivesUsed;
		set
		{
			_revivesUsed = Math.Max(0, value);
			InvokeDisplayAmountChanged();
		}
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedFreeCardsUntilOwnerTurnEnd
	{
		get => _freeCardsUntilOwnerTurnEnd;
		set => _freeCardsUntilOwnerTurnEnd = value;
	}

	public override bool ShowCounter => !IsCanonical;

	public override int DisplayAmount => Math.Max(0, DynamicVars["MaxRevives"].IntValue - _revivesUsed);

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(2m),
		new PowerVar<DexterityPower>(2m),
		new DynamicVar("ReviveHpPercent", 30m),
		new DynamicVar("MaxRevives", 7m),
		new PowerVar<WeakPower>(2m),
		new PowerVar<VulnerablePower>(2m),
		new PowerVar<IntangiblePower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<DexterityPower>(),
		HoverTipFactory.FromPower<WeakPower>(),
		HoverTipFactory.FromPower<VulnerablePower>(),
		HoverTipFactory.FromPower<IntangiblePower>()
	];

	public override async Task BeforeCombatStart()
	{
		_pendingPlayerRevive = false;
		_freeCardsUntilOwnerTurnEnd = false;
		if (Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, null);
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_pendingPlayerRevive = false;
		_freeCardsUntilOwnerTurnEnd = false;
		return Task.CompletedTask;
	}

	public override Task BeforeDeath(Creature creature)
	{
		if (creature != Owner.Creature
			|| _pendingPlayerRevive
			|| _revivesUsed >= DynamicVars["MaxRevives"].IntValue)
		{
			return Task.CompletedTask;
		}

		_pendingPlayerRevive = true;
		return Task.CompletedTask;
	}

	public override bool ShouldDie(Creature creature)
	{
		return creature != Owner.Creature || !_pendingPlayerRevive;
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (target != Owner.Creature || !wasRemovalPrevented || !_pendingPlayerRevive)
		{
			return;
		}

		_pendingPlayerRevive = false;
		SavedRevivesUsed++;
		_freeCardsUntilOwnerTurnEnd = true;
		int reviveHp = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * DynamicVars["ReviveHpPercent"].BaseValue / 100m));
		Flash([Owner.Creature]);
		await CreatureCmd.SetCurrentHp(Owner.Creature, reviveHp);
		await ApplyReviveRewards(choiceContext);
	}

	public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == Owner.Creature.Side && _freeCardsUntilOwnerTurnEnd)
		{
			_freeCardsUntilOwnerTurnEnd = false;
		}

		return Task.CompletedTask;
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击与本体的免费海克斯），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		return TryMakeFree(card, originalCost, out modifiedCost);
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		return TryMakeFree(card, originalCost, out modifiedCost);
	}

	private async Task ApplyReviveRewards(PlayerChoiceContext choiceContext)
	{
		if (Owner.Creature.CombatState is HextechCombatState combatState)
		{
			await PowerCmd.Apply<WeakPower>(combatState.HittableEnemies, DynamicVars.Weak.BaseValue, Owner.Creature, null);
			await PowerCmd.Apply<VulnerablePower>(combatState.HittableEnemies, DynamicVars.Vulnerable.BaseValue, Owner.Creature, null);
		}

		await PowerCmd.Apply<IntangiblePower>(Owner.Creature, DynamicVars["IntangiblePower"].BaseValue, Owner.Creature, null);
		await DrawUntilHandFull(choiceContext);
	}

	private Task DrawUntilHandFull(PlayerChoiceContext choiceContext)
	{
		if (Owner.PlayerCombatState == null || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		int cardsToDraw = Math.Max(0, CardPile.MaxCardsInHand - PileType.Hand.GetPile(Owner).Cards.Count);
		return cardsToDraw > 0
			? CardPileCmd.Draw(choiceContext, cardsToDraw, Owner, fromHandDraw: false)
			: Task.CompletedTask;
	}

	// 能量与辉星费用共用同一判定:复活后到本人回合结束前,手牌/出牌区里自己的牌全部免费。
	private bool TryMakeFree(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldMakeCardFree(card))
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	private bool ShouldMakeCardFree(CardModel card)
	{
		return _freeCardsUntilOwnerTurnEnd
			&& !Owner.Creature.IsDead
			&& card.Owner == Owner
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}
}
