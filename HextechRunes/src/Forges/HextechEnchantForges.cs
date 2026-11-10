using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace HextechRunes;

public abstract class EnchantmentForgeBase<TEnchantment> : HextechForgeBase
	where TEnchantment : EnchantmentModel
{
	// 附魔层数;选牌张数固定为 1,两者互不相关(动量锻造器是选 1 张牌附魔动量 3)。
	protected virtual int EnchantmentAmount => 1;

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		.. HoverTipFactory.FromEnchantment<TEnchantment>(EnchantmentAmount)
	];

	// 泛型模型类的钩子只转发,不直接用类型参数(第三方批量补丁会把它写死,见 AutoPlayFormsAtCombatStartRuneBase)。
	public override Task AfterObtained()
	{
		return EnchantSelectedCardsAsync();
	}

	private async Task EnchantSelectedCardsAsync()
	{
		EnchantmentModel canonicalEnchantment = ModelDb.Enchantment<TEnchantment>();
		// FromDeckForEnchantment 的第三参是选牌界面的"附魔层数预览"(仅展示),不是选牌张数——
		// 误传张数(1)会让动量锻造器在选牌屏显示"动量1";选牌张数由 prefs 第二参控制,
		// 实际附魔层数由下方 Enchant 决定(动量=3,与预览一致)。
		IEnumerable<CardModel> selectedCards = await CardSelectCmd.FromDeckForEnchantment(
			Owner,
			canonicalEnchantment,
			EnchantmentAmount,
			new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1));
		foreach (CardModel selected in selectedCards)
		{
			Flash();
			CardCmd.Enchant(canonicalEnchantment.ToMutable(), selected, EnchantmentAmount);
			CardCmd.Preview(selected);
		}
	}
}

public sealed class GlamForge : EnchantmentForgeBase<Glam>
{
}

public sealed class SwiftForge : EnchantmentForgeBase<Swift>
{
	protected override int EnchantmentAmount => 2;
}

public sealed class SoulsPowerForge : EnchantmentForgeBase<SoulsPower>
{
}

public sealed class MomentumForge : EnchantmentForgeBase<Momentum>
{
	protected override int EnchantmentAmount => 3;
}

public sealed class EmbersForge : EnchantmentForgeBase<TezcatarasEmber>
{
}

public sealed class SpiralForge : EnchantmentForgeBase<UniversalSpiral>
{
}

public sealed class SteadyForge : EnchantmentForgeBase<Steady>
{
}
