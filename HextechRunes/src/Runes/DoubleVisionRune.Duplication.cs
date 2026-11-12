using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

public sealed partial class DoubleVisionRune
{
	private async Task DuplicateRewardCards(IReadOnlyList<CardModel> sourceCards)
	{
		if (Owner.Creature.IsDead)
		{
			return;
		}

		List<CardPileAddResult> results = new();
		foreach (CardModel sourceCard in sourceCards)
		{
			if (sourceCard.Owner != Owner || !Owner.RunState.ContainsCard(sourceCard))
			{
				continue;
			}

			CardModel copy = Owner.RunState.CloneCard(sourceCard);
			CardPileAddResult result = await RunWithCommandDuplicationSuppressed(
				() => CardPileCmd.Add(copy, PileType.Deck, clonedBy: this));
			if (!result.success)
			{
				continue;
			}

			SaveManager.Instance.MarkCardAsSeen(result.cardAdded);
			TrySyncObtainedReward("card", result.cardAdded.Id.Entry, () => RunManager.Instance.RewardSynchronizer.SyncLocalObtainedCard(result.cardAdded));
			results.Add(result);
		}

		if (results.Count > 0)
		{
			Flash();
			CardCmd.PreviewCardPileAdd(results, 2f);
		}
	}

	private async Task DuplicateGoldReward(Player player, GoldReward reward)
	{
		if (reward.Amount <= 0)
		{
			return;
		}

		bool wasGoldStolenBack = GoldRewardWasStolenBackField?.GetValue(reward) is true;
		await DuplicateGoldAmount(player, reward.Amount, wasGoldStolenBack);
	}

	private async Task DuplicateGoldAmount(Player player, int amount, bool wasGoldStolenBack)
	{
		if (amount <= 0)
		{
			return;
		}

		Flash();
		await RunWithCommandDuplicationSuppressed(
			() => PlayerCmd.GainGold(amount, player, wasGoldStolenBack));
		TrySyncObtainedReward("gold", amount.ToString(), () => RunManager.Instance.RewardSynchronizer.SyncLocalObtainedGold(amount));
	}

	private async Task DuplicatePotionReward(Player player, PotionReward reward)
	{
		PotionModel? claimedPotion = reward.ClaimedPotion;
		if (claimedPotion == null)
		{
			return;
		}

		await DuplicateObtainedPotion(player, claimedPotion);
	}

	private async Task DuplicateObtainedPotion(Player player, PotionModel sourcePotion)
	{
		PotionModel copy = ModelDb.GetById<PotionModel>(sourcePotion.CanonicalId()).ToMutable();
		PotionProcureResult result = await RunWithCommandDuplicationSuppressed(
			() => PotionCmd.TryToProcure(copy, player));
		if (!result.success)
		{
			return;
		}

		Flash();
		TrySyncObtainedReward("potion", result.potion.Id.Entry, () => RunManager.Instance.RewardSynchronizer.SyncLocalObtainedPotion(result.potion));
	}

	private async Task DuplicateRelicReward(Player player, RelicReward reward)
	{
		RelicModel? claimedRelic = reward.ClaimedRelic;
		if (claimedRelic == null)
		{
			return;
		}

		await DuplicateObtainedRelic(player, claimedRelic);
	}

	private async Task DuplicateObtainedRelic(Player player, RelicModel sourceRelic, bool syncReward = true)
	{
		// 此通道排除海克斯基类、内容注册表成员及本模组系列程序集中的遗物，
		// 避免重复执行符文与锻造器的拾取流程；锻造奖励和商店锻造另有专用复制入口。
		// 三项并不互相覆盖：注册表只认已登记的 ID(外部基于 HextechRelicBase 的符文登记失败时查不到)，
		// 欧洛巴斯二次升级等本体遗物既不继承 HextechRelicBase 也不在符文注册表里，
		// 程序集前缀同时覆盖本体与拓展包(HextechRunesSponsorPack)。
		// 其他原版或外部遗物继续接受下方的逐类限制，并非只允许原版遗物。
		if (sourceRelic is HextechRelicBase
			|| HextechCatalog.IsHextechCustomRelic(sourceRelic)
			|| sourceRelic.GetType().Assembly.GetName().Name?.StartsWith(ModInfo.Id, StringComparison.Ordinal) == true)
		{
			return;
		}

		if (sourceRelic is DustyTome dustyTome)
		{
			await DuplicateDustyTome(player, dustyTome, syncReward);
			return;
		}

		// 复视不对 Orobas 先古遗物「古老牙齿」「欧洛巴斯之触」生效（不复制它们）：
		// 它们的获得/转化流程不适合被复制（古老牙齿重复获得会因牌组无可转化牌而卡死）。
		// 黄金罗盘（GoldenCompass）同样跳过：其 AfterObtained 会 await RunManager.GenerateMap() 重建全图、
		// 消耗共享 RNG 并改写共享地图。即使事件事务在所有端执行,多人各玩家事件任务仍可能并发完成,
		// 不能把全局地图重建挂到玩家级奖励事务里。
		// 先古遗物本就不该被双倍，复制出第二枚黄金罗盘语义上也不成立。
		if (sourceRelic is ArchaicTooth or TouchOfOrobas or GoldenCompass)
		{
			return;
		}

		ModelId sourceId = sourceRelic.CanonicalId();
		RelicModel? canonical = sourceId == ModelId.none
			? null
			: ModelDb.GetByIdOrNull<RelicModel>(sourceId);
		if (canonical == null)
		{
			HextechLog.Warn(
				"DoubleVision", $"Skipped relic duplication because its model is not registered: "
				+ $"player={player.NetId} relic={sourceId.Entry} type={sourceRelic.GetType().FullName}.");
			return;
		}

		RelicModel copy = canonical.ToMutable();
		CopyWaxState(sourceRelic, copy);
		RelicModel obtained = await RunWithCommandDuplicationSuppressed(
			() => RelicCmd.Obtain(copy, player));
		if (syncReward)
		{
			TrySyncObtainedReward("relic", obtained.Id.Entry, () => RunManager.Instance.RewardSynchronizer.SyncLocalObtainedRelic(obtained));
		}
		if (LocalContext.IsMe(player))
		{
			Flash();
		}
	}

	private async Task DuplicateDustyTome(Player player, DustyTome sourceTome, bool syncReward)
	{
		if (sourceTome.AncientCard is not { } ancientCardId)
		{
			HextechLog.Warn("DoubleVision", $"Refused to duplicate Dusty Tome without an AncientCard.");
			return;
		}

		DustyTome copy = (DustyTome)ModelDb.GetById<RelicModel>(sourceTome.CanonicalId()).ToMutable();
		CopyWaxState(sourceTome, copy);
		copy.AncientCard = ancientCardId;
		RelicModel obtained = await RunWithDustyTomeAfterObtainedSuppressed(
			copy,
			() => RunWithCommandDuplicationSuppressed(() => RelicCmd.Obtain(copy, player)));
		if (syncReward)
		{
			TrySyncObtainedReward("relic", obtained.Id.Entry, () => RunManager.Instance.RewardSynchronizer.SyncLocalObtainedRelic(obtained));
		}
		if (LocalContext.IsMe(player))
		{
			Flash();
		}
	}

	internal static void CopyWaxState(RelicModel source, RelicModel copy)
	{
		copy.IsWax = source.IsWax;
	}

	internal static bool ShouldSuppressDustyTomeAfterObtained(DustyTome dustyTome)
	{
		return ReferenceEquals(SuppressedDustyTomeAfterObtained.Value, dustyTome);
	}

	// 复制份只拿遗物本身：原版 DustyTome.AfterObtained 会再发一张先古卡，复制份不应重复发放。
	// 抑制只认这一个实例，原件和同时获得的其他尘封之书照常结算。
	internal static async Task<RelicModel> RunWithDustyTomeAfterObtainedSuppressed(
		DustyTome dustyTome,
		Func<Task<RelicModel>> action)
	{
		DustyTome? previous = SuppressedDustyTomeAfterObtained.Value;
		SuppressedDustyTomeAfterObtained.Value = dustyTome;
		try
		{
			return await action();
		}
		finally
		{
			SuppressedDustyTomeAfterObtained.Value = previous;
		}
	}

	private async Task DuplicateForgeReward(Player player, HextechForgeChoiceReward reward)
	{
		await DuplicateForgeById(player, reward.ClaimedForgeId);
	}

	// 商店购买的属性锻造器不走 AfterRewardTaken/HextechForgeChoiceReward,而直接 RelicCmd.Obtain 又会被
	// DuplicateObtainedRelic 的「本模组程序集」闸门跳过(锻造器全是本模组类型)。因此由商店购买流程在成功获得后显式调用本入口,为玩家持有的每个复视各复制一份,复用与锻造奖励完全相同的
	// ObtainSelectedForge(syncObtainedRelic) 路径。GetActiveRunes 已含「本地持有者」联机闸门(远端由广播兜底)。
	internal static async Task DuplicatePurchasedForge(Player player, RelicModel forge)
	{
		ModelId forgeId = forge.CanonicalId();
		if (forgeId == ModelId.none)
		{
			return;
		}

		foreach (DoubleVisionRune rune in GetActiveRunes(player))
		{
			await rune.DuplicateForgeById(player, forgeId);
		}
	}

	// 卡牌奖励的替代选项（百炼成钢的锻体）在所有端按同步选择对称执行，原锻造器本身也是各端各自获得、不广播。
	// 复制份照同样方式在所有端各自获得:不走本地持有者闸门和 syncObtainedRelic 广播,否则远端会多拿一份。
	// 附魔锻造器拾起时的选牌走选择同步协议,所有端一起进入,与原锻造器一致。
	internal static async Task DuplicateReplicatedForge(Player player, RelicModel forge)
	{
		ModelId forgeId = forge.CanonicalId();
		if (forgeId == ModelId.none)
		{
			return;
		}

		foreach (DoubleVisionRune rune in GetEventActiveRunes(player))
		{
			RelicModel copy = ModelDb.GetById<RelicModel>(forgeId).ToMutable();
			rune.Flash();
			await RunWithCommandDuplicationSuppressed(
				() => HextechForgeGrantHelper.ObtainSelectedForge(player, copy, syncObtainedRelic: false));
		}
	}

	private async Task DuplicateForgeById(Player player, ModelId forgeId)
	{
		// (B1)附魔锻造的 AfterObtained 会开交互式选牌(FromDeckForEnchantment),必须走「持有者开UI+SyncLocalChoice、
		// 远端 WaitForRemoteChoice」的选择同步协议(每次选牌都 ReserveChoiceId)。复制份只能在【本地持有者】这一端
		// 真正开第二次选牌,再靠 ObtainSelectedForge 的 syncObtainedRelic 广播让远端经 RewardSynchronizer 获得这份
		// 锻造并 WaitForRemoteChoice 回放同一选牌——这是原版 HextechForgeChoiceReward.OnSelect(仅选取端运行)的镜像。
		// 缺这道本地闸门时,远端也各自跑 ObtainSelectedForge 开自己的选牌→复制份 choiceId 与持有者错位→远端拿到
		// Index 型结果→AsDeckCards 抛异常(玩家实测黑屏/卡的来源之一)。非本地持有者直接返回,由广播兜底。
		if (forgeId == ModelId.none || !ShouldDuplicateForPlayer(player))
		{
			return;
		}

		RelicModel forge = ModelDb.GetById<RelicModel>(forgeId).ToMutable();
		Flash();
		await RunWithCommandDuplicationSuppressed(
			() => HextechForgeGrantHelper.ObtainSelectedForge(player, forge, syncObtainedRelic: true));
	}
}
