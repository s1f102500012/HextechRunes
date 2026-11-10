namespace HextechRunes;

// 升级：精神过载(仅骨妹) —— 持有时,打出精神过载不再施加原版减益,改为施加本模组的同名正面效果
// HextechNeurosurgePower(你的回合开始时对所有敌人施加等同于层数的灾厄)。
// 只在卡牌 OnPlay 处替换施加对象;不触碰原版 NeurosurgePower 的 Type/AfterSideTurnStart,
// 也不需要人工制品的特判(正面效果本来就不会被拦)。
public sealed class NeurosurgeUpgradeRune : CardUpgradeRuneBase<Neurosurge>
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		.. base.ExtraHoverTips,
		HoverTipFactory.FromPower<HextechNeurosurgePower>(),
		HoverTipFactory.FromPower<DoomPower>()
	];

	protected override bool IsAvailableForCharacter(Player player) => IsNecrobinderPlayer(player);

	/// <summary>只对持有本符文的玩家自己的精神过载生效;队友的牌保持原版。</summary>
	internal static bool ShouldSwapToHextechPower(CardModel card)
	{
		return card is Neurosurge && card.Owner?.GetRelic<NeurosurgeUpgradeRune>() != null;
	}

	// 原版 Neurosurge.OnPlay 的完整复制体(三个版本反编译一致:PowerUp 动画 → 获得能量 → 抽牌 → 施加能力),
	// 只把最后一步施加的能力换成海克斯版;原版体的 IL 由 vanilla_copy_guard 冻结,游戏更新后漂移会在测试与启动日志里显形。
	internal static async Task PlayUpgraded(PlayerChoiceContext choiceContext, Neurosurge card)
	{
		Player owner = card.Owner;
		await CreatureCmd.TriggerAnim(owner.Creature, "PowerUp", owner.Character.PowerUpAnimDelay);
		await PlayerCmd.GainEnergy(card.DynamicVars.Energy.BaseValue, owner);
		await CardPileCmd.Draw(choiceContext, card.DynamicVars.Cards.BaseValue, owner);
		await PowerCmd.Apply<HextechNeurosurgePower>(choiceContext, owner.Creature, card.DynamicVars["NeurosurgePower"].IntValue, owner.Creature, card);
	}

	[HarmonyPatch(typeof(Neurosurge), "OnPlay", typeof(PlayerChoiceContext), typeof(CardPlay))]
	[HextechPatch("rune.neurosurge.on-play", "升级精神过载", Rune = typeof(NeurosurgeUpgradeRune))]
	private static class OnPlayPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(Neurosurge __instance, PlayerChoiceContext choiceContext, ref Task __result)
		{
			if (!ShouldSwapToHextechPower(__instance))
			{
				return true;
			}

			__result = PlayUpgraded(choiceContext, __instance);
			return false;
		}
	}
}
