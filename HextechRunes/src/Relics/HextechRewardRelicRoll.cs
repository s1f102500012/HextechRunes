using MegaCrit.Sts2.Core.Entities.Relics;

namespace HextechRunes;

/// <summary>
/// 战斗胜利额外掉落遗物（当心小蛋糕、特兹卡塔拉的慈爱）共用的稀有度骰与去重。
/// 稀有度按战斗类型加权，用海克斯自己的稳定随机，不消耗原版奖励随机数；
/// 最近掉过的遗物记在海克斯实例上（存档、联机校验都覆盖），候选够用时排除。
/// </summary>
internal static class HextechRewardRelicRoll
{
	internal const int RecentDropMemory = 2;

	private const char RecentSeparator = '|';

	// 普通 / 罕见 / 稀有，合计 100。非精英、非 Boss 的战斗按普通战斗算。
	internal static (int Common, int Uncommon, int Rare) GetTierWeights(RoomType roomType)
	{
		return roomType switch
		{
			RoomType.Elite => (35, 40, 25),
			RoomType.Boss => (10, 40, 50),
			_ => (60, 30, 10)
		};
	}

	internal static RelicRarity TierFromRoll(RoomType roomType, int roll)
	{
		(int common, int uncommon, _) = GetTierWeights(roomType);
		if (roll < common)
		{
			return RelicRarity.Common;
		}

		return roll < common + uncommon
			? RelicRarity.Uncommon
			: RelicRarity.Rare;
	}

	internal static RelicRarity RollTier(RunState runState, RoomType roomType, params string?[] saltParts)
	{
		int roll = HextechStableRandom.Index(runState, 100, [.. saltParts, "tier", roomType.ToString()]);
		return TierFromRoll(roomType, roll);
	}

	// 原版稀有度归档：先古算稀有，商店算罕见，事件算普通。
	internal static RelicRarity ToTier(RelicRarity rarity)
	{
		return rarity switch
		{
			RelicRarity.Rare or RelicRarity.Ancient => RelicRarity.Rare,
			RelicRarity.Uncommon or RelicRarity.Shop => RelicRarity.Uncommon,
			_ => RelicRarity.Common
		};
	}

	/// <summary>
	/// 先在骰中的稀有度里找最近没掉过的；没有就按距离依次退到相邻稀有度（同距离先退低档）；
	/// 全池都刚掉过时才允许重复，优先骰中的稀有度。
	/// </summary>
	internal static List<T> SelectCandidates<T>(
		IReadOnlyList<T> pool,
		Func<T, RelicRarity> tierOf,
		RelicRarity tier,
		IReadOnlyCollection<string> recentKeys,
		Func<T, string> keySelector)
	{
		List<T> fresh = pool.Where(item => !recentKeys.Contains(keySelector(item))).ToList();
		foreach (RelicRarity candidateTier in TierFallbackOrder(tier))
		{
			List<T> tiered = fresh.Where(item => tierOf(item) == candidateTier).ToList();
			if (tiered.Count > 0)
			{
				return tiered;
			}
		}

		List<T> sameTier = pool.Where(item => tierOf(item) == tier).ToList();
		return sameTier.Count > 0 ? sameTier : pool.ToList();
	}

	internal static IReadOnlyList<RelicRarity> TierFallbackOrder(RelicRarity tier)
	{
		return tier switch
		{
			RelicRarity.Rare => [RelicRarity.Rare, RelicRarity.Uncommon, RelicRarity.Common],
			RelicRarity.Uncommon => [RelicRarity.Uncommon, RelicRarity.Common, RelicRarity.Rare],
			_ => [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare]
		};
	}

	internal static IReadOnlyList<string> ParseRecent(string? saved)
	{
		return string.IsNullOrEmpty(saved)
			? Array.Empty<string>()
			: saved.Split(RecentSeparator, StringSplitOptions.RemoveEmptyEntries);
	}

	internal static string PushRecent(string? saved, string key)
	{
		IEnumerable<string> recent = ParseRecent(saved).Append(key);
		return string.Join(RecentSeparator, recent.TakeLast(RecentDropMemory));
	}
}
