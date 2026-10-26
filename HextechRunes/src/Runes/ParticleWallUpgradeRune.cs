namespace HextechRunes;

public sealed class ParticleWallUpgradeRune : CardUpgradeRuneBase<ParticleWall>
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new StarsVar(1)
	];

	protected override bool IsAvailableForCharacter(Player player) => IsRegentPlayer(player);

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner == Owner && cardPlay.Card is ParticleWall)
		{
			Flash();
			await PlayerCmd.GainStars(DynamicVars.Stars.BaseValue, Owner);
		}
	}
}
