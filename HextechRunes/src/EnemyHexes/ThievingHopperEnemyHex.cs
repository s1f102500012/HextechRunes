using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

internal sealed class ThievingHopperEnemyHex : HextechEnemyHexEffect
{
	internal const string EscapeMoveId = "HEXTECH_THIEVING_HOPPER_ESCAPE";

	internal override MonsterHexKind Kind => MonsterHexKind.ThievingHopper;

	internal static string TheftKey(uint combatId) => $"enemy-thieving-hopper-stolen:{combatId}";

	internal static bool CanPlanTheft(Creature enemy, HextechEnemyHexContext context)
	{
		return enemy.Side == CombatSide.Enemy && !enemy.IsDead && !enemy.IsStunned
			&& !HasTheftBlockingPower(enemy.Powers)
			&& !IsTrainingDummy(enemy.Monster)
			&& enemy.CombatId is uint id && enemy.CombatState is { } combat && combat.RunState == context.RunState
			&& !IsProtectedBoss(combat.Encounter?.RoomType, enemy.IsPrimaryEnemy)
			&& HextechCombatProcTracker.GetGlobalProcsInCombat(context.Tracking, TheftKey(id)) == 0
			&& enemy.Monster is { } monster && monster.NextMove.Id != EscapeMoveId
			&& !monster.NextMove.Intents.Any(i => i.IntentType is IntentType.Escape or IntentType.Stun);
	}

	// 训练人偶事件的靶子只有一个什么都不做的行动，是给玩家限时输出用的；偷牌逃跑会让事件直接没有靶子可打。
	internal static bool IsTrainingDummy(MonsterModel? monster)
		=> monster is BattleFriendV1 or BattleFriendV2 or BattleFriendV3;

	internal static bool IsProtectedBoss(RoomType? roomType, bool isPrimaryEnemy)
		=> roomType == RoomType.Boss && isPrimaryEnemy;

	internal static bool HasTheftBlockingPower(IEnumerable<PowerModel> powers)
		// 睡眠不属于 Stun；Minion 的离场常由首领的死亡回调驱动，不能让逃跑绕过该关系。
		=> powers.Any(power => power is AsleepPower or SlumberPower or MinionPower);

	internal const int TheftHpThresholdPercent = 20;

	internal static bool IsBelowTheftThreshold(int currentHp, int maxHp)
		=> maxHp > 0 && (long)currentHp * 100 < (long)maxHp * TheftHpThresholdPercent;

	internal static bool IsBelowTheftThreshold(Creature enemy) => IsBelowTheftThreshold(enemy.CurrentHp, enemy.MaxHp);

	// 意图在敌方回合末 RollMove 时规划，那一刻已低于阈值的敌人直接计划偷牌；
	// 玩家回合里才被打到阈值以下的，立即把偷牌意图补进已经显示的下一次行动，不必再等一轮。
	internal override async Task AfterEnemyDamageReceived(HextechEnemyHexContext context, Creature target, uint combatId, DamageResult result, Creature? dealer, CardModel? cardSource)
	{
		if (target.CombatState?.CurrentSide != CombatSide.Player
			|| target.Monster is not { } monster
			|| !IsBelowTheftThreshold(target)
			|| !CanPlanTheft(target, context))
		{
			return;
		}

		await HextechCombatHooks.PlanThievingHopperTheftNow(monster);
	}

	internal static int StealPriority(CardModel card)
	{
		// 与原版 ThievingHopper 相同，优先顺走罕见牌，远古牌与灌注牌最后选择。
		if (card.Enchantment is Imbued || card.Rarity == CardRarity.Ancient)
		{
			return 3;
		}

		return card.Rarity switch
		{
			CardRarity.Uncommon => 0,
			CardRarity.Common or CardRarity.Rare or CardRarity.Event => 1,
			CardRarity.Basic or CardRarity.Quest => 2,
			_ => 4
		};
	}

	internal static async Task StealAndPlanEscape(HextechEnemyHexContext context, Creature enemy)
	{
		// CanPlanTheft 保证 CombatId、CombatState 与 Monster 均非空。
		if (!CanPlanTheft(enemy, context)
			|| enemy.CombatId is not uint combatId
			|| enemy.CombatState is not { } combatState)
		{
			return;
		}

		// 敌人行动时已弃牌；与原版一样只从抽/弃牌堆选择仍在牌组中的原件，
		// 防止不同怪物通过战斗复制牌重复偷走同一张牌组原件。联机每个敌人也只偷一张。
		CardModel[][] targets = combatState.Players
			.Where(static player => !player.Creature.IsDead)
			.OrderBy(static player => player.NetId)
			.Select(static player => CardPile.GetCards(player, PileType.Draw, PileType.Discard)
				.Where(card => card.DeckVersion != null && player.Deck.Cards.Contains(card.DeckVersion))
				.ToArray())
			.Where(static cards => cards.Length > 0)
			.ToArray();
		if (targets.Length == 0)
		{
			return;
		}

		string id = combatId.ToString(CultureInfo.InvariantCulture);
		CardModel[] cards = targets[HextechStableRandom.Index(context.RunState, targets.Length, "hopper-player", id)];
		int priority = cards.Min(StealPriority);
		CardModel[] pool = cards.Where(card => StealPriority(card) == priority).ToArray();
		CardModel stolenCard = pool[HextechStableRandom.Index(context.RunState, pool.Length, "hopper-card", id)];
		HextechCombatProcTracker.ConsumeGlobalProcInCombat(context.Tracking, TheftKey(combatId));
		await CardPileCmd.RemoveFromCombat(stolenCard);
		SwipePower swipe = (SwipePower)ModelDb.Power<SwipePower>().ToMutable();
		await swipe.Steal(stolenCard);
		await PowerCmd.Apply(swipe, enemy, 1m, enemy, null);

		if (enemy.IsDead
			|| enemy.CombatState == null
			|| enemy.Monster is not { MoveStateMachine: { } moveStateMachine } monster)
		{
			return;
		}

		MoveState escape = CreateEscapeMove(() => Escape(enemy));
		moveStateMachine.States[EscapeMoveId] = escape;
		monster.SetMoveImmediate(escape, forceTransition: true);
	}

	internal static MoveState CreateEscapeMove(Func<Task> escapeAction)
	{
		MoveState escape = new(EscapeMoveId, _ => escapeAction(), new EscapeIntent());
		// 自循环已经能保留下一次 RollMove 的逃跑意图。不能锁住 CanTransitionAway，
		// 否则 ReattachPower 的 SetMoveImmediate(DeadState) 会被拒绝，死亡节段无法复活。
		escape.FollowUpState = escape;
		return escape;
	}

	private static async Task Escape(Creature enemy)
	{
		if (enemy.IsDead || enemy.CombatState == null)
		{
			return;
		}

		try
		{
			NCreature? node = NCombatRoom.Instance?.GetCreatureNode(enemy);
			if (node != null && GodotObject.IsInstanceValid(node))
			{
				node.ToggleIsInteractable(false);
				// 保持模型自己的待机姿态，仅移动表现节点；不要求各怪物提供 Flee 动画。
				node.SetAnimationTrigger("Idle");
				Tween tween = node.CreateTween();
				float distance = Math.Max(2400f, node.GetViewportRect().Size.X * 2f);
				tween.TweenProperty(node.Visuals, "position:x", node.Visuals.Position.X + distance, 0.85);
				await Cmd.Wait(0.9f);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("ThievingHopper", $"Escape presentation failed; continuing vanilla escape: {ex.Message}");
		}
		// 不走死亡命令：逃跑不应触发顺走的击杀返还。
		await CreatureCmd.Escape(enemy);
	}
}

internal sealed class ThievingHopperTheftIntent(Creature source) : CardDebuffIntent
{
	internal Creature Source => source;
}
