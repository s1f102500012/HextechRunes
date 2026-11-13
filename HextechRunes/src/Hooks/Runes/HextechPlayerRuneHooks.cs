namespace HextechRunes;

internal static partial class HextechPlayerRuneHooks
{
	internal static async Task JuggernautUpgradeAfterBlockGained(JuggernautPower power, Creature creature, decimal amount)
	{
		if (amount <= 0m || creature != power.Owner)
		{
			return;
		}

		List<Creature> targets = power.CombatState.HittableEnemies.ToList();
		if (targets.Count == 0)
		{
			return;
		}

		power.Owner.Player?.GetRelic<JuggernautUpgradeRune>()?.Flash(targets);
		await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), targets, power.Amount, ValueProp.Unpowered, power.Owner);
	}

	// 刀扇打出君王之剑：DamageCmd 的 TargetingAllOpponents 需要具体 CombatState，拿不到时不出手。
	// 段数与原版 OnPlay 同源（0.111.0 的 RepeatVar / 0.113.0 的 HitCountVar），由兼容层按版本读取。
	internal static async Task PlayFanOfKnivesSovereignBlade(PlayerChoiceContext choiceContext, SovereignBlade card)
	{
		if (card.CombatState is not CombatState combatState)
		{
			return;
		}

		AttackCommand attack = DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
			.FromCardCompat(card)
			.WithHitCount(HextechV113ApiCompat.GetSovereignBladeHitCount(card))
			.WithAttackerAnim("Cast", card.Owner.Character.AttackAnimDelay)
			.WithAttackerFx(null, "event:/sfx/characters/regent/regent_sovereign_blade")
			.TargetingAllOpponents(combatState)
			.WithHitFx("vfx/vfx_giant_horizontal_slash", null, "slash_attack.mp3");

		await attack.Execute(choiceContext);
	}

	// 卡牌标签 getter 是热路径，图鉴/第三方会在规范模型上读它；规范模型的 Owner 会抛 CanonicalModelException，先判 IsCanonical。
	internal static Player? TryGetMutableCardOwner(CardModel card)
	{
		return card.IsCanonical ? null : card.Owner;
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.Tags), MethodType.Getter)]
	[HextechPatch("rune.card-tags", "卡牌标签", Runes = [typeof(DeviantCognitionRune), typeof(BigKnifeRune)])]
	internal static class CardTagsPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, ref IEnumerable<CardTag> __result)
		{
			// 各条件都是只读判断，先判牌的类型，再枚举标签、扫遗物。
			Player? owner = TryGetMutableCardOwner(__instance);
			if (__instance is SovereignBlade
				&& !__result.Contains(CardTag.Shiv)
				&& HextechKnifeHelper.ShouldTreatSovereignBladeAsShiv(__instance, owner))
			{
				__result = __result.Append(CardTag.Shiv);
			}

			if (__instance.Type != CardType.Attack
				|| __result.Contains(CardTag.Strike)
				|| owner?.GetRelic<DeviantCognitionRune>() == null)
			{
				return;
			}

			__result = __result.Append(CardTag.Strike);
		}
	}
}
