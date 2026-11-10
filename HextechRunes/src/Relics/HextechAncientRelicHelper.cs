using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace HextechRunes;

internal static class HextechAncientRelicHelper
{
	private static readonly Type[] NonupeipeRelicTypes =
	[
		typeof(BlessedAntler),
		typeof(BrilliantScarf),
		typeof(DelicateFrond),
		typeof(DiamondDiadem),
		typeof(FurCoat),
		typeof(Glitter),
		typeof(JewelryBox),
		typeof(LoomingFruit),
		typeof(SignetRing),
		typeof(BeautifulBracelet)
	];

	private static readonly Type[] VakuuRelicTypes =
	[
		typeof(BloodSoakedRose),
		typeof(ChoicesParadox),
		typeof(DistinguishedCape),
		typeof(Fiddle),
		typeof(JeweledMask),
		typeof(LordsParasol),
		typeof(MusicBox),
		typeof(PreservedFog),
		typeof(SereTalon),
		typeof(WhisperingEarring)
	];

	public static RelicModel CreateRandomNonupeipeRelic(Player player, string source)
	{
		return CreateRandomAncientRelic(NonupeipeRelicTypes, player, source);
	}

	// 逐件获得时依次调用：上一件入手后遗物数与已持有集合都变了，下一件自然不会重复。
	public static RelicModel CreateRandomVakuuRelic(Player player, string source)
	{
		return CreateRandomAncientRelic(VakuuRelicTypes, player, source);
	}

	private static RelicModel CreateRandomAncientRelic(Type[] pool, Player player, string source)
	{
		Type[] candidates = pool
			.Where(type => !HasRelic(player, type))
			.ToArray();
		if (candidates.Length == 0)
		{
			candidates = pool;
		}

		Type relicType = HextechStableRandom.Pick(
			candidates,
			(RunState)player.RunState,
			HextechStableRandom.TypeModelKey,
			source,
			HextechStableRandom.PlayerKey(player),
			player.Relics.Count.ToString());
		return ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType)).ToMutable();
	}

	// 稀有度按战斗类型加权（HextechRewardRelicRoll），不再走原版 RelicFactory.RollRarity，
	// 免得消耗 PlayerRng.Rewards 改变原版后续奖励序列；最近掉过的蜡制遗物在候选够用时排除。
	public static RelicModel CreateRepeatableWaxRelic(
		Player player,
		string source,
		RoomType roomType,
		int dropOrdinal,
		IReadOnlyCollection<string> recentRelicIds)
	{
		RunState runState = (RunState)player.RunState;
		string playerKey = HextechStableRandom.PlayerKey(player);
		RelicRarity tier = HextechRewardRelicRoll.RollTier(runState, roomType, source, playerKey, dropOrdinal.ToString());
		List<RelicModel> candidates = HextechRewardRelicRoll.SelectCandidates(
			BuildRepeatableRewardRelicPool(player),
			static relic => HextechRewardRelicRoll.ToTier(relic.Rarity),
			tier,
			recentRelicIds,
			static relic => relic.Id.Entry);

		RelicModel relic = HextechStableRandom.Pick(
			candidates,
			runState,
			static relic => relic.Id.Entry,
			source,
			playerKey,
			dropOrdinal.ToString(),
			tier.ToString()).ToMutable();
		relic.IsWax = true;
		return relic;
	}

	private static List<RelicModel> BuildRepeatableRewardRelicPool(Player player)
	{
		IEnumerable<RelicModel> rewardRelics = ModelDb.RelicPool<SharedRelicPool>()
			.GetUnlockedRelics(player.UnlockState)
			.Concat(player.Character.RelicPool.GetUnlockedRelics(player.UnlockState));

		return rewardRelics
			.Where(static relic => relic.Rarity is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare)
			.GroupBy(static relic => relic.Id)
			.Select(static group => group.First())
			.OrderBy(static relic => relic.Id.Entry, StringComparer.Ordinal)
			.ToList();
	}

	private static bool HasRelic(Player player, Type relicType)
	{
		ModelId relicId = ModelDb.GetId(relicType);
		return player.Relics.Any(relic => relic.CanonicalId() == relicId);
	}
}
