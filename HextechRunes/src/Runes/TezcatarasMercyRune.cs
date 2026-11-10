using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

public sealed class TezcatarasMercyRune : HextechSharedCombatVictoryRuneBase
{
	private int _combatCounter;
	private int _rewardDropCount;
	private string _recentRewardRelicIds = "";

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedCombatCounter
	{
		get => _combatCounter;
		set => _combatCounter = Math.Max(0, value);
	}

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

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Relics", 1m),
		new DynamicVar("CombatInterval", 3m)
	];

	public override async Task ApplySharedCombatVictory(CombatRoom room)
	{
		if (Owner.Creature.IsDead)
		{
			return;
		}

		RelicModel waxRelic = HextechAncientRelicHelper.CreateRepeatableWaxRelic(
			Owner,
			"tezcataras-mercy-wax-relic",
			room.RoomType,
			_rewardDropCount,
			HextechRewardRelicRoll.ParseRecent(_recentRewardRelicIds).ToHashSet(StringComparer.Ordinal));
		SavedRewardDropCount = _rewardDropCount + 1;
		SavedRecentRewardRelicIds = HextechRewardRelicRoll.PushRecent(_recentRewardRelicIds, waxRelic.Id.Entry);
		SaveManager.Instance.MarkRelicAsSeen(waxRelic);
		room.AddExtraReward(Owner, new HextechWaxRelicReward(waxRelic, Owner));

		SavedCombatCounter++;
		if (_combatCounter >= DynamicVars["CombatInterval"].IntValue)
		{
			SavedCombatCounter = 0;
			await MeltLeftmostWaxRelic();
		}

		Flash(Array.Empty<Creature>());
	}

	private async Task MeltLeftmostWaxRelic()
	{
		RelicModel? relic = Owner.Relics.FirstOrDefault(static relic => relic.IsWax && !relic.IsMelted);
		if (relic != null)
		{
			await RelicCmd.Melt(relic);
		}
	}
}
