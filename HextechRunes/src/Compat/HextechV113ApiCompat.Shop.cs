#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace HextechRunes;

internal static partial class HextechV113ApiCompat
{
	// 0.113.0 原版 MerchantInventory.Setup 按 AllEntries 的顺序（角色牌、无色牌、遗物、药水、闪电战条目、删牌、testEntries）
	// 写 MerchantEntry.Id = 下标；MerchantSynchronizer 按 Id 广播购买（Id 为空直接抛异常），各端用 Id 找回同一条目。
	// Setup 之后追加的条目在这里按同一口径重编全部 Id：各端以相同顺序构造库存并追加，编号各端一致。
	// 库存还没 Setup（没有条目带 Id）时不编号，留给原版 Setup。
	internal static void AssignMerchantEntryIdsLikeSetup(MerchantInventory inventory)
	{
		List<MerchantEntry> entries = inventory.AllEntries;
		if (!entries.Any(static entry => entry.Id.HasValue))
		{
			return;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			entries[i].Id = (uint)i;
		}
	}
}
#endif
