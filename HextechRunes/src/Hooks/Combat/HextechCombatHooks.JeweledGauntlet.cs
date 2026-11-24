using System.Globalization;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private const string KnowledgeDemonCurseMoveId = "CURSE_OF_KNOWLEDGE_MOVE";
	private const int FinalKnowledgeDemonCurseIndex = 2;
	private const string TestSubjectRespawnMoveId = "RESPAWN_MOVE";
	private const string IllusionReviveMoveId = "REVIVE_MOVE";
	private const string TheInsatiableOpeningMoveId = "LIQUIFY_GROUND_MOVE";

	// MonsterModel._isPerformingMove（bool）与 KnowledgeDemon._curseOfKnowledgeCounter（int），
	// 0.107.1/0.111.0 原版私有字段；MoveState.Intents 后备字段见 HextechCombatHooks.Core。
	// 任一缺失时两个补丁的 Prepare 都返回 false，珠光护手整组停用，补丁体内不再判空。
	private static readonly FieldInfo MonsterIsPerformingMoveField =
		TryGetField(typeof(MonsterModel), "_isPerformingMove")!;
	private static readonly FieldInfo KnowledgeDemonCurseCounterField =
		TryGetField(typeof(KnowledgeDemon), "_curseOfKnowledgeCounter")!;

	private static bool JeweledGauntletHooksAvailable =>
		MoveStateIntentsField != null
		&& MonsterIsPerformingMoveField != null
		&& KnowledgeDemonCurseCounterField != null;

	// 原版 MonsterModel.PerformMove（0.107.1/0.111.0/0.113.0）完成后，本回合的额外行动按固定顺序执行：
	//   本回合行动 A（原版）→ 珠光护手重复 A → 连拨击锤推进出来的 B → 珠光护手重复 B。
	// 珠光护手对 A、B 各自判定（键含行动 ID，同名行动结果相同），重复出来的行动不再判定，连拨击锤只在
	// RollMove 时推进一步，所以一回合最多 4 次行动，不会连锁膨胀。头顶意图按同一顺序列出。
	private static async Task PerformMonsterTurnExtraMoves(
		Task originalTask,
		MonsterTurnExtraMoves plan)
	{
		await originalTask;

		MonsterModel monster = plan.Monster;
		if (plan.RepeatCurrent)
		{
			await PerformExtraMonsterMove(monster, plan.Current, "enemy Jeweled Gauntlet");
		}

		// 前面的行动可能让怪物死亡、离场或被强制改招（不变量恢复后 B 作废）；B 只在仍然有效时接着执行。
		if (plan.FollowUp is not { } followUp
			|| !ReferenceEquals(GetFanTheHammerFollowUpForThisTurn(monster), followUp)
			|| !CanPerformJeweledGauntletRepeat(monster, plan.Current, monster.Creature.CombatState))
		{
			return;
		}

		bool repeatFollowUp = ShouldRepeatJeweledGauntletMove(monster, followUp);
		BeginFanTheHammerFollowUp(monster, followUp);
		if (await PerformExtraMonsterMove(monster, followUp, "enemy Fan the Hammer") && repeatFollowUp)
		{
			await PerformExtraMonsterMove(monster, followUp, "enemy Jeweled Gauntlet");
		}
	}

	// 按原版 PerformMove 的同一顺序执行一次 NextMove：
	// 等待 → 置 _isPerformingMove → MoveState.PerformMove → 记战斗历史 → 清标志 → 死亡移除 → 等待。
	// 原版没有"本回合额外执行一次行动"的 Hook；刻意省略 MoveStateMachine.OnMovePerformed，状态机不因此前进。
	// 这段复制写在 Postfix 里而非跳过型前缀，因此由 HextechPatch.CopiesVanillaLogic 把 PerformMove 纳入原版拷贝守卫。
	private static async Task<bool> PerformExtraMonsterMove(
		MonsterModel monster,
		MoveState move,
		string reason)
	{
		Creature creature = monster.Creature;
		HextechCombatState? combatState = creature.CombatState;
		if (!CanPerformJeweledGauntletRepeat(monster, move, combatState))
		{
			return false;
		}

		await Cmd.CustomScaledWait(0.1f, 0.2f);
		// 等待期间第三方延迟伤害、阶段切换或遭遇销毁都可能改写怪物状态;写入执行态前按当前状态再核一次。
		// 战斗容器换了(生物被移入另一场战斗)也取消:后面的目标、历史与移除都要与检查用的是同一个 combatState。
		if (!ReferenceEquals(creature.CombatState, combatState)
			|| !CanPerformJeweledGauntletRepeat(monster, move, combatState))
		{
			return false;
		}

		MonsterIsPerformingMoveField.SetValue(monster, true);
		IReadOnlyList<Creature> targets = combatState!.PlayerCreatures.ToArray();
		try
		{
			HextechLog.Info("JeweledGauntlet", $"Monster {monster.Id.Entry} performing extra move {move.Id} via {reason}");
			await move.PerformMove(targets);
			CombatManager.Instance.History.MonsterPerformedMove(combatState, monster, move, targets);
		}
		finally
		{
			MonsterIsPerformingMoveField.SetValue(monster, false);
		}

		if (creature.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature))
		{
			combatState.RemoveCreature(creature);
		}

		await Cmd.CustomScaledWait(0.1f, 0.4f);
		return true;
	}

	private static bool CanPerformJeweledGauntletRepeat(
		MonsterModel monster,
		MoveState capturedMove,
		HextechCombatState? combatState)
	{
		if (!CombatManager.Instance.IsInProgress
			|| combatState == null
			|| monster.Creature.IsDead
			|| !combatState.Creatures.Contains(monster.Creature))
		{
			return false;
		}

		// 部分 Boss 阶段转换、逃跑或特殊动作会在第一次执行期间强制改写 NextMove。
		// 即使旧动作的意图看似普通，也不能在状态机已前进后再执行其捕获委托。
		return ReferenceEquals(monster.NextMove, capturedMove);
	}

	private static void RestoreJeweledGauntletIntents(JeweledGauntletIntentPatchState? state)
	{
		if (state == null)
		{
			return;
		}

		try
		{
			// 只撤销自己临时安装的列表；若别的逻辑在 UpdateIntent 期间确实改了行动，则不覆盖它。
			if (ReferenceEquals(MoveStateIntentsField.GetValue(state.Move), state.DisplayedIntents))
			{
				MoveStateIntentsField.SetValue(state.Move, state.OriginalIntents);
			}
		}
		catch (Exception ex)
		{
			LogJeweledGauntletFailure(nameof(RestoreJeweledGauntletIntents), ex);
		}
	}

	private static bool ShouldRepeatJeweledGauntletMove(MonsterModel monster, MoveState move)
	{
		Creature creature = monster.Creature;
		// 稳定随机数需要具体 RunState 作为种子来源。
		if (creature.CombatId is not uint combatId
			|| creature.CombatState is not { } combatState
			|| combatState.RunState is not RunState runState
			|| !TryGetActiveEnemyHexModifier(creature, MonsterHexKind.JeweledGauntlet, out HextechMayhemModifier? modifier))
		{
			return false;
		}

		if (ShouldSuppressJeweledGauntletRepeat(monster, move))
		{
			return false;
		}

		if (!AreJeweledGauntletIntentsRepeatable(move.Intents))
		{
			return false;
		}

		int chance = modifier.GetMonsterHexStrengthTier(MonsterHexKind.JeweledGauntlet) switch
		{
			<= 1 => 10,
			2 => 20,
			_ => 30
		};
		return HextechStableRandom.PercentChance(
			runState,
			chance,
			"enemy-jeweled-gauntlet-repeat",
			combatId.ToString(CultureInfo.InvariantCulture),
			combatState.RoundNumber.ToString(CultureInfo.InvariantCulture),
			move.Id);
	}

	private static bool ShouldSuppressJeweledGauntletRepeat(
		MonsterModel monster,
		MoveState move)
	{
		if (move.Id is TestSubjectRespawnMoveId or IllusionReviveMoveId)
		{
			return true;
		}

		if (monster is TheInsatiable && move.Id == TheInsatiableOpeningMoveId)
		{
			return true;
		}

		if (monster is not KnowledgeDemon)
		{
			return false;
		}

		int curseCounter = (int)KnowledgeDemonCurseCounterField.GetValue(monster)!;
		return WouldRepeatFinalKnowledgeDemonCurse(move.Id, curseCounter);
	}

	internal static bool WouldRepeatFinalKnowledgeDemonCurse(string moveId, int curseCounter)
	{
		// 原行动会先消费当前阶段，珠光护手的重复行动随后消费下一阶段。
		// 因此计数为 1 时就必须阻止重复，否则第二阶段的暴击会直接执行第三阶段。
		return curseCounter + 1 >= FinalKnowledgeDemonCurseIndex
			&& string.Equals(moveId, KnowledgeDemonCurseMoveId, StringComparison.Ordinal);
	}

	internal static bool AreJeweledGauntletIntentsRepeatable(IReadOnlyList<AbstractIntent> intents)
	{
		// 偷牌成功会立刻安排逃跑，改变行动对象；预览也必须排除这类不会实际重复的行动。
		return intents.Count > 0 && !intents.Any(static intent => intent is ThievingHopperTheftIntent)
			&& intents.All(static intent => IsJeweledGauntletIntentTypeRepeatable(intent.IntentType));
	}

	internal static bool IsJeweledGauntletIntentTypeRepeatable(IntentType intentType)
	{
		return intentType is IntentType.Attack
			or IntentType.Buff
			or IntentType.CardDebuff
			or IntentType.Debuff
			or IntentType.DebuffStrong
			or IntentType.Defend
			or IntentType.Heal
			or IntentType.StatusCard;
	}

	internal static IReadOnlyList<AbstractIntent> DuplicateJeweledGauntletIntentGroup(
		IReadOnlyList<AbstractIntent> intents)
	{
		return ComposeMonsterTurnIntents(intents, repeatCurrent: true, followUp: [], repeatFollowUp: false);
	}

	/// <summary>本回合将要执行的行动按执行顺序排成一组意图：A（珠光护手时 A 两遍）再接连拨击锤的 B（同理）。</summary>
	internal static IReadOnlyList<AbstractIntent> ComposeMonsterTurnIntents(
		IReadOnlyList<AbstractIntent> current,
		bool repeatCurrent,
		IReadOnlyList<AbstractIntent> followUp,
		bool repeatFollowUp)
	{
		List<AbstractIntent> composed = new(current.Count * 2 + followUp.Count * 2);
		composed.AddRange(current);
		if (repeatCurrent)
		{
			composed.AddRange(current);
		}

		composed.AddRange(followUp);
		if (repeatFollowUp)
		{
			composed.AddRange(followUp);
		}

		return composed.ToArray();
	}

	private static void LogJeweledGauntletFailure(string hook, Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("combat.jeweled-gauntlet-failure", 10))
		{
			HextechLog.Error("Mayhem", $"{hook} failed; enemy Jeweled Gauntlet fell back to one action: {ex}");
		}
	}

	private sealed record MonsterTurnExtraMoves(
		MonsterModel Monster,
		MoveState Current,
		bool RepeatCurrent,
		MoveState? FollowUp);

	private sealed record JeweledGauntletIntentPatchState(
		MoveState Move,
		IReadOnlyList<AbstractIntent> OriginalIntents,
		IReadOnlyList<AbstractIntent> DisplayedIntents);

	[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove), new Type[0])]
	[HextechPatch(
		"combat.jeweled-gauntlet.perform-move",
		"珠光护手、连拨击锤",
		MonsterHexes = [MonsterHexKind.JeweledGauntlet, MonsterHexKind.FanTheHammer],
		CopiesVanillaLogic = true)]
	private static class JeweledGauntletPerformMovePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => JeweledGauntletHooksAvailable;

		[HarmonyPrefix]
		private static void Prefix(
			MonsterModel __instance,
			out MonsterTurnExtraMoves? __state)
		{
			__state = null;
			try
			{
				MoveState current = __instance.NextMove;
				bool repeatCurrent = ShouldRepeatJeweledGauntletMove(__instance, current);
				MoveState? followUp = GetFanTheHammerFollowUpForThisTurn(__instance);
				if (repeatCurrent || followUp != null)
				{
					__state = new MonsterTurnExtraMoves(__instance, current, repeatCurrent, followUp);
				}
			}
			catch (Exception ex)
			{
				LogJeweledGauntletFailure("JeweledGauntletPerformMovePatch.Prefix", ex);
			}
		}

		[HarmonyPostfix]
		private static void Postfix(
			ref Task __result,
			MonsterTurnExtraMoves? __state)
		{
			if (__state != null)
			{
				__result = PerformMonsterTurnExtraMoves(__result, __state);
			}
		}
	}

	[HarmonyPatch(typeof(NCreature), nameof(NCreature.UpdateIntent), typeof(IEnumerable<Creature>))]
	[HextechPatch(
		"combat.jeweled-gauntlet.update-intent",
		"珠光护手、连拨击锤",
		MonsterHexes = [MonsterHexKind.JeweledGauntlet, MonsterHexKind.FanTheHammer])]
	private static class JeweledGauntletUpdateIntentPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => JeweledGauntletHooksAvailable;

		// 只读：判定与 PerformMove 前缀同源（稳定随机 + 连拨击锤登记表），不写任何共享状态。
		[HarmonyPrefix]
		private static void Prefix(
			NCreature __instance,
			out JeweledGauntletIntentPatchState? __state)
		{
			__state = null;
			try
			{
				MonsterModel? monster = __instance.Entity?.Monster;
				if (monster == null)
				{
					return;
				}

				MoveState move = monster.NextMove;
				bool repeatCurrent = ShouldRepeatJeweledGauntletMove(monster, move);
				MoveState? followUp = GetFanTheHammerFollowUpForThisTurn(monster);
				if (!repeatCurrent && followUp == null)
				{
					return;
				}

				bool repeatFollowUp = followUp != null && ShouldRepeatJeweledGauntletMove(monster, followUp);
				IReadOnlyList<AbstractIntent> originalIntents = move.Intents;
				IReadOnlyList<AbstractIntent> displayedIntents = ComposeMonsterTurnIntents(
					originalIntents,
					repeatCurrent,
					followUp?.Intents ?? [],
					repeatFollowUp);
				MoveStateIntentsField.SetValue(move, displayedIntents);
				__state = new JeweledGauntletIntentPatchState(move, originalIntents, displayedIntents);
			}
			catch (Exception ex)
			{
				LogJeweledGauntletFailure("JeweledGauntletUpdateIntentPatch.Prefix", ex);
			}
		}

		// Finalizer 在正常返回与异常时都会执行，恢复只需要这一处。
		[HarmonyFinalizer]
		private static Exception? Finalizer(
			Exception? __exception,
			JeweledGauntletIntentPatchState? __state)
		{
			RestoreJeweledGauntletIntents(__state);
			return __exception;
		}
	}
}
