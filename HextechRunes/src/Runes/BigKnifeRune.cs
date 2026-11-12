namespace HextechRunes;

public sealed class BigKnifeRune : HextechRelicBase
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Shiv>(),
		HoverTipFactory.FromCard<SovereignBlade>(),
		HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	// 免费在 Late 阶段定为 0（同原版免费攻击/技能），晚于溢流等常规阶段的加费。
	public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldMakeBladeFree(card) || card.EnergyCost.CostsX)
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldMakeBladeFree(card) || card.HasStarCostX)
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	private bool ShouldMakeBladeFree(CardModel card)
	{
		return card.Owner == Owner
			&& card is SovereignBlade
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}

	// 0.109 起 Shiv.CreateInHand 尾部多一个可选参 Player creator。
#if STS2_109_OR_NEWER
	[HarmonyPatch(typeof(Shiv), nameof(Shiv.CreateInHand), typeof(Player), typeof(HextechCombatState), typeof(Player))]
#else
	[HarmonyPatch(typeof(Shiv), nameof(Shiv.CreateInHand), typeof(Player), typeof(HextechCombatState))]
#endif
	[HextechPatch("rune.big-knife.shiv-one", "大刀", Rune = typeof(BigKnifeRune))]
	private static class ShivCreateOnePatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Player owner, HextechCombatState combatState, ref Task<CardModel?> __result)
		{
			if (owner.GetRelic<BigKnifeRune>() == null || combatState is not CombatState concreteState)
			{
				return true;
			}

			__result = HextechKnifeHelper.CreateOneBigKnifeBladeInHand(owner, concreteState);
			return false;
		}
	}

#if STS2_109_OR_NEWER
	[HarmonyPatch(typeof(Shiv), nameof(Shiv.CreateInHand), typeof(Player), typeof(int), typeof(HextechCombatState), typeof(Player))]
#else
	[HarmonyPatch(typeof(Shiv), nameof(Shiv.CreateInHand), typeof(Player), typeof(int), typeof(HextechCombatState))]
#endif
	[HextechPatch("rune.big-knife.shiv-many", "大刀", Rune = typeof(BigKnifeRune))]
	private static class ShivCreateManyPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Player owner, int count, HextechCombatState combatState, ref Task<IEnumerable<CardModel>> __result)
		{
			if (owner.GetRelic<BigKnifeRune>() == null || combatState is not CombatState concreteState)
			{
				return true;
			}

			__result = HextechKnifeHelper.CreateBigKnifeBladesInHand(owner, count, concreteState);
			return false;
		}
	}

	[HarmonyPatch(typeof(SovereignBlade), nameof(SovereignBlade.TargetType), MethodType.Getter)]
	[HextechPatch("rune.big-knife.sovereign-blade-target", "大刀", Rune = typeof(BigKnifeRune))]
	private static class SovereignBladeTargetTypePatch
	{
		[HarmonyPostfix]
		private static void Postfix(SovereignBlade __instance, ref TargetType __result)
		{
			if (HextechKnifeHelper.ShouldFanOfKnivesAffectSovereignBlade(__instance))
			{
				__result = TargetType.AllEnemies;
			}
		}
	}

	[HarmonyPatch(typeof(SovereignBlade), "OnPlay", typeof(PlayerChoiceContext), typeof(CardPlay))]
	[HextechPatch("rune.big-knife.sovereign-blade-play", "大刀", Rune = typeof(BigKnifeRune))]
	private static class SovereignBladeOnPlayPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(SovereignBlade __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
		{
			if (!HextechKnifeHelper.ShouldFanOfKnivesAffectSovereignBlade(__instance) || __instance.CombatState is not CombatState)
			{
				return true;
			}

			__result = HextechPlayerRuneHooks.PlayFanOfKnivesSovereignBlade(choiceContext, __instance);
			return false;
		}
	}

	// 敌方「操控现实」的状态牌翻倍是同一目标上的独立前缀（HextechEnemyManipulateRealityHooks，优先级 Normal - 1），
	// 依赖本前缀先完成小刀替换；本前缀的优先级显式固定为 Normal，二者先后由补丁清单冻结。
	[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat), typeof(IEnumerable<CardModel>), typeof(PileType), typeof(Player), typeof(CardPilePosition))]
	[HextechPatch("rune.big-knife.generated-cards", "大刀", Rune = typeof(BigKnifeRune))]
	private static class GeneratedCardsPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Normal)]
		private static void Prefix(ref IEnumerable<CardModel> cards)
		{
			// 兜底理由：本前缀在"敌人塞状态牌/生成卡进战斗"的必经路径上，异常会中断整个 AddGeneratedCardsToCombat、
			// 让上层塞牌任务链卡死。cards 来自外部，可能是其他模组改写后的惰性序列；枚举失败或替换出错时原样放行，不做替换。
			List<CardModel> originals;
			try
			{
				originals = cards.ToList();
			}
			catch (Exception ex)
			{
				HextechLog.Error("BigKnife", $"rune.big-knife.generated-cards: enumerating generated cards failed; passing cards through without replacement.\n{ex}");
				return;
			}

			try
			{
				List<CardModel>? rewritten = null;
				for (int i = 0; i < originals.Count; i++)
				{
					CardModel card = originals[i];
					if (!HextechKnifeHelper.TryCreateBigKnifeReplacement(card, out CardModel replacement))
					{
						rewritten?.Add(card);
						continue;
					}

					if (rewritten == null)
					{
						rewritten = originals.Take(i).ToList();
					}
					rewritten.Add(replacement);
				}

				if (rewritten != null)
				{
					cards = rewritten;
				}
			}
			catch (Exception ex)
			{
				HextechLog.Error("BigKnife", $"rune.big-knife.generated-cards: Shiv replacement failed; passing cards through without replacement.\n{ex}");
			}
		}
	}
}
