#if STS2_108_OR_NEWER
namespace HextechRunes;

/// <summary>
/// 升级：群情激愤（仅铁甲战士、仅联机）：本场战斗中每有玩家打出一次群情激愤，所有群情激愤的攻击伤害 +1。
/// 加成不写进卡牌：每次结算都按本场战斗出牌历史里群情激愤的打出次数（任意玩家、含复制品）现算，
/// 走伤害加法修正，所以所有人的卡面预览、手里与牌堆里现有的和之后生成的复制品都一致，各端只取决于同步的出牌历史。
/// 口径照原版动量/魔球：打出结算之后才计入，重放与自动打出各算一次（原版出牌历史按每次打出记录）。
/// 多名持有者各自提供一份；持有者死亡后不再参与钩子，加成随之消失，复活后按历史恢复。
/// 0.107.1 没有群情激愤，旧变体的分部（<c>OutrageUpgradeRune.Legacy.cs</c>）只是拿不到的壳；两个分部都不声明 SavedProperty。
/// </summary>
public sealed partial class OutrageUpgradeRune : CardUpgradeRuneBase<Outrage>
{
	protected override bool IsAvailableForCharacter(Player player) => IsNetworkMultiplayer() && IsIroncladPlayer(player);

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card is Outrage)
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	public override decimal ModifyDamageAdditiveCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		// 与原版动量同口径：只加在强化攻击上。
		return cardSource is Outrage && props.IsPoweredAttack()
			? CountOutragePlaysThisCombat()
			: 0m;
	}

	internal static int CountOutragePlaysThisCombat()
	{
		return CombatManager.Instance.History.Entries
			.OfType<CardPlayFinishedEntry>()
			.Count(static entry => entry.CardPlay.Card is Outrage);
	}
}
#endif
