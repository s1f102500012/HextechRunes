namespace HextechRunes;

public sealed class GetExcitedRune : HextechRelicBase
{
	private int _pendingEnergy;
	private int _pendingDraw;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedPendingEnergy
	{
		get => _pendingEnergy;
		set => _pendingEnergy = Math.Max(0, value);
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedPendingDraw
	{
		get => _pendingDraw;
		set => _pendingDraw = Math.Max(0, value);
	}

	public override Task BeforeCombatStart()
	{
		_pendingEnergy = 2;
		_pendingDraw = 2;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_pendingEnergy = 0;
		_pendingDraw = 0;
		return Task.CompletedTask;
	}

#if STS2_113_OR_NEWER
	public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
#else
	public override Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
#endif
	{
		if (wasRemovalPrevented
			|| target.Side == Owner.Creature.Side
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target))
		{
			return Task.CompletedTask;
		}

		_pendingEnergy += 2;
		_pendingDraw += 2;
		Flash();
		return Task.CompletedTask;
	}

	public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner)
		{
			return;
		}

		int energy = _pendingEnergy;
		int draw = _pendingDraw;
		_pendingEnergy = 0;
		_pendingDraw = 0;
		if (energy > 0)
		{
			await PlayerCmd.GainEnergy(energy, player);
		}

		if (draw > 0)
		{
			await CardPileCmd.Draw(choiceContext, draw, player);
		}
	}
}
