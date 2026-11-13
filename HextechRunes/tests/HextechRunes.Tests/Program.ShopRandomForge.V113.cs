#if STS2_113_OR_NEWER
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 0.113.0 购买按条目 Id 广播：Setup 之后追加的随机锻造器条目，编号要与"条目在 Setup 前就在列表里"时原版 Setup 的结果相同，
	// 且 TryPurchaseEntryWithIdUnsynchronized 的按 Id 查找能找回该条目。
	[HextechTest]
	private static void RandomForgeShopEntryIdsMatchVanillaSetupNumbering()
	{
		Player player = CreateMerchantTestPlayer();

		(MerchantInventory appended, List<MerchantEntry> appendedOrder, MerchantRelicEntry appendedForge) = CreateMerchantTestInventory(player);
		appended.Setup();
		HextechV113ApiCompat.AddRelicEntry(appended, appendedForge);

		(MerchantInventory reference, List<MerchantEntry> referenceOrder, MerchantRelicEntry referenceForge) = CreateMerchantTestInventory(player);
		((List<MerchantRelicEntry>)AccessTools.Field(typeof(MerchantInventory), "_relicEntries").GetValue(reference)!).Add(referenceForge);
		reference.Setup();

		List<MerchantEntry> all = appended.AllEntries;
		Equal(referenceOrder.Count + 1, all.Count, "entry count after append");
		for (int i = 0; i < all.Count; i++)
		{
			Equal<uint?>((uint)i, all[i].Id, $"appended inventory id at {i}");
		}

		for (int i = 0; i < appendedOrder.Count; i++)
		{
			Equal(referenceOrder[i].Id, appendedOrder[i].Id, $"existing entry {i} id matches vanilla Setup numbering");
		}

		Equal(referenceForge.Id, appendedForge.Id, "random forge entry id matches vanilla Setup numbering");
		Expect(ReferenceEquals(all.FirstOrDefault(entry => entry.Id == appendedForge.Id), appendedForge), "id lookup finds the random forge entry");

		(MerchantInventory notSetUp, _, MerchantRelicEntry pendingForge) = CreateMerchantTestInventory(player);
		HextechV113ApiCompat.AddRelicEntry(notSetUp, pendingForge);
		Expect(notSetUp.AllEntries.All(static entry => entry.Id == null), "inventory before Setup keeps ids for vanilla Setup");
	}

	private static Player CreateMerchantTestPlayer()
	{
		Player player = CreateOrdinalTestPlayer(1);
		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
		return player;
	}

	// 三件遗物、三瓶药水、一条 testEntries（排在 AllEntries 末尾，追加遗物后它的下标会后移）；返回构造顺序与待追加的锻造器条目。
	private static (MerchantInventory Inventory, List<MerchantEntry> Order, MerchantRelicEntry Forge) CreateMerchantTestInventory(Player player)
	{
		List<MerchantRelicEntry> relics = [CreateUninitializedRelicEntry(null), CreateUninitializedRelicEntry(null), CreateUninitializedRelicEntry(null)];
		List<MerchantPotionEntry> potions = Enumerable.Range(0, 3)
			.Select(static _ => (MerchantPotionEntry)RuntimeHelpers.GetUninitializedObject(typeof(MerchantPotionEntry)))
			.ToList();
		MerchantInventory inventory = new(player, [], [], relics, potions, cardRemovalEntry: null, blitzEntries: []);
		MerchantRelicEntry trailing = CreateUninitializedRelicEntry(null);
		inventory.testEntries.Add(trailing);
		List<MerchantEntry> order = [.. relics, .. potions, trailing];
		return (inventory, order, CreateUninitializedRelicEntry(null));
	}
}
#endif
