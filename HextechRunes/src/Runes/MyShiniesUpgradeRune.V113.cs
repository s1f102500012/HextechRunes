#if STS2_113_OR_NEWER
namespace HextechRunes;

/// <summary>
/// 升级：我的小钱钱：持有者打出的我的小钱钱每段命中敌人时，按这一段实际扣掉的生命（未被格挡、不含过量伤害）获得金币。
/// 原版我的小钱钱只存在于 0.113.0，旧变体的分部（<c>MyShiniesUpgradeRune.Legacy.cs</c>）只是拿不到的壳；两个分部都不声明 SavedProperty。
/// 命中段数在打出时按当时金币一次算定，这次打出赚到的金币只影响之后的打出。
/// </summary>
public sealed partial class MyShiniesUpgradeRune : CardUpgradeRuneBase<MyShinies>
{
	private bool _grantingGold;
	// 本次打出是否已经播过一次金币音效与闪烁；纯本地表现，不进存档、不参与联机。
	private bool _feedbackShownThisPlay;

	protected override bool IsAvailableForCharacter(Player player) => true;

	// 每次打出（含重放）开始时重置：一次打出的多段命中只响一次金币音效、只闪一次。
	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (cardPlay.Card is MyShinies && cardPlay.Card.Owner == Owner)
		{
			_feedbackShownThisPlay = false;
		}

		return Task.CompletedTask;
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (_grantingGold
			|| target.Side != CombatSide.Enemy
			|| result.UnblockedDamage <= 0
			|| cardSource is not MyShinies
			|| cardSource.Owner != Owner)
		{
			return;
		}

		bool firstFeedbackThisPlay = !_feedbackShownThisPlay;
		_feedbackShownThisPlay = true;

		// 战斗内发金币的联机路径：各端在同一条伤害链里等待原版 GainGold，发钱期间禁止自身重入。
		// 每段命中都立即发钱（战斗在这次打出中途结束时原版不再派发出牌结束钩子，攒到结束再发会丢），只是把音效与闪烁合并成每次打出一次。
		_grantingGold = true;
		try
		{
			await GoldrendRune.GainGoldAsync(result.UnblockedDamage, Owner, muteGoldSfx: !firstFeedbackThisPlay);
			if (firstFeedbackThisPlay)
			{
				Flash();
			}
		}
		finally
		{
			_grantingGold = false;
		}
	}
}
#endif
