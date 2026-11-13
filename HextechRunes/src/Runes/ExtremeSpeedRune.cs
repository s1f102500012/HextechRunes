using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace HextechRunes;

public sealed class ExtremeSpeedRune : HextechRelicBase
{
	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("MaxHpLoss", 5m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Adrenaline>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterObtained()
	{
		List<CardModel> transformableCards = Owner.Deck.Cards
			.Where(static card => card.IsTransformable)
			.ToList();
		if (transformableCards.Count == 0)
		{
			return;
		}

		IEnumerable<CardModel> selected = await CardSelectCmd.FromDeckGeneric(
			Owner,
			new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 0, transformableCards.Count)
			{
				Cancelable = true,
				RequireManualConfirmation = true
			},
			static card => card.IsTransformable);

		List<CardTransformation> transformations = selected
			.Select(card => CardTransformUpgradeHelper.CreateFixedReplacementTransformation(
				card,
				Owner.RunState.CreateCard<Adrenaline>(Owner)))
			.ToList();
		if (transformations.Count == 0)
		{
			return;
		}

		Flash();
		await CardCmd.Transform(transformations, null, CardPreviewStyle.GridLayout);
		await HextechV113ApiCompat.LoseMaxHpNotFromCard(
			new BlockingPlayerChoiceContext(),
			Owner.Creature,
			transformations.Count * DynamicVars["MaxHpLoss"].BaseValue);
	}
}
