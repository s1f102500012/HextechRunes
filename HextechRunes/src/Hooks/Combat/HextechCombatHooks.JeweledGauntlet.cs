using System.Diagnostics.CodeAnalysis;
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

	// 在原版 MonsterModel.PerformMove（0.107.1/0.111.0）完成后按同一顺序再执行一次行动：
	// 等待 → 置 _isPerformingMove → MoveState.PerformMove → 记战斗历史 → 清标志 → 死亡移除 → 等待。
	// 原版没有"重复执行本回合行动"的 Hook；刻意省略 MoveStateMachine.OnMovePerformed，状态机只前进一次。
	// 这段复制写在 Postfix 里而非跳过型前缀，因此由 HextechPatch.CopiesVanillaLogic 把 PerformMove 纳入原版拷贝守卫。
	private static async Task RepeatJeweledGauntletMoveAfterOriginal(
		Task originalTask,
		JeweledGauntletMoveRepeatState repeatState)
	{
		await originalTask;

		MonsterModel monster = repeatState.Monster;
		Creature creature = monster.Creature;
		HextechCombatState? combatState = creature.CombatState;
		if (!CanPerformJeweledGauntletRepeat(monster, repeatState.Move, combatState))
		{
			return;
		}

		await Cmd.CustomScaledWait(0.1f, 0.2f);
		// 等待期间第三方延迟伤害、阶段切换或遭遇销毁都可能改写怪物状态;写入执行态前按当前状态再核一次。
		// 战斗容器换了(生物被移入另一场战斗)也取消:后面的目标、历史与移除都要与检查用的是同一个 combatState。
		if (!ReferenceEquals(creature.CombatState, combatState)
			|| !CanPerformJeweledGauntletRepeat(monster, repeatState.Move, combatState))
		{
			return;
		}

		MonsterIsPerformingMoveField.SetValue(monster, true);
		IReadOnlyList<Creature> targets = combatState!.PlayerCreatures.ToArray();
		try
		{
			HextechLog.Info("JeweledGauntlet", $"Monster {monster.Id.Entry} repeating move {repeatState.Move.Id} via enemy Jeweled Gauntlet");
			await repeatState.Move.PerformMove(targets);
			CombatManager.Instance.History.MonsterPerformedMove(combatState, monster, repeatState.Move, targets);
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

	private static bool ShouldRepeatJeweledGauntletMove(
		MonsterModel monster,
		[NotNullWhen(true)] out MoveState? move)
	{
		move = null;
		Creature creature = monster.Creature;
		// 稳定随机数需要具体 RunState 作为种子来源。
		if (creature.CombatId is not uint combatId
			|| creature.CombatState is not { } combatState
			|| combatState.RunState is not RunState runState
			|| !TryGetActiveEnemyHexModifier(creature, MonsterHexKind.JeweledGauntlet, out HextechMayhemModifier? modifier))
		{
			return false;
		}

		move = monster.NextMove;
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
		AbstractIntent[] duplicated = new AbstractIntent[intents.Count * 2];
		for (int i = 0; i < intents.Count; i++)
		{
			duplicated[i] = intents[i];
			duplicated[i + intents.Count] = intents[i];
		}

		return duplicated;
	}

	private static void LogJeweledGauntletFailure(string hook, Exception ex)
	{
		if (HextechRunLogBudget.TryConsume("combat.jeweled-gauntlet-failure", 10))
		{
			HextechLog.Error("Mayhem", $"{hook} failed; enemy Jeweled Gauntlet fell back to one action: {ex}");
		}
	}

	private sealed record JeweledGauntletMoveRepeatState(MonsterModel Monster, MoveState Move);

	private sealed record JeweledGauntletIntentPatchState(
		MoveState Move,
		IReadOnlyList<AbstractIntent> OriginalIntents,
		IReadOnlyList<AbstractIntent> DisplayedIntents);

	[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove), new Type[0])]
	[HextechPatch("combat.jeweled-gauntlet.perform-move", "珠光护手", CopiesVanillaLogic = true)]
	private static class JeweledGauntletPerformMovePatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => JeweledGauntletHooksAvailable;

		[HarmonyPrefix]
		private static void Prefix(
			MonsterModel __instance,
			out JeweledGauntletMoveRepeatState? __state)
		{
			__state = null;
			try
			{
				if (ShouldRepeatJeweledGauntletMove(__instance, out MoveState? move))
				{
					__state = new JeweledGauntletMoveRepeatState(__instance, move);
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
			JeweledGauntletMoveRepeatState? __state)
		{
			if (__state != null)
			{
				__result = RepeatJeweledGauntletMoveAfterOriginal(__result, __state);
			}
		}
	}

	[HarmonyPatch(typeof(NCreature), nameof(NCreature.UpdateIntent), typeof(IEnumerable<Creature>))]
	[HextechPatch("combat.jeweled-gauntlet.update-intent", "珠光护手")]
	private static class JeweledGauntletUpdateIntentPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() => JeweledGauntletHooksAvailable;

		[HarmonyPrefix]
		private static void Prefix(
			NCreature __instance,
			out JeweledGauntletIntentPatchState? __state)
		{
			__state = null;
			try
			{
				MonsterModel? monster = __instance.Entity?.Monster;
				if (monster == null
					|| !ShouldRepeatJeweledGauntletMove(monster, out MoveState? move))
				{
					return;
				}

				IReadOnlyList<AbstractIntent> originalIntents = move.Intents;
				IReadOnlyList<AbstractIntent> displayedIntents = DuplicateJeweledGauntletIntentGroup(originalIntents);
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
