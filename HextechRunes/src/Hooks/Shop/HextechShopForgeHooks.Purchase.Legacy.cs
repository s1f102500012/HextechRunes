#if !STS2_113_OR_NEWER
using Godot;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace HextechRunes;

// 0.107.1 / 0.111.0：商店购买只在购买者本机执行（NMerchantRelic → OnTryPurchaseWrapper），
// 其他端靠 RewardSynchronizer 的扣钱与获得遗物广播同步结果。
internal static partial class HextechShopForgeHooks
{
	private const string RelicEntryPurchaseMethodName = "OnTryPurchase";

	// 0.107.1 / 0.111.0 的商店布局：删牌服务向下让出第 4 个遗物槽的价格。
	private static readonly Vector2 CardRemovalRandomForgeOffset = new(0f, 60f);

	// 开店界面时补装条目：CreateForNormalMerchant 之外构造的普通商店库存在这里补上。
	private static void InstallRandomForgeEntryBeforeLayout(MerchantInventory inventory)
	{
		InstallRandomForgeEntry(inventory, inventory.Player);
	}

	private static async Task<(bool, int)> PurchaseRandomForge(MerchantRelicEntry entry, MerchantInventory inventory, bool ignoreCost)
	{
		// 只由 PurchasePatch 在 IsRandomForgeEntry 为真时调用；商店专属遗物只有 RandomForgeShopRelic 一种。
		Player player = inventory.Player;
		RandomForgeShopRelic shopRelic = (RandomForgeShopRelic)entry.Model!;
		HextechForgeShopPriceHelper.RefreshRandomForgeShopRelic(shopRelic, player.RunState as RunState);
		int cost = entry.Cost;
		if (!HextechForgeGrantHelper.TryCreateStableShopForgeChoice(player, shopRelic.PurchaseCount, out List<RelicModel> options))
		{
			entry.InvokePurchaseFailed(PurchaseStatus.FailureOutOfStock);
			return (false, 0);
		}

		RelicModel? forge = await HextechForgeSelectionCoordinator.SelectForge(player, options, "shop", syncMultiplayerChoice: false);
		if (forge == null)
		{
			return (false, 0);
		}

		// 主机权威复核:被配置禁用的锻造器即便因配置同步时序混进了候选,也要在扣钱前挡下,避免客机白花金币又拿到被禁锻造器。
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
			if (HextechPlayerContextHelper.IsMultiplayerConnected())
			{
				RunManager.Instance.RewardSynchronizer.SyncLocalGoldLost(cost);
			}
		}

		player.RunState.CurrentMapPointHistoryEntry?
			.GetEntry(player.NetId)
			.BoughtRelics
			.Add(forge.Id);

		await HextechForgeGrantHelper.ObtainSelectedForge(player, forge, syncObtainedRelic: true);

		// 复视复制商店购买的锻造器:商店购买不走锻造奖励(HextechForgeChoiceReward)那条已支持的复制路径,
		// 且直接 RelicCmd.Obtain 会被复视的「本模组程序集」闸门跳过,故在此显式触发一次(复用锻造奖励同款逻辑)。
		// 复制只在本地持有者一端开选择界面，复制份走 syncObtainedRelic 广播;它失败时若把异常抛进购买流程，
		// 只有本端会跳过下面的购买计数与原版补货/购买事件，两端商店状态就此分叉。所以这里只兜住复制本身的失败，
		// 取消（OperationCanceledException）照常向上传。
		try
		{
			await DoubleVisionRune.DuplicatePurchasedForge(player, forge);
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
