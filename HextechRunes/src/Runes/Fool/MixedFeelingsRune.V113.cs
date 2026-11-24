#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class MixedFeelingsRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Sadness>(),
		HoverTipFactory.FromCard<Joy>()
	];

	// "生成悲伤"口径同苦中作乐（原版万事无忧、肥料）：每张持有者生成并进入持有者牌堆的悲伤触发一次。
	// 喜悦走原版生成命令加入手牌（同升级：熔融之拳"在你的手牌中添加"的写法）；手牌已满时原版改放弃牌堆。
	// 生成会再派发一次生成钩子，但那张牌是喜悦而不是悲伤，不会自我触发。
	public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
	{
		if (!IsOwnSadnessGeneration(card, creator)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		int count = DynamicVars.Cards.IntValue;
		if (count <= 0)
		{
			return;
		}

		List<CardModel> joys = Enumerable.Range(0, count)
			.Select(_ => (CardModel)combatState.CreateCard<Joy>(Owner))
			.ToList();
		Flash();
		await HextechCardGeneration.AddGeneratedCardsToCombat(joys, PileType.Hand, addedByPlayer: true);
	}

	internal bool IsOwnSadnessGeneration(CardModel card, Player? creator)
	{
		return creator == Owner
			&& card is Sadness
			&& card.Owner == Owner
			&& !Owner.Creature.IsDead
			&& IsFoolPlayer(Owner);
	}
}
#endif
