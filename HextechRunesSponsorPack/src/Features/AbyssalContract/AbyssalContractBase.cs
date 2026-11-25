using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Rooms;

namespace HextechRunesSponsorPack;

// 五种深渊契约各自的行为。策略实例无状态且进程内共享,所有持久计数写在 AbyssalContractRune 的
// [SavedProperty] 上,每场战斗的临时计数写在 rune 的 internal 字段上 —— 策略类自己不许有字段。
// 悬浮提示由符文按 AbyssalContractCatalog.Choices 推导,策略类不提供。
internal abstract class AbyssalContractBase
{
	public virtual Task ApplyInitialEffect(AbyssalContractRune rune) => Task.CompletedTask;

	public virtual Task AfterRemoved(AbyssalContractRune rune) => Task.CompletedTask;

	public virtual Task BeforeCombatStart(AbyssalContractRune rune) => Task.CompletedTask;

	public virtual Task AfterCombatVictory(AbyssalContractRune rune, CombatRoom room) => Task.CompletedTask;

	public virtual Task AfterCardPlayed(
		AbyssalContractRune rune,
		PlayerChoiceContext choiceContext,
		CardPlay cardPlay) => Task.CompletedTask;

	public virtual Task BeforeSideTurnStart(
		AbyssalContractRune rune,
		PlayerChoiceContext choiceContext,
		CombatSide side,
		HextechCombatState combatState) => Task.CompletedTask;

	public virtual Task BeforeTurnEnd(
		AbyssalContractRune rune,
		PlayerChoiceContext choiceContext,
		CombatSide side) => Task.CompletedTask;

	public virtual bool ShouldAddToDeck(AbyssalContractRune rune, CardModel card) => true;

	public virtual Task AfterAddToDeckPrevented(AbyssalContractRune rune, CardModel card) => Task.CompletedTask;

	public virtual bool TryModifyEnergyCostInCombat(
		AbyssalContractRune rune,
		CardModel card,
		decimal originalCost,
		out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		return false;
	}

	// 契约赠卡刻的是 Imbued,而 Imbued 只接受技能牌(CanEnchantCardType => CardType.Skill),
	// 三张赠卡(SwordSage / Parry / SleightOfFlesh)在 0.111.0 是 Power ——
	// 走 CardCmd.Enchant 会在 CanEnchant 处直接抛 InvalidOperationException,所以这里保留
	// EnchantInternal。附魔历史只在 card.Pile 为牌组时才记,而这里的牌尚未入堆(HextechRelicBase
	// .AddCardCopiesToDeckOrHand 先 CreateCard、再 configure、最后才 Add),记不记都没差别。
	protected static void ApplyImbuedEnchantment(CardModel card)
	{
		Imbued imbued = (Imbued)ModelDb.Enchantment<Imbued>().ToMutable();
		card.EnchantInternal(imbued, 1m);
		imbued.ModifyCard();
		card.FinalizeUpgradeInternal();
	}

	protected static async Task UpgradeCurrentStartingRelic(AbyssalContractRune rune)
	{
		Player owner = rune.Owner;
		if (!AbyssalContractCatalog.TryGetStarterRelics(owner.Character, out AbyssalContractCatalog.StarterRelicUpgrade starter))
		{
			return;
		}

		RelicModel? original = AbyssalContractCatalog.FindOwnedRelic(owner, starter.Starter);
		if (original != null)
		{
			await RelicCmd.Replace(original, AbyssalContractCatalog.GetCanonicalRelic(starter.Upgraded).ToMutable());
		}
	}
}
