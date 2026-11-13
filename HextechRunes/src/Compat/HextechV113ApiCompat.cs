using MegaCrit.Sts2.Core.Entities.Merchant;
#if STS2_113_OR_NEWER
using static HextechRunes.HextechHookReflection;
#endif

namespace HextechRunes;

/// <summary>
/// 0.111.0↔0.113.0 直接调用类 API 差异的集中适配(签名统一为 0.111 形态,0.113 下换成等价的新写法)。
/// 共享代码不写行内 #if,版本差异都收在这里。
/// </summary>
internal static partial class HextechV113ApiCompat
{
#if STS2_113_OR_NEWER
	// MerchantInventory._relicEntries(List<MerchantRelicEntry>,0.113.0 原版私有字段):0.113.0 删除了 AddRelicEntry,
	// 列表改由构造器传入。缺失时条目不加入库存。
	private static readonly FieldInfo? MerchantInventoryRelicEntriesField = TryGetField(typeof(MerchantInventory), "_relicEntries");
#endif

	// 0.111.0 的 AddRelicEntry 只做 _relicEntries.Add(entry);0.113.0 按同一字段追加。
	// 0.113.0 购买按条目 Id 同步,库存已经 Setup 过时按 Setup 的口径重编全部条目 Id(见 AssignMerchantEntryIdsLikeSetup);
	// 原版 Setup 订阅的 UpdateEntries 不补,由调用方自己订阅 PurchaseCompleted 刷新库存。
	internal static void AddRelicEntry(MerchantInventory inventory, MerchantRelicEntry entry)
	{
#if STS2_113_OR_NEWER
		if (MerchantInventoryRelicEntriesField?.GetValue(inventory) is List<MerchantRelicEntry> relicEntries)
		{
			relicEntries.Add(entry);
			AssignMerchantEntryIdsLikeSetup(inventory);
		}
#else
		inventory.AddRelicEntry(entry);
#endif
	}

	// 0.111.0 的 isFromCard: false 对应 0.113.0 的 cardSource: null(两版都据此决定失血是否带 ValueProp.Move)。
	internal static Task LoseMaxHpNotFromCard(PlayerChoiceContext choiceContext, Creature creature, decimal amount)
	{
#if STS2_113_OR_NEWER
		return CreatureCmd.LoseMaxHp(choiceContext, creature, amount, cardSource: null);
#else
		return CreatureCmd.LoseMaxHp(choiceContext, creature, amount, isFromCard: false);
#endif
	}
}
