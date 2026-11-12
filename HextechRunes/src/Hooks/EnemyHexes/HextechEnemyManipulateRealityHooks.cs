using System.Diagnostics.CodeAnalysis;

namespace HextechRunes;

/// <summary>
/// 敌方「操控现实」：非玩家生成、进入玩家牌堆的状态牌在加入战斗前整批翻倍。
/// 需要在状态牌加入牌堆前改写整批卡牌，标准 effect hook 无法表达，所以挂在 <c>AddGeneratedCardsToCombat</c> 前缀上。
/// </summary>
internal static class HextechEnemyManipulateRealityHooks
{
	/// <summary>
	/// 排在大号匕首替换前缀（<see cref="Priority.Normal"/>）之后：先把小刀换成大刀，再对替换后的整批翻倍。
	/// 两个前缀同属本模组 Harmony owner，<c>HarmonyBefore/After</c> 按 owner 生效、排不了同 owner 的先后，只能用优先级固定。
	/// </summary>
	internal const int GeneratedCardsPrefixPriority = Priority.Normal - 1;

	[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat), typeof(IEnumerable<CardModel>), typeof(PileType), typeof(Player), typeof(CardPilePosition))]
	[HextechPatch("enemy.manipulate-reality.generated-cards", "敌方海克斯:操控现实", MonsterHexes = [MonsterHexKind.ManipulateReality])]
	private static class GeneratedCardsPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(GeneratedCardsPrefixPriority)]
		private static void Prefix(ref IEnumerable<CardModel> cards, Player? creator)
		{
			// 玩家自己生成的牌不翻倍，也就不必枚举外部序列。
			if (creator != null)
			{
				return;
			}

			// 兜底理由：这是"敌人塞状态牌/生成卡进战斗"的必经路径，异常会中断整个 AddGeneratedCardsToCombat、
			// 让上层塞牌任务链卡死。cards 可能是大号匕首前缀改写后的 List，也可能是外部（含其他模组改写后）的惰性序列，
			// 枚举失败时原样放行，不翻倍。
			List<CardModel> originals;
			try
			{
				originals = cards.ToList();
			}
			catch (Exception ex)
			{
				HextechLog.Error("EnemyManipulateReality", $"enemy.manipulate-reality.generated-cards: enumerating generated cards failed; passing cards through without doubling.\n{ex}");
				return;
			}

			try
			{
				List<CardModel>? rewritten = TryApplyEnemyManipulateRealityStatusDoubling(originals, addedByPlayer: false);
				if (rewritten != null)
				{
					cards = rewritten;
				}
			}
			catch (Exception ex)
			{
				HextechLog.Error("EnemyManipulateReality", $"enemy.manipulate-reality.generated-cards: status doubling failed; passing cards through without doubling.\n{ex}");
			}
		}
	}

	internal static List<CardModel>? TryApplyEnemyManipulateRealityStatusDoubling(IReadOnlyList<CardModel> cards, bool addedByPlayer)
	{
		if (addedByPlayer)
		{
			return null;
		}

		List<CardModel>? rewritten = null;
		for (int i = 0; i < cards.Count; i++)
		{
			CardModel card = cards[i];
			if (!ShouldDoubleEnemyGeneratedStatusCard(card))
			{
				rewritten?.Add(card);
				continue;
			}

			rewritten ??= cards.Take(i).ToList();
			rewritten.Add(card);
			if (TryCreateManipulateRealityStatusCopy(card, out CardModel? copy))
			{
				rewritten.Add(copy);
			}
		}

		return rewritten;
	}

	internal static bool ShouldDoubleEnemyGeneratedStatusCard(CardModel card)
	{
		return card.Type == CardType.Status
			&& card.Owner?.Creature.Side == CombatSide.Player
			&& card.Owner.Creature.CombatState?.RunState == card.Owner.RunState
			&& HextechMayhemModifier.FindIn(card.Owner.RunState)?.HasActiveMonsterHex(MonsterHexKind.ManipulateReality) == true;
	}

	internal static bool TryCreateManipulateRealityStatusCopy(CardModel card, [NotNullWhen(true)] out CardModel? copy)
	{
		copy = null;
		if (card.Owner?.Creature.CombatState is not HextechCombatState combatState)
		{
			return false;
		}

		copy = combatState.CloneCard(card);
		return true;
	}
}
