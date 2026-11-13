#if STS2_113_OR_NEWER
using Godot;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace HextechRunes;

// 0.113.0：商店购买经 MerchantSynchronizer 按条目 Id 广播（MerchantEntryPurchasedMessage），每一端都对购买者那份库存执行
// MerchantEntry.OnTryPurchaseUnsynchronized：OnTryPurchaseInternal → 补货/清空 → Hook.AfterItemPurchased → PurchaseCompleted。
// 原版条目因此不再调用 RewardSynchronizer.SyncLocalGoldLost / SyncLocalObtained*，删牌的选牌改走选择同步。
// 本模组的随机锻造器条目同样在各端对称执行，选择走 PlayerChoiceSynchronizer，扣钱、获得、复视复制、购买计数各端各自完成，不再广播。
internal static partial class HextechShopForgeHooks
{
	private const string RelicEntryPurchaseMethodName = "OnTryPurchaseInternal";

	// 与旧版本一样把删牌服务向下让到第 4 个遗物槽下方，只是 0.113.0 的布局需要让得更多：
	// merchant_inventory.tscn 里遗物行在 (839,603)、槽距 150，第 4 槽在 x=1289，价格标签下沿约 y=672；
	// 删牌服务的圆形图标中心约 (1350,653)、半径约 88，正压在第 4 槽的价格上。下移 120 后图标上沿约 y=685，
	// 它自己的价格标签下沿约 y=900，仍在地毯贴图（高 978）之内。
	private static readonly Vector2 CardRemovalRandomForgeOffset = new(0f, 120f);

	// 不在开店界面时补装：界面只给本机库存，补装的条目只存在于本机、各端条目 Id 也会错位，
	// 购买消息在其他端找不到对应条目。条目只在 CreateForNormalMerchant 后缀里对所有玩家的库存安装。
	private static void InstallRandomForgeEntryBeforeLayout(MerchantInventory inventory)
	{
	}

	private static async Task<(bool, int)> PurchaseRandomForge(MerchantRelicEntry entry, MerchantInventory? inventory, bool ignoreCost)
	{
		// 只由 PurchasePatch 在 IsRandomForgeEntry 为真时调用；商店专属遗物只有 RandomForgeShopRelic 一种。
		// 本方法在每一端执行；以下所有共享状态写入都必须只依赖各端一致的输入（本局快照、购买者状态、同步选择结果）。
		Player player = entry.Player;
		RandomForgeShopRelic shopRelic = (RandomForgeShopRelic)entry.Model!;
		HextechForgeShopPriceHelper.RefreshRandomForgeShopRelic(shopRelic, player.RunState as RunState);
		int cost = entry.Cost;
		if (!HextechForgeGrantHelper.TryCreateStableShopForgeChoice(player, shopRelic.PurchaseCount, out List<RelicModel> options))
		{
			entry.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
			return (false, 0);
		}

		// 购买者本机弹选择界面并同步下标（取消同步为 -1），其他端等待同一个 choiceId；
		// 原版删牌条目的选牌（CardSelectCmd.FromDeckForRemoval）也是这样在各端对称预留 choiceId。
		RelicModel? forge = await HextechForgeSelectionCoordinator.SelectForge(player, options, "shop", syncMultiplayerChoice: true);
		if (forge == null)
		{
			return (false, 0);
		}

		// 被配置禁用的锻造器在扣钱前挡下；禁用表取本局同步快照，各端判定相同。
		if (HextechForgeGrantHelper.IsForgeDisabledForPlayer(player, forge))
		{
			HextechLog.Warn("Mayhem", $"Blocked purchasing a config-disabled forge: player={player.NetId} relic={forge.CanonicalId().Entry}");
			entry.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
			return (false, 0);
		}

		if (!CanContinueSynchronizedPurchase())
		{
			HextechLog.Warn("Mayhem", "Random forge purchase cancelled because multiplayer service is disconnected.");
			return (false, 0);
		}

		if (!ignoreCost)
		{
			await PlayerCmd.LoseGold(cost, player, GoldLossType.Spent);
		}

		player.RunState.CurrentMapPointHistoryEntry?
			.GetEntry(player.NetId)
			.BoughtRelics
			.Add(forge.Id);

		await HextechForgeGrantHelper.ObtainSelectedForge(player, forge, syncObtainedRelic: false);

		// 复视复制：各端各自获得复制份、不广播（与百炼成钢的对称路径同一入口）。复制失败只兜住复制本身，
		// 不打断下面的购买计数与原版的补货/AfterItemPurchased/PurchaseCompleted；取消照常向上传。
		try
		{
			await DoubleVisionRune.DuplicateReplicatedForge(player, forge);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			HextechLog.Warn("Mayhem", $"Double Vision failed to duplicate purchased forge: player={player.NetId} relic={forge.CanonicalId().Entry}: {ex.GetType().Name}: {ex.Message}");
		}

		shopRelic.IncrementPurchaseCount();
		entry.OnMerchantInventoryUpdated();

		return (true, ignoreCost ? 0 : cost);
	}
}
#endif
