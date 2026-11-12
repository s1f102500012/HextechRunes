using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes;

public sealed class WatchOutGrapefruitRune : HextechSharedCombatVictoryRuneBase
{
	// 掉落档位按原版稀有度归档（见 HextechRewardRelicRoll.ToTier）：先古→稀有，商店→罕见，事件→普通。
	// 写死在这里而不是运行时读 RelicModel.Rarity，档位一目了然，原版改稀有度也不会悄悄改掉落分布。
	private static readonly (Type Type, RelicRarity Tier)[] FoodRelics =
	[
		(typeof(Strawberry), RelicRarity.Common),
		(typeof(FragrantMushroom), RelicRarity.Common), // 事件
		(typeof(BigMushroom), RelicRarity.Common), // 事件
		(typeof(ChosenCheese), RelicRarity.Common), // 事件
		(typeof(BoneTea), RelicRarity.Common), // 事件
		(typeof(EmberTea), RelicRarity.Common), // 事件
		(typeof(Pear), RelicRarity.Uncommon),
		(typeof(LastingCandy), RelicRarity.Uncommon),
		(typeof(DragonFruit), RelicRarity.Uncommon), // 商店
		(typeof(LeesWaffle), RelicRarity.Uncommon), // 商店
		(typeof(Bread), RelicRarity.Uncommon), // 商店
		(typeof(Mango), RelicRarity.Rare),
		(typeof(MeatOnTheBone), RelicRarity.Rare),
		(typeof(IceCream), RelicRarity.Rare),
		(typeof(LoomingFruit), RelicRarity.Rare), // 先古
		(typeof(YummyCookie), RelicRarity.Rare), // 先古
		(typeof(PaelsFlesh), RelicRarity.Rare), // 先古
		(typeof(NutritiousOyster), RelicRarity.Rare), // 先古
		(typeof(VeryHotCocoa), RelicRarity.Rare), // 先古
		(typeof(NutritiousSoup), RelicRarity.Rare) // 先古
	];

	private static readonly (Type Type, RelicRarity Tier) RegentFoodRelic = (typeof(LunarPastry), RelicRarity.Rare);

	private int _rewardDropCount;
	private string _recentRewardRelicIds = "";

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedRewardDropCount
	{
		get => _rewardDropCount;
		set => _rewardDropCount = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedRecentRewardRelicIds
	{
		get => _recentRewardRelicIds;
		set => _recentRewardRelicIds = value ?? "";
	}

	public override Task ApplySharedCombatVictory(CombatRoom room)
	{
		if (Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		RunState runState = (RunState)Owner.RunState;
		string playerKey = HextechStableRandom.PlayerKey(Owner);
		string dropOrdinal = _rewardDropCount.ToString();
		RelicRarity tier = HextechRewardRelicRoll.RollTier(runState, room.RoomType, "treat-yourself-food-relic", playerKey, dropOrdinal);
		List<(Type Type, RelicRarity Tier)> candidates = HextechRewardRelicRoll.SelectCandidates(
			BuildFoodRelicCandidates(
				IsRegentOwner,
				Owner.GetRelic<IceCream>() != null,
				Owner.GetRelic<NutritiousSoup>() != null),
			static food => food.Tier,
			tier,
			HextechRewardRelicRoll.ParseRecent(_recentRewardRelicIds).ToHashSet(StringComparer.Ordinal),
			static food => HextechStableRandom.TypeModelKey(food.Type));
		Type relicType = HextechStableRandom.Pick(
			candidates,
			runState,
			static food => HextechStableRandom.TypeModelKey(food.Type),
			"treat-yourself-food-relic",
			playerKey,
			dropOrdinal,
			tier.ToString()).Type;

		SavedRewardDropCount = _rewardDropCount + 1;
		SavedRecentRewardRelicIds = HextechRewardRelicRoll.PushRecent(_recentRewardRelicIds, HextechStableRandom.TypeModelKey(relicType));

		RelicModel relic = ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType)).ToMutable();
		Flash(Array.Empty<Creature>());
		room.AddExtraReward(Owner, new RelicReward(relic, Owner));
		return Task.CompletedTask;
	}

	internal static IReadOnlyList<(Type Type, RelicRarity Tier)> BuildFoodRelicCandidates(
		bool isRegent,
		bool hasIceCream,
		bool hasNutritiousSoup)
	{
		IEnumerable<(Type Type, RelicRarity Tier)> candidates = FoodRelics;
		if (isRegent)
		{
			candidates = candidates.Append(RegentFoodRelic);
		}
		if (hasIceCream)
		{
			candidates = candidates.Where(static food => food.Type != typeof(IceCream));
		}
		if (hasNutritiousSoup)
		{
			candidates = candidates.Where(static food => food.Type != typeof(NutritiousSoup));
		}
		return candidates.ToArray();
	}
}
