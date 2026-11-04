using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunes;

internal static class HextechRewardSafetyHooks
{
	private const string PaelsWingSacrificeAlternativeId = "SACRIFICE";
	private static readonly ConditionalWeakTable<CardReward, CardRewardCompatibilityState> CardRewardStates = new();

	private static IReadOnlyList<CardRewardAlternative> GenerateCardRewardAlternativesWithoutVanillaLimit(CardReward cardReward)
	{
		List<CardRewardAlternative> alternatives = [];
		if (cardReward.CanSkip)
		{
			alternatives.Add(new CardRewardAlternative("Skip", PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward));
		}

		if (cardReward.CanReroll || GetRemainingDriftwoodRerolls(cardReward) > 0)
		{
			alternatives.Add(CreateDriftwoodRerollAlternative(cardReward));
		}

		Hook.ModifyCardRewardAlternatives(cardReward.Player.RunState, cardReward.Player, cardReward, alternatives);
		IReadOnlyList<CardRewardAlternative> normalized = NormalizeCardRewardAlternativesForCompatibility(alternatives);
		// 每个奖励固定返回同一个列表实例并原地刷新:原版 OnSelect 在开头取一次 Generate 结果,之后按下标取备选项;
		// 浮木重掷会再次 Generate 并刷新界面,原地刷新才能让 OnSelect 手里的列表与界面一致
		// (例如最后一次重掷用掉后“重掷”项消失,下标随之移位)。
		CardRewardCompatibilityState state = CardRewardStates.GetOrCreateValue(cardReward);
		state.Alternatives.Clear();
		state.Alternatives.AddRange(normalized);
		return state.Alternatives;
	}

	private static CardRewardAlternative CreateDriftwoodRerollAlternative(CardReward cardReward)
	{
		return new CardRewardAlternative("REROLL", () =>
		{
			ConsumeDriftwoodReroll(cardReward);
			cardReward.Reroll();
			return Task.CompletedTask;
		}, PostAlternateCardRewardAction.DoNothing);
	}

	private static int GetRemainingDriftwoodRerolls(CardReward cardReward)
	{
		if (CardRewardStates.TryGetValue(cardReward, out CardRewardCompatibilityState? state)
			&& state.RemainingDriftwoodRerolls.HasValue)
		{
			return Math.Max(0, state.RemainingDriftwoodRerolls.Value);
		}

		if (!cardReward.CanReroll)
		{
			return 0;
		}

		int driftwoodCount = CountOwnedDriftwood(cardReward.Player);
		if (driftwoodCount <= 0)
		{
			return 0;
		}

		state = CardRewardStates.GetOrCreateValue(cardReward);
		state.RemainingDriftwoodRerolls = driftwoodCount;
		return driftwoodCount;
	}

	private static void ConsumeDriftwoodReroll(CardReward cardReward)
	{
		int remaining = GetRemainingDriftwoodRerolls(cardReward);
		if (remaining <= 0)
		{
			return;
		}

		CardRewardCompatibilityState state = CardRewardStates.GetOrCreateValue(cardReward);
		state.RemainingDriftwoodRerolls = remaining - 1;
	}

	// 只有浮木（多份时按数量计重掷次数）和佩尔之翼（与浮木同时持有或多份时超过原版上限）需要接管。
	internal static bool NeedsCardRewardAlternativesTakeover(CardReward cardReward)
	{
		return CardRewardStates.TryGetValue(cardReward, out _)
			|| NeedsCardRewardAlternativesTakeover(cardReward.Player.Relics);
	}

	internal static bool NeedsCardRewardAlternativesTakeover(IEnumerable<RelicModel> relics)
	{
		return relics.Any(static relic => relic is Driftwood or PaelsWing);
	}

	private static int CountOwnedDriftwood(Player player)
	{
		return player.Relics.Count(static relic => relic is Driftwood);
	}

	internal static IReadOnlyList<CardRewardAlternative> NormalizeCardRewardAlternativesForCompatibility(
		IReadOnlyList<CardRewardAlternative> alternatives)
	{
		CardRewardAlternative[] sacrificeAlternatives = alternatives
			.Where(static alternative => IsPaelsWingSacrificeAlternative(alternative))
			.ToArray();

		if (sacrificeAlternatives.Length <= 1)
		{
			return alternatives;
		}

		List<CardRewardAlternative> normalized = new(alternatives.Count - sacrificeAlternatives.Length + 1);
		bool addedMergedSacrifice = false;
		foreach (CardRewardAlternative alternative in alternatives)
		{
			if (!IsPaelsWingSacrificeAlternative(alternative))
			{
				normalized.Add(alternative);
				continue;
			}

			if (addedMergedSacrifice)
			{
				continue;
			}

			normalized.Add(CreateMergedPaelsWingSacrificeAlternative(sacrificeAlternatives));
			addedMergedSacrifice = true;
		}

		return normalized;
	}

	private static bool IsPaelsWingSacrificeAlternative(CardRewardAlternative alternative)
	{
		return string.Equals(alternative.OptionId, PaelsWingSacrificeAlternativeId, StringComparison.OrdinalIgnoreCase);
	}

	private static CardRewardAlternative CreateMergedPaelsWingSacrificeAlternative(
		IReadOnlyList<CardRewardAlternative> sacrificeAlternatives)
	{
		return new CardRewardAlternative(
			PaelsWingSacrificeAlternativeId,
			async () =>
			{
				foreach (CardRewardAlternative alternative in sacrificeAlternatives)
				{
					await alternative.OnSelect();
				}
			},
			PostAlternateCardRewardAction.EndSelectionAndCompleteReward);
	}

	private sealed class CardRewardCompatibilityState
	{
		public List<CardRewardAlternative> Alternatives { get; } = [];

		public int? RemainingDriftwoodRerolls { get; set; }
	}

	// 承载 OnSelect 前后所需状态:DoubleVision 的追踪 scope + 进入 OnSelect 前的卡数(供禁忌魔典判别是否真选走了卡)。
	private sealed record CardRewardOnSelectState(object? DoubleVisionScope, int CardCountBeforeSelect);

	// 复视的事务作用域都是 AsyncLocal：Prefix 在调用方执行流里入栈，Postfix 同步恢复并把 __state 清空，
	// 异步续体只负责清账。原命令（或排在后面的补丁）同步抛异常时 Postfix 不执行，由 Finalizer 恢复；
	// Postfix 已处理过的调用 __state 为 null，Finalizer 不会重复出栈。
	private static void CompleteSuppression(ref object? state)
	{
		DoubleVisionRune.CompleteRewardCommandSuppression(state);
		state = null;
	}

	private static Exception? AbandonScopeAfterSynchronousFailure(object? state, Exception? exception)
	{
		if (exception != null && state != null)
		{
			DoubleVisionRune.AbandonScopeAfterSynchronousFailure(state);
		}

		return exception;
	}

	private static async Task<bool> CompleteForbiddenGrimoireCardRewardAsync(CardReward reward, Task<bool> originalTask, int cardCountBeforeSelect)
	{
		bool rewardComplete = await originalTask;
		if (!rewardComplete || !ShouldApplyForbiddenGrimoire(reward))
		{
			return rewardComplete;
		}

		// 只在"确实选走了至少一张卡"时才补发其余未选卡。献祭(佩尔之翼)/锻体(百炼成钢)等走
		// EndSelectionAndCompleteReward 的 alternative 会让 rewardComplete=true 却不从 _cards 移除任何卡
		// (选前==选后),照旧补发会把整组卡白送。用"卡数减少"判别精确区分:正常选卡(含帽子戏法/Mayhem 多选)
		// 选后<选前→补发剩余;alternative 选后==选前→不补发。必须用 < 而非 ==(选前-1),否则一次移除多张的
		// 多选卡会被误判而漏发最后一张。判别只读 reward.Cards 计数(reward 状态、两端一致),不引入联机分叉。
		if (reward.Cards.Count() >= cardCountBeforeSelect)
		{
			return rewardComplete;
		}

		List<CardModel> remainingCards = reward.Cards.ToList();
		if (remainingCards.Count == 0)
		{
			return rewardComplete;
		}

		foreach (CardModel card in remainingCards)
		{
			CardPileAddResult result = await CardPileCmd.Add(card, PileType.Deck);
			if (result.success)
			{
				HextechLog.Info("EnemyForbiddenGrimoire", $"Forced unpicked card reward: player={reward.Player.NetId} card={result.cardAdded.Id.Entry}");
			}
			else
			{
				HextechLog.Warn("EnemyForbiddenGrimoire", $"Failed to force unpicked card reward: player={reward.Player.NetId} card={card.Id.Entry}");
			}
		}

		return rewardComplete;
	}

	private static bool ShouldApplyForbiddenGrimoire(CardReward reward)
	{
		Player player = reward.Player;
		return !player.Creature.IsDead
			&& HextechMayhemModifier.FindIn(player.RunState) is HextechMayhemModifier modifier
			&& modifier.HasActiveMonsterHex(MonsterHexKind.ForbiddenGrimoire);
	}

	internal static bool TryRestoreForgeChoiceReward(SerializableReward save, Player player, ref Reward result)
	{
		if (save.RewardType != RewardType.Gold
			|| result is not GoldReward
			|| save.CustomDescriptionEncounterSourceId != ModelDb.GetId<RandomForgeShopRelic>()
			|| save.CardPoolIds.Count == 0
			|| !HextechForgeChoiceReward.TryFromSavedReward(save, player, out HextechForgeChoiceReward? restored))
		{
			return false;
		}

		result = restored;
		return true;
	}

	// 跳过型前缀（已裁决保留，见 architecture.md）：原版 Generate 在 Hook.ModifyCardRewardAlternatives 之后
	// 对超过 2 个备选项直接抛异常，本模组的多次浮木重掷与多份佩尔之翼献祭会超过这个上限，没有可用的 Hook 放宽它。
	// 只在本局启用海克斯、且玩家持有浮木或佩尔之翼（或这份奖励已在按浮木次数计数）时替换，其余卡牌奖励走原版，
	// 不挡其他模组在 Generate 上的前缀。替换体逐步复制原版（跳过/重掷 → Hook.ModifyCardRewardAlternatives），
	// 仅去掉数量上限、按浮木数量计重掷次数并合并重复的献祭项。目标 IL 由原版拷贝守卫冻结（0.107.1/0.111.0）。
	[HarmonyPatch(typeof(CardRewardAlternative), nameof(CardRewardAlternative.Generate), typeof(CardReward))]
	[HextechPatch("reward.card-alternatives", "卡牌奖励备选项")]
	private static class CardRewardAlternativesPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(CardReward cardReward, ref IReadOnlyList<CardRewardAlternative> __result)
		{
			if (!HextechMayhemModifier.IsEnabledForRun(cardReward.Player.RunState)
				|| !NeedsCardRewardAlternativesTakeover(cardReward))
			{
				return true;
			}

			__result = GenerateCardRewardAlternativesWithoutVanillaLimit(cardReward);
			return false;
		}
	}

	[HarmonyPatch(typeof(Reward), nameof(Reward.FromSerializable), typeof(SerializableReward), typeof(Player))]
	[HextechPatch("reward.from-serializable", "奖励序列化恢复")]
	private static class RewardFromSerializablePatch
	{
		[HarmonyPostfix]
		private static void Postfix(SerializableReward save, Player player, ref Reward __result)
		{
			if (save.RewardType == RewardType.Gold && save.GoldAmount < 0 && __result is GoldReward)
			{
				__result = new GoldReward(0, player, save.WasGoldStolenBack);
				HextechLog.Warn("Rewards", $"Repaired serialized gold reward with negative amount {save.GoldAmount}; defaulting to 0 gold.");
				return;
			}

			if (save.RewardType == RewardType.Relic
				&& HextechWaxRelicReward.IsWaxSave(save)
				&& save.PredeterminedModelId != ModelId.none)
			{
				RelicModel relic = ModelDb.GetById<RelicModel>(save.PredeterminedModelId).ToMutable();
				__result = new HextechWaxRelicReward(relic, player);
				return;
			}

			if (save.RewardType == RewardType.Card
				&& save.CustomDescriptionEncounterSourceId == ModelDb.GetId<ColorDiscoveryRune>()
				&& save.PredeterminedModelId != ModelId.none)
			{
				__result = ColorDiscoveryCardReward.FromSavedReward(save, player);
				return;
			}

			if (save.RewardType == RewardType.SpecialCard
				&& save.PredeterminedModelId == ModelDb.GetId<ColorDiscoveryRune>()
				&& save.SpecialCard != null
				&& ColorDiscoveryCardReward.TryFromSavedSpecialCardReward(
					save,
					__result,
					player,
					out ColorDiscoveryCardReward? restoredColorDiscoveryReward))
			{
				__result = restoredColorDiscoveryReward;
				return;
			}

			TryRestoreForgeChoiceReward(save, player, ref __result);
		}
	}

	[HarmonyPatch(typeof(Reward), nameof(Reward.SelectUnsynchronized), new Type[0])]
	[HextechPatch("reward.select-unsynchronized", "复视奖励事务")]
	private static class RewardSelectUnsynchronizedPatch
	{
		[HarmonyPrefix]
		private static void Prefix(out object? __state)
		{
			__state = DoubleVisionRune.BeginRewardCommandSuppression();
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state) => CompleteSuppression(ref __state);

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	// 巨口储蓄罐的每层收入不是奖励,不该被复视翻倍。它在进房钩子里发钱,钩子在所有端执行,
	// 而复视只在持有端复制再走奖励同步;进战斗房时远端把同步消息缓存到战斗结束,首回合校验金币分叉。
	// 原版在调用 GainGold 前没有 await,前缀设置的抑制深度能覆盖到这次发钱。
	[HarmonyPatch(typeof(MawBank), nameof(MawBank.AfterRoomEntered), typeof(AbstractRoom))]
	[HextechPatch("reward.maw-bank-income", "复视奖励事务")]
	private static class MawBankIncomePatch
	{
		[HarmonyPrefix]
		private static void Prefix(out object? __state)
		{
			__state = DoubleVisionRune.BeginRewardCommandSuppression();
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state) => CompleteSuppression(ref __state);

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	// 佩尔之翼献祭换来的遗物也算奖励。献祭在所有端执行，复制同样在所有端各自进行（见 BeginPaelsWingSacrifice）。
	// 原版在调用 RelicCmd.Obtain 前没有 await,前缀设置的标记能覆盖到这次获得。
	[HarmonyPatch(typeof(PaelsWing), nameof(PaelsWing.OnSacrifice), new Type[0])]
	[HextechPatch("reward.paels-wing-sacrifice", "复视奖励事务")]
	private static class PaelsWingSacrificePatch
	{
		[HarmonyPrefix]
		private static void Prefix(PaelsWing __instance, out object? __state)
		{
			__state = DoubleVisionRune.BeginPaelsWingSacrifice(__instance.Owner);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state)
		{
			DoubleVisionRune.CompletePaelsWingSacrifice(__state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	[HarmonyPatch(typeof(EventOption), nameof(EventOption.Chosen), new Type[0])]
	[HextechPatch("reward.event-option", "复视奖励事务")]
	private static class EventOptionChosenPatch
	{
		[HarmonyPrefix]
		private static void Prefix(EventOption __instance, out object? __state)
		{
			__state = DoubleVisionRune.BeginEventOptionRelicTransaction(__instance);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state, ref Task __result)
		{
			__result = DoubleVisionRune.CompleteEventOptionRelicTransactionAsync(__result, __state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.AfterObtained), new Type[0])]
	[HextechPatch("reward.dusty-tome", "复视奖励事务")]
	internal static class DustyTomePatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		internal static bool Prefix(DustyTome __instance, ref Task __result)
		{
			if (!DoubleVisionRune.ShouldSuppressDustyTomeAfterObtained(__instance))
			{
				return true;
			}

			__result = Task.CompletedTask;
			return false;
		}
	}

	[HarmonyPatch(typeof(CardReward), "OnSelect")]
	[HextechPatch("reward.card-select", "复视奖励事务")]
	private static class CardRewardSelectPatch
	{
		[HarmonyPrefix]
		private static void Prefix(CardReward __instance, out object? __state)
		{
			__state = new CardRewardOnSelectState(
				DoubleVisionRune.BeginCardRewardTracking(__instance.Player),
				__instance.Cards.Count());
		}

		[HarmonyPostfix]
		private static void Postfix(CardReward __instance, ref object? __state, ref Task<bool> __result)
		{
			// Prefix 只注入 __instance 与 __state,Harmony 不会因别的前缀跳过原方法而跳过它,__state 必已赋值。
			CardRewardOnSelectState state = (CardRewardOnSelectState)__state!;
			Task<bool> result = __result;
			if (ShouldApplyForbiddenGrimoire(__instance))
			{
				result = CompleteForbiddenGrimoireCardRewardAsync(__instance, result, state.CardCountBeforeSelect);
			}

			__result = DoubleVisionRune.CompleteCardRewardAsync(result, state.DoubleVisionScope);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) =>
			AbandonScopeAfterSynchronousFailure((__state as CardRewardOnSelectState)?.DoubleVisionScope, __exception);
	}

	[HarmonyPatch(typeof(SpecialCardReward), "OnSelect")]
	[HextechPatch("reward.special-card-select", "复视奖励事务")]
	private static class SpecialCardRewardSelectPatch
	{
		[HarmonyPrefix]
		private static void Prefix(SpecialCardReward __instance, out object? __state)
		{
			__state = DoubleVisionRune.BeginCardRewardTracking(__instance.Player);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state, ref Task<bool> __result)
		{
			__result = DoubleVisionRune.CompleteCardRewardAsync(__result, __state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	[HarmonyPatch(typeof(RelicReward), "OnSelect")]
	[HextechPatch("reward.relic-select", "复视奖励事务")]
	private static class RelicRewardSelectPatch
	{
		[HarmonyPrefix]
		private static void Prefix(RelicReward __instance, out object? __state)
		{
			__state = DoubleVisionRune.CaptureRewardDuplicationState(__instance.Player);
		}

		[HarmonyPostfix]
		private static void Postfix(RelicReward __instance, object? __state, ref Task<bool> __result)
		{
			__result = DoubleVisionRune.CompleteRelicRewardAsync(__instance, __result, __state);
		}
	}

	[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool))]
	[HextechPatch("reward.card-pile-add", "复视奖励事务")]
	private static class CardPileAddPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel card, PileType newPileType, AbstractModel? clonedBy, ref Task<CardPileAddResult> __result)
		{
			DoubleVisionRune.TrackCardPileAdd(card, newPileType, clonedBy, ref __result);
		}
	}

	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), typeof(RelicModel), typeof(Player), typeof(int))]
	[HextechPatch("reward.relic-obtain", "复视奖励事务")]
	private static class RelicObtainPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.High)]
		private static void Prefix(RelicModel relic, Player player, out object? __state)
		{
			__state = DoubleVisionRune.BeginDirectRelicReward(relic, player);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state, ref Task<RelicModel> __result)
		{
			__result = DoubleVisionRune.CompleteDirectRelicRewardAsync(__result, __state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	[HarmonyPatch(typeof(PotionCmd), nameof(PotionCmd.TryToProcure), typeof(PotionModel), typeof(Player), typeof(int))]
	[HextechPatch("reward.potion-procure", "复视奖励事务")]
	private static class PotionProcurePatch
	{
		[HarmonyPrefix]
		private static void Prefix(Player player, out object? __state)
		{
			__state = DoubleVisionRune.BeginDirectPotionReward(player);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state, ref Task<PotionProcureResult> __result)
		{
			__result = DoubleVisionRune.CompleteDirectPotionRewardAsync(__result, __state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}

	[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold), typeof(decimal), typeof(Player), typeof(bool))]
	[HextechPatch("reward.gain-gold", "复视奖励事务")]
	private static class GainGoldPatch
	{
		[HarmonyPrefix]
		private static void Prefix(decimal amount, Player player, bool wasStolenBack, out object? __state)
		{
			__state = DoubleVisionRune.BeginDirectGoldReward(player, amount, wasStolenBack);
		}

		[HarmonyPostfix]
		private static void Postfix(ref object? __state, ref Task __result)
		{
			__result = DoubleVisionRune.CompleteDirectGoldRewardAsync(__result, __state);
			__state = null;
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(object? __state, Exception? __exception) => AbandonScopeAfterSynchronousFailure(__state, __exception);
	}
}
