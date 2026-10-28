using HextechRunes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunesSponsorPack;

// 战斗胜利后为随机牌添加合法附魔。合法性由 CanEnchant 判断，多重附魔的规则交由相应模组处理。
public sealed class EnchantmentMasterRune : HextechRelicBase
{
	internal const string LogTag = "EnchantmentMaster";

	private static readonly HashSet<Type> GoldEnchantmentForgeTypes =
	[
		typeof(GlamForge),
		typeof(EnchantmentForge),
		typeof(EntropyForge)
	];

	private static readonly HashSet<Type> PrismaticEnchantmentForgeTypes =
	[
		typeof(SpiralForge),
		typeof(ArcaneForge),
		typeof(EvolutionForge),
		typeof(MysticForge)
	];

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PrismaticForgeCount", 1m),
		new DynamicVar("GoldForgeCount", 2m)
	];

	public override async Task AfterObtained()
	{
		Flash();
		await HextechRunesApi.ObtainRandomForges(
			Owner,
			HextechRarityTier.Prismatic,
			DynamicVars["PrismaticForgeCount"].IntValue,
			IsPrismaticEnchantmentForge,
			"enchantment-master-prismatic");
		await HextechRunesApi.ObtainRandomForges(
			Owner,
			HextechRarityTier.Gold,
			DynamicVars["GoldForgeCount"].IntValue,
			IsGoldEnchantmentForge,
			"enchantment-master-gold");
	}

	public override bool IsAvailableForPlayer(Player player)
	{
		return true;
	}

	// 联机:本方法在所有客户端上对称执行(Hook.AfterCombatVictory 对每个玩家的遗物都分发),
	// 不能加 IsLocalPlayer / IsMine 之类的本地分支。随机源是运行种子稳定哈希(盐里含层数,
	// 所以每场战斗不同),池按 Id.Entry 有序,两端结论一致。表现层(Flash/Preview)在状态写入之后。
	public override Task AfterCombatVictory(CombatRoom room)
	{
		if (Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		List<CardModel> candidates = [];
		List<IReadOnlyList<EnchantmentModel>> candidateOptions = [];
		foreach (CardModel card in Owner.Deck.Cards)
		{
			IReadOnlyList<EnchantmentModel> legal = RandomEnchantmentPool.GetLegalEnchantments(card);
			if (legal.Count > 0)
			{
				candidates.Add(card);
				candidateOptions.Add(legal);
			}
		}

		if (candidates.Count == 0)
		{
			return Task.CompletedTask;
		}

		RunState runState = (RunState)Owner.RunState;
		string netId = Owner.NetId.ToString();
		int cardIndex = HextechRunesApi.StableIndex(runState, candidates.Count, "enchantment-master", netId, "card");
		CardModel target = candidates[cardIndex];
		IReadOnlyList<EnchantmentModel> options = candidateOptions[cardIndex];
		EnchantmentModel canonical = options[HextechRunesApi.StableIndex(runState, options.Count, "enchantment-master", netId, "enchant")];

		// 牌组里的牌全都有附魔时,候选只剩被多重附魔类模组放宽了 CanEnchant 的牌,而实际施加仍可能被原版
		// CardCmd.Enchant 或这些模组拒绝并抛异常。本方法同步执行,异常会直接打断战斗胜利钩子,战斗结束卡死;
		// 施加失败只放弃这一次附魔。原版的抛出点在写入状态之前,两端同样失败,不会分叉。
		try
		{
			CardCmd.Enchant(canonical.ToMutable(), target, 1m);
		}
		catch (Exception ex)
		{
			SponsorLog.Warn(LogTag, $"Skipped enchanting {target.Id.Entry} with {canonical.Id.Entry}: {ex.GetType().Name}: {ex.Message}");
			return Task.CompletedTask;
		}

		SponsorLog.Info(LogTag, $"Enchanted {target.Id.Entry} with {canonical.Id.Entry}");
		try
		{
			Flash();
			CardCmd.Preview(target);
		}
		catch (Exception ex)
		{
			SponsorLog.Warn(LogTag, $"Enchant preview failed for {target.Id.Entry}: {ex.GetType().Name}: {ex.Message}");
		}

		return Task.CompletedTask;
	}

	private static bool IsGoldEnchantmentForge(Type forgeType)
	{
		return GoldEnchantmentForgeTypes.Contains(forgeType);
	}

	private static bool IsPrismaticEnchantmentForge(Type forgeType)
	{
		return PrismaticEnchantmentForgeTypes.Contains(forgeType);
	}
}
