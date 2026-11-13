#if STS2_113_OR_NEWER
namespace HextechRunes;

public sealed partial class CrystalBallRune
{
	// 本场战斗内的触发序号，参与稳定随机的盐；每次真实加牌推进一次，各端在同一钩子里对称推进。
	private int _addOrdinal;

	protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Tarot)];

	// 原版 TeaOfDiscourtesy 同样在 BeforeCombatStart 往抽牌堆加牌：此时牌堆已建好、战斗已在进行，
	// 加入的牌参与第一回合抽牌。
	public override Task BeforeCombatStart()
	{
		_addOrdinal = 0;
		return AddTarotCardsAsync();
	}

	// 原版 CardPileCmd.Shuffle 把弃牌堆洗进抽牌堆之后才派发 AfterShuffle，此时加入随机位置的牌留在洗好的抽牌堆里，
	// 不会被这次洗牌带走，也不会再次触发洗牌（同 DizzySpinningRune）。只认持有者自己的洗牌。
	public override Task AfterShuffle(PlayerChoiceContext choiceContext, Player shuffler)
	{
		return shuffler == Owner ? AddTarotCardsAsync() : Task.CompletedTask;
	}

	private async Task AddTarotCardsAsync()
	{
		if (Owner.Creature.IsDead || !IsFoolPlayer(Owner))
		{
			return;
		}

		int ordinal = _addOrdinal++;
		Flash();
		await HextechTarotCardGeneration.AddRandomTarotCardsToDrawPile(
			Owner,
			DynamicVars.Cards.IntValue,
			"crystal-ball",
			HextechStableRandom.PlayerKey(Owner),
			"turn",
			Owner.PlayerCombatState?.TurnNumber.ToString(),
			"ordinal",
			ordinal.ToString());
	}
}
#endif
