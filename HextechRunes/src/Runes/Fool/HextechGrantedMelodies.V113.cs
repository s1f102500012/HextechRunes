#if STS2_113_OR_NEWER
namespace HextechRunes;

/// <summary>
/// 开嗓、引吭高歌"直接拥有旋律"的汇总查询，以及让原版旋律判定认得这些旋律的补丁。
/// 原版旋律不是状态：<see cref="MelodyCmd.PlayedThisTurn"/> 从战斗历史里找本回合已结算完、带该音节的出牌；
/// <c>PlayedWah/Cho/KiThisTurn</c>、<c>MelodiesThisTurn</c> 与愚者战斗界面的旋律符号都经它。
/// 直接给予的旋律没有出牌记录，按出牌历史计数的"获得旋律时"（曲中意、失落鲁特琴、暴力和弦、歌之道）不会自己触发；
/// 引吭高歌给予旋律时由它自己结算这四个能力一次，开嗓的"拥有"不结算。
/// 状态全部记在持有者的遗物实例上，本类只汇总查询，没有静态可变字段。
/// </summary>
internal static class HextechGrantedMelodies
{
	internal static readonly CardTag[] MelodyTags = [CardTag.Wah, CardTag.Cho, CardTag.Ki];

	// 界面刷新也会走到这里：只读遗物字段与回合号，不推进随机、不改状态。
	internal static bool IsGrantedThisTurn(Player player, CardTag melodyTag)
	{
		foreach (RelicModel relic in player.Relics)
		{
			if (relic is OpeningNoteRune openingNote && openingNote.GrantsMelodyThisTurn())
			{
				return true;
			}

			if (relic is SingOutRune singOut && singOut.GrantsMelodyThisTurn(melodyTag))
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
	[HextechPatch("fool.granted-melodies", "直接拥有旋律", Runes = [typeof(OpeningNoteRune), typeof(SingOutRune)])]
	private static class PlayedThisTurnPatch
	{
		[HarmonyPostfix]
		private static void Postfix(Player player, CardTag cardTag, ref bool __result)
		{
			if (!__result && IsGrantedThisTurn(player, cardTag))
			{
				__result = true;
			}
		}
	}
}
#endif
