#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class TearsToLaughterRune
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Sadness>(),
		HoverTipFactory.FromCard<Joy>()
	];

	// 时机取原版 CombatManager.FlushPlayerHand 弃完手牌之后的 AfterFlush：悲伤自带保留仍在手里，
	// 变出的喜悦（不带保留）不会在本回合结束时被弃掉，留到下个回合可用。只在持有者自己结束回合时触发。
	// 手牌里的悲伤全部变化，按手牌顺序；变化照原版 EcstasyPower：CreateCard<Joy> + CardCmd.Transform。
	public override async Task AfterFlush(PlayerChoiceContext choiceContext, Player player, IReadOnlyCollection<CardModel> flushedCards, IReadOnlyCollection<CardModel> retainedCards)
	{
		if (player != Owner
			|| Owner.Creature.IsDead
			|| !IsFoolPlayer(Owner)
			|| Owner.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		List<CardModel> sadness = GetSadnessInHand();
		if (sadness.Count == 0)
		{
			return;
		}

		List<CardTransformation> transformations = sadness
			.Select(card => new CardTransformation(card, combatState.CreateCard<Joy>(Owner)))
			.ToList();
		Flash();
		await CardCmd.Transform(transformations, null);
	}

	internal List<CardModel> GetSadnessInHand()
	{
		return PileType.Hand.GetPile(Owner).Cards.Where(static card => card is Sadness).ToList();
	}
}
#endif
