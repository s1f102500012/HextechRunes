using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Rewards;

namespace HextechRunes;

public sealed class HundredRefinementsRune : HextechRelicBase
{
	private const string BodyForgeOptionId = "HEXTECH_BODY_FORGE";
	private int _bodyForgeCount;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBodyForgeCount
	{
		get => _bodyForgeCount;
		set
		{
			_bodyForgeCount = Math.Max(0, value);
			InvokeDisplayAmountChanged();
		}
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("BodyForges", 2m),
		new DynamicVar("ForgeCount", 1m)
	];

	public override bool ShowCounter => true;

	public override int DisplayAmount => !IsCanonical ? _bodyForgeCount % BodyForgeThreshold : 0;

	public override bool TryModifyCardRewardAlternatives(Player player, CardReward cardReward, List<CardRewardAlternative> alternatives)
	{
		if (player != Owner)
		{
			return false;
		}

		// 多个「百炼成钢」共用一个锻体按钮:仅由第一个实例添加,点击时同步推进所有实例的计数
		// (与佩尔之翼把多个牺牲按钮合并成一个同理),而不是每个海克斯各一个按钮、各自计数。
		if (GetActiveRunes(Owner).FirstOrDefault() != this)
		{
			return false;
		}

		alternatives.Add(new CardRewardAlternative(
			BodyForgeOptionId,
			ForgeBodyForAllInstances,
			PostAlternateCardRewardAction.EndSelectionAndCompleteReward));
		return true;
	}

	private async Task ForgeBodyForAllInstances()
	{
		foreach (HundredRefinementsRune rune in GetActiveRunes(Owner))
		{
			await rune.ForgeBodyOnce();
		}
	}

	private async Task ForgeBodyOnce()
	{
		_bodyForgeCount++;
		InvokeDisplayAmountChanged();
		Flash();
		if (_bodyForgeCount % BodyForgeThreshold != 0)
		{
			return;
		}

		// 锻体顶替的是卡牌奖励，换来的锻造器按奖励算，复视照常复制。
		Player owner = Owner;
		_ = await HextechForgeGrantHelper.TryObtainRandomForges(
			owner,
			Math.Max(0, DynamicVars["ForgeCount"].IntValue),
			afterObtained: forge => DoubleVisionRune.DuplicateReplicatedForge(owner, forge));
	}

	private static IReadOnlyList<HundredRefinementsRune> GetActiveRunes(Player player)
	{
		return player.Relics.OfType<HundredRefinementsRune>().ToList();
	}

	private int BodyForgeThreshold => Math.Max(1, DynamicVars["BodyForges"].IntValue);
}
