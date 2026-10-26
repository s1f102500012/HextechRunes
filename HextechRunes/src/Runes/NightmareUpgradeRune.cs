namespace HextechRunes;

public sealed class NightmareUpgradeRune : CardUpgradeRuneBase<Nightmare>
{
	protected override bool IsAvailableForCharacter(Player player) => IsSilentPlayer(player);

	// 原版夜魇在下回合抽牌前由 NightmarePower 克隆选中的牌加入手牌（手牌满时进弃牌堆）；
	// 这次调用前后新出现在战斗里的牌就是这批复制品，设为本场战斗免费。
	internal static async Task MakeNewCopiesFree(Task original, Player owner, HashSet<CardModel> before)
	{
		await original;
		if (owner.PlayerCombatState is not { } combatState)
		{
			return;
		}

		bool any = false;
		foreach (CardModel card in combatState.AllCards.Where(card => !before.Contains(card)).ToList())
		{
			card.SetToFreeThisCombat();
			any = true;
		}

		if (any)
		{
			owner.GetRelic<NightmareUpgradeRune>()?.Flash();
		}
	}

	[HarmonyPatch(typeof(NightmarePower), nameof(NightmarePower.BeforeHandDraw), typeof(Player), typeof(PlayerChoiceContext), typeof(HextechCombatState))]
	[HextechPatch("rune.nightmare.free-copies", "升级夜魇", Rune = typeof(NightmareUpgradeRune))]
	private static class FreeNightmareCopiesPatch
	{
		[HarmonyPrefix]
		private static void Prefix(NightmarePower __instance, Player player, out HashSet<CardModel>? __state)
		{
			Player? owner = __instance.Owner.Player;
			__state = owner != null
				&& owner == player
				&& owner.GetRelic<NightmareUpgradeRune>() != null
				&& owner.PlayerCombatState is { } combatState
					? combatState.AllCards.ToHashSet()
					: null;
		}

		[HarmonyPostfix]
		private static void Postfix(NightmarePower __instance, HashSet<CardModel>? __state, ref Task __result)
		{
			if (__state != null && __instance.Owner.Player is { } owner)
			{
				__result = MakeNewCopiesFree(__result, owner, __state);
			}
		}
	}
}
