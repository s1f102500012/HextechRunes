#if STS2_113_OR_NEWER
namespace HextechRunes;

/// <summary>
/// 开嗓"直接拥有旋律"的查询，以及让原版旋律判定认得这些旋律的补丁。
/// 原版旋律不是状态：<see cref="MelodyCmd.PlayedThisTurn"/> 从战斗历史里找本回合带该音节的出牌
/// （0.113.x 看已结算完的出牌，0.114.0 起看已开始打出的）；
/// <c>PlayedWah/Cho/KiThisTurn</c>、<c>MelodiesThisTurn</c> 与愚者战斗界面的旋律符号都经它。
/// 直接拥有的旋律没有出牌记录，按出牌历史计数的"获得旋律时"（曲中意、失落鲁特琴、暴力和弦、歌之道）不会触发，开嗓也不补结算。
/// 状态全部记在持有者的遗物实例上，本类只汇总查询，没有静态可变字段。
/// </summary>
internal static class HextechGrantedMelodies
{
	internal static readonly CardTag[] MelodyTags = [CardTag.Wah, CardTag.Cho, CardTag.Ki];

	// 界面刷新也会走到这里：只读遗物字段与回合号，不推进随机、不改状态。
	// 开嗓一次拥有全部三种旋律，不分音节。
	internal static bool IsGrantedThisTurn(Player player)
	{
		foreach (RelicModel relic in player.Relics)
		{
			if (relic is OpeningNoteRune openingNote && openingNote.GrantsMelodyThisTurn())
			{
				return true;
			}
		}

		return false;
	}

	// 只在持有者自己阵营的回合里算"本回合"，与原版历史条目按回合与阵营比对一致（敌方回合不拥有旋律）。
	internal static bool IsOwnersSideTurn(Player owner)
	{
		Creature? creature = owner.Creature;
		return creature?.CombatState != null
			&& creature.CombatState.CurrentSide == creature.Side
			&& owner.PlayerCombatState != null;
	}

	// 目标方法体（闭包 + LINQ + 带插值的抛出）远超 JIT 内联阈值，调用方不会把它内联掉而绕过补丁。
	// 原版对非旋律标签先抛异常，后缀不会对非法标签执行。
	[HarmonyPatch(typeof(MelodyCmd), nameof(MelodyCmd.PlayedThisTurn))]
	[HextechPatch("fool.granted-melodies", "直接拥有旋律", Rune = typeof(OpeningNoteRune))]
	private static class PlayedThisTurnPatch
	{
		[HarmonyPostfix]
		private static void Postfix(Player player, ref bool __result)
		{
			if (!__result && IsGrantedThisTurn(player))
			{
				__result = true;
			}
		}
	}
}
#endif
