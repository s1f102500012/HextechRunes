using System.Diagnostics.CodeAnalysis;
using Godot;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using CoreHook = MegaCrit.Sts2.Core.Hooks.Hook;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechShopForgeHooks
{
	private const float CardRemovalRandomForgeOffsetY = 60f;

	// MerchantInventory._relicEntries（List<MerchantRelicEntry>，0.107.1/0.110.0/0.111.0 原版私有字段）：
	// 假商店（NFakeMerchantInventory）里移除随机锻造器条目用；缺失时条目保留，只影响假商店展示。
	private static readonly FieldInfo? MerchantInventoryRelicEntriesField = TryGetField(typeof(MerchantInventory), "_relicEntries");

	/// <summary>
	/// 随机锻造器商店条目依赖四个原版商人方法(购买/补货/清空/定价);任一缺失则整组停用,
	/// 否则条目会出现在商店却无法购买。七个补丁类共用这一次探测。
	/// </summary>
	private static readonly bool RandomForgeShopHooksAvailable = ProbeRandomForgeShopHooks();

	private static readonly Dictionary<ulong, Vector2> CardRemovalOriginalPositions = [];

	private static bool ProbeRandomForgeShopHooks()
	{
		const BindingFlags instanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
		bool available =
			TryGetMethod(typeof(MerchantRelicEntry), "OnTryPurchase", instanceNonPublic, typeof(MerchantInventory), typeof(bool)) != null
			&& TryGetMethod(typeof(MerchantRelicEntry), "RestockAfterPurchase", instanceNonPublic, typeof(MerchantInventory)) != null
			&& TryGetMethod(typeof(MerchantRelicEntry), "ClearAfterPurchase", instanceNonPublic) != null
			&& TryGetMethod(typeof(CoreHook), nameof(CoreHook.ModifyMerchantPrice), BindingFlags.Static | BindingFlags.Public, typeof(IRunState), typeof(Player), typeof(MerchantEntry), typeof(decimal)) != null;
		if (!available)
		{
			HextechLog.Warn("Mayhem", "Random forge shop entry disabled because one or more merchant hooks are unavailable.");
		}

		return available;
	}

	private static void InstallRandomForgeEntry(MerchantInventory inventory, Player player)
	{
		// 模组总开关:商店随机锻造器是无条件注入普通局的少数泄漏点之一,按本局冻结值门控。
		// 无 run/modifier 时:联机局固定 false(实时本地配置在两端可能不同,而这里门控的是
		// MerchantInventory 模型写入,按本地配置各走一边会库存分叉);单机局退回实时配置。
		// 弑神试炼局我方不拿任何模组内容，商店也不卖锻造器。
		if (!HextechMayhemModifier.IsEnabledForRun(player.RunState)
			|| HextechGodslayerTrial.IsTrialRun(player.RunState))
		{
			return;
		}

		if (inventory.RelicEntries.Any(IsRandomForgeEntry))
		{
			return;
		}

		RandomForgeShopRelic shopRelic = (RandomForgeShopRelic)ModelDb.Relic<RandomForgeShopRelic>().ToMutable();
		HextechForgeShopPriceHelper.RefreshRandomForgeShopRelic(shopRelic, player.RunState as RunState);
		MerchantRelicEntry entry = new(shopRelic, player);
		entry.PurchaseCompleted += (_, _) => UpdateInventoryEntries(inventory);
		inventory.AddRelicEntry(entry);
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

	// 单人流程照常继续；联机局只在连接仍在时继续。
	private static bool CanContinueSynchronizedPurchase()
	{
		return !HextechPlayerContextHelper.IsNetworkMultiplayerRun() || HextechPlayerContextHelper.IsMultiplayerConnected();
	}

	private static bool IsRandomForgeEntry(MerchantEntry entry)
	{
		return entry is MerchantRelicEntry relicEntry && HextechCatalog.IsHextechShopRelic(relicEntry.Model);
	}

	private static void RemoveRandomForgeEntries(MerchantInventory inventory)
	{
		if (!inventory.RelicEntries.Any(IsRandomForgeEntry))
		{
			return;
		}

		if (MerchantInventoryRelicEntriesField?.GetValue(inventory) is List<MerchantRelicEntry> relicEntries)
		{
			relicEntries.RemoveAll(IsRandomForgeEntry);
		}
	}

	private static bool TryGetRandomForgeShopRelic(MerchantEntry entry, [NotNullWhen(true)] out RandomForgeShopRelic? shopRelic)
	{
		shopRelic = entry is MerchantRelicEntry relicEntry ? relicEntry.Model as RandomForgeShopRelic : null;
		return shopRelic != null;
	}

	private static void UpdateInventoryEntries(MerchantInventory inventory)
	{
		foreach (MerchantEntry entry in inventory.AllEntries)
		{
			if (TryGetRandomForgeShopRelic(entry, out RandomForgeShopRelic? shopRelic))
			{
				HextechForgeShopPriceHelper.RefreshRandomForgeShopRelic(shopRelic, inventory.Player.RunState as RunState);
			}

			entry.OnMerchantInventoryUpdated();
		}
	}

	private static void EnsureRandomForgeRelicSlot(NMerchantInventory merchantInventory, MerchantInventory inventory)
	{
		if (!inventory.RelicEntries.Any(IsRandomForgeEntry))
		{
			return;
		}

		if (merchantInventory.GetNodeOrNull<Control>("%Relics") is not Control relicContainer)
		{
			HextechLog.Warn("Mayhem", $"Random forge shop slot skipped: relic container unavailable.");
			return;
		}

		List<NMerchantRelic> relicSlots = relicContainer.GetChildren().OfType<NMerchantRelic>().ToList();
		while (relicSlots.Count < inventory.RelicEntries.Count)
		{
			NMerchantRelic? template = relicSlots.LastOrDefault();
			if (template == null)
			{
				HextechLog.Warn("Mayhem", $"Random forge shop slot skipped: no relic slot template available.");
				return;
			}

			Node duplicatedNode = template.Duplicate();
			if (duplicatedNode is not NMerchantRelic extraSlot)
			{
				duplicatedNode.QueueFree();
				HextechLog.Warn("Mayhem", $"Random forge shop slot skipped: duplicated node is not a merchant relic slot.");
				return;
			}

			extraSlot.Name = $"{template.Name}_HextechExtra{relicSlots.Count}";
			extraSlot.Position = template.Position + GetNextSlotOffset(relicSlots);
			relicContainer.AddChild(extraSlot);
			relicSlots.Add(extraSlot);
		}
	}

	private static void MoveCardRemovalBelowRandomForge(NMerchantInventory merchantInventory, MerchantInventory inventory)
	{
		if (!inventory.RelicEntries.Any(IsRandomForgeEntry))
		{
			return;
		}

		if (merchantInventory.GetNodeOrNull<NMerchantCardRemoval>("%MerchantCardRemoval") is not NMerchantCardRemoval cardRemoval)
		{
			HextechLog.Warn("Mayhem", $"Random forge shop card removal offset skipped: card removal node unavailable.");
			return;
		}

		cardRemoval.Position = GetOriginalCardRemovalPosition(cardRemoval, cardRemoval.Position)
			+ new Vector2(0f, CardRemovalRandomForgeOffsetY);
	}

	private static Vector2 GetOriginalCardRemovalPosition(GodotObject node, Vector2 currentPosition)
	{
		ulong instanceId = node.GetInstanceId();
		if (!CardRemovalOriginalPositions.TryGetValue(instanceId, out Vector2 originalPosition))
		{
			originalPosition = currentPosition;
			CardRemovalOriginalPositions[instanceId] = originalPosition;
		}

		return originalPosition;
	}

	private static Vector2 GetNextSlotOffset(IReadOnlyList<NMerchantRelic> relicSlots)
	{
		if (relicSlots.Count >= 2)
		{
			Vector2 offset = relicSlots[^1].Position - relicSlots[^2].Position;
			if (offset.LengthSquared() > 1f)
			{
				return offset;
			}
		}

		return new Vector2(160f, 0f);
	}

	// 跳过型前缀（已裁决保留，见 architecture.md）：原版 MerchantRelicEntry.OnTryPurchase 直接 RelicCmd.Obtain 条目模型，
	// 没有"购买时改发别的遗物"的 Hook；只对本模组的随机锻造器条目替换为"选锻造器 → 扣款 → 发放"，其余条目走原版。
	// 替换体对照原版 OnTryPurchase 的扣款/历史/同步步骤逐步复制（0.107.1/0.110.0/0.111.0），IL 由原版拷贝守卫冻结。
	[HarmonyPatch(typeof(MerchantRelicEntry), "OnTryPurchase", typeof(MerchantInventory), typeof(bool))]
	[HextechPatch("shop.random-forge.purchase", "商店随机锻造器")]
	private static class PurchasePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(MerchantRelicEntry __instance, MerchantInventory inventory, bool ignoreCost, ref Task<(bool, int)> __result)
		{
			if (!IsRandomForgeEntry(__instance))
			{
				return true;
			}

			__result = PurchaseRandomForge(__instance, inventory, ignoreCost);
			return false;
		}
	}

	// 跳过型前缀（已裁决保留）：原版补货会为该槽另抽一件遗物，随机锻造器条目要常驻，没有"本条目不补货"的 Hook。
	// 只在 ShouldRefillMerchantEntry 为真（如持有信使）时触发，仅对本模组条目跳过。
	[HarmonyPatch(typeof(MerchantRelicEntry), "RestockAfterPurchase", typeof(MerchantInventory))]
	[HextechPatch("shop.random-forge.restock", "商店随机锻造器")]
	private static class RestockPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(MerchantRelicEntry __instance)
		{
			return !IsRandomForgeEntry(__instance);
		}
	}

	// Hook.ModifyMerchantPrice 分发的非跳过前缀（已裁决保留，见 architecture.md）：只把本模组随机锻造器条目的
	// 基准价换成本局设定价，其余条目与所有监听器照常执行。原版 AbstractModel.ModifyMerchantPrice 虚方法无法等价替代：
	// 分发按"牌组 → 遗物/药水 → Modifier → 模组订阅者"迭代，会员卡/信使等折扣遗物排在 Modifier 之前，
	// 由 Modifier 覆写只能改折后价；条目模型 RandomForgeShopRelic 从未被获得、不在监听列表里；
	// 基准价 _cost 由 CalcCost 按商店 RNG 浮动生成，改它要么动 RNG 消耗、要么反射写保护字段并失去随配置实时刷新。
	[HarmonyPatch(typeof(CoreHook), nameof(CoreHook.ModifyMerchantPrice), typeof(IRunState), typeof(Player), typeof(MerchantEntry), typeof(decimal))]
	[HextechPatch("shop.random-forge.price", "商店随机锻造器")]
	private static class PricePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		private static void Prefix(MerchantEntry entry, ref decimal result)
		{
			if (TryGetRandomForgeShopRelic(entry, out RandomForgeShopRelic? shopRelic))
			{
				HextechForgeShopPriceHelper.RefreshRandomForgeShopRelic(shopRelic, shopRelic.Owner?.RunState as RunState);
				result = HextechForgeShopPriceHelper.GetRandomForgeShopPriceFor(shopRelic.Owner?.RunState as RunState);
			}
		}
	}

	// 购买后保留条目：原版 OnTryPurchaseWrapper 在 Hook.ShouldRefillMerchantEntry 为假时调用 ClearAfterPurchase 清空槽位。
	// 以前在 Hook.ShouldRefillMerchantEntry 分发上挂跳过型前缀强制返回真（会吞掉其他监听器），现改为只在本模组条目
	// 自己的 ClearAfterPurchase 上跳过：分发结果为真走 RestockAfterPurchase（上面已对本条目跳过），为假走这里，
	// 两条路都保持条目不变，其他监听器照常被询问。补丁 ID 沿用 shop.random-forge.refill。
	[HarmonyPatch(typeof(MerchantRelicEntry), "ClearAfterPurchase")]
	[HextechPatch("shop.random-forge.refill", "商店随机锻造器")]
	private static class KeepEntryAfterPurchasePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(MerchantRelicEntry __instance)
		{
			return !IsRandomForgeEntry(__instance);
		}
	}

	[HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.CreateForNormalMerchant), typeof(Player))]
	[HextechPatch("shop.random-forge.entry", "商店随机锻造器", Optional = true)]
	private static class MerchantEntryPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPostfix]
		private static void Postfix(Player player, MerchantInventory __result)
		{
			// 新商店意味着旧的卡牌移除节点已销毁,按 InstanceId 记的原坐标一并作废,避免跨商店只增不减。
			CardRemovalOriginalPositions.Clear();
			InstallRandomForgeEntry(__result, player);
		}
	}

	[HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize), typeof(MerchantInventory), typeof(MerchantDialogueSet))]
	[HextechPatch("shop.random-forge.layout", "商店随机锻造器", Optional = true)]
	private static class LayoutPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		private static void Prefix(NMerchantInventory __instance, MerchantInventory inventory)
		{
			if (__instance is NFakeMerchantInventory)
			{
				RemoveRandomForgeEntries(inventory);
				return;
			}

			InstallRandomForgeEntry(inventory, inventory.Player);
			EnsureRandomForgeRelicSlot(__instance, inventory);
		}

		[HarmonyPostfix]
		private static void Postfix(NMerchantInventory __instance, MerchantInventory inventory)
		{
			if (__instance is NFakeMerchantInventory)
			{
				return;
			}

			MoveCardRemovalBelowRandomForge(__instance, inventory);
		}
	}

	// 跳过型前缀（已裁决保留）：原版购买动画把遗物图标飞入遗物栏并清空槽位节点，随机锻造器条目只是占位图标、
	// 实际获得的是另选的锻造器，播放会留下错误的图标与空槽。纯本地表现层，仅对本模组条目跳过；目标缺失时整组可选降级。
	[HarmonyPatch(typeof(NMerchantRelic), "OnSuccessfulPurchase", typeof(PurchaseStatus), typeof(MerchantEntry))]
	[HextechPatch("shop.random-forge.purchase-animation", "商店随机锻造器", Optional = true)]
	private static class PurchaseAnimationPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => RandomForgeShopHooksAvailable;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NMerchantRelic __instance)
		{
			if (!IsRandomForgeEntry(__instance.Entry))
			{
				return true;
			}

			__instance.Entry.OnMerchantInventoryUpdated();
			HextechLog.Info("Mayhem", "Skipped merchant relic inventory animation for random forge placeholder.");
			return false;
		}
	}
}
