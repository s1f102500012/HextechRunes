#if STS2_113_OR_NEWER
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

// 愚者箭矢组（箭无虚发、忠诚、穿云箭、百步穿杨、回马枪）的战斗逻辑测试。
// 夹具里的战斗没有开始，原版 Hook 分发整体跳过（ShouldSkipCombatEffect），所以由测试按原版 ArrowCmd / AttackCommand 的顺序
// 手动调用各模型的钩子；PowerCmd / ArrowCmd 的数值路径与原版能力自己的逻辑（AimPower、ImpaledPower）走真实代码。
internal static partial class Program
{
	private const ValueProp ArrowAttackProps = ValueProp.Move | ValueProp.FromArrow;

	private static PlayerChoiceContext ArrowTestContext() => new BlockingPlayerChoiceContext();

	private static IDisposable InjectArrowModels() => InjectMissingModels(typeof(AimPower), typeof(ImpaledPower));

	private static AttackCommand CreateArrowTestAttack(Creature attacker, ValueProp props = ArrowAttackProps)
	{
		AttackCommand command = new(5m);
		SetAutoProperty(command, nameof(AttackCommand.Attacker), attacker);
		SetAutoProperty(command, nameof(AttackCommand.DamageProps), props);
		return command;
	}

	private static T CreateArrowRune<T>(Player owner)
		where T : HextechRelicBase, new()
	{
		T rune = CreateMutableTestModel<T>();
		rune.Owner = owner;
		return rune;
	}

	private static int AimOf(Player player) => player.Creature.GetPower<AimPower>()?.Amount ?? 0;

	private static void GiveAim(Player player, int amount)
	{
		PowerCmd.Apply<AimPower>(ArrowTestContext(), player.Creature, amount, player.Creature, null).GetAwaiter().GetResult();
	}

	private static void SetArrowTestBlock(Creature creature, int block)
	{
		AccessTools.Field(typeof(Creature), "_block").SetValue(creature, block);
	}

	// 按原版 ArrowCmd.DislodgeArrow(ICombatState, ImpaledPower, ...) 的顺序：先取下第一支箭，再触发 AfterArrowDislodged，最后收回箭。
	private static void DislodgeFirstArrowWithHook(ImpaledPower power, params HextechRelicBase[] listeners)
	{
		ArrowSlot arrow = power.RemoveFirstArrow();
		foreach (HextechRelicBase listener in listeners)
		{
			listener.AfterArrowDislodged(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		}

		arrow.Status = ArrowStatus.Retrieved;
	}

	/// <summary>箭无虚发：自己的箭松脱获得 {AimPower} 点瞄准；队友的箭、非愚者持有者不触发。</summary>
	[HextechTest]
	private static void FoolSureShotGainsAimOnOwnDislodgeOnly()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		SureShotRune rune = CreateArrowRune<SureShotRune>(fool);
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		int aimPerDislodge = rune.DynamicVars["AimPower"].IntValue;

		(ImpaledPower own, _) = combat.Impale(enemy, fool);
		DislodgeFirstArrowWithHook(own, rune);
		Equal(aimPerDislodge, AimOf(fool), "own dislodge grants aim");

		(ImpaledPower mate, _) = combat.Impale(enemy, teammate);
		DislodgeFirstArrowWithHook(mate, rune);
		Equal(aimPerDislodge, AimOf(fool), "a teammate's arrow does not count as yours");
		Equal(0, AimOf(teammate), "the teammate gains nothing either");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		SureShotRune foreign = CreateArrowRune<SureShotRune>(ironclad);
		(ImpaledPower foreignPower, _) = combat.Impale(enemy, ironclad);
		DislodgeFirstArrowWithHook(foreignPower, foreign);
		Equal(0, AimOf(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 箭无虚发：持有者攻击进行中的松脱（射击命中让旧箭数满松脱）留到攻击结束再获得，
	/// 落在原版 AimPower 扣除并移除旧实例之后的新实例上，之后的射击照常登记与消耗；嵌套攻击只在最外层结束时获得一次。
	/// </summary>
	[HextechTest]
	private static void FoolSureShotDefersAimUntilOwnerAttackEnds()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Creature enemy = combat.AddEnemy();
		SureShotRune rune = CreateArrowRune<SureShotRune>(fool);
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		int aimPerDislodge = rune.DynamicVars["AimPower"].IntValue;

		GiveAim(fool, 3);
		AimPower spentAim = fool.Creature.GetPower<AimPower>()!;
		(ImpaledPower power, _) = combat.Impale(enemy, fool);
		AttackCommand shot = CreateArrowTestAttack(fool.Creature);
		AttackCommand nested = CreateArrowTestAttack(fool.Creature, ValueProp.Move);

		spentAim.BeforeAttack(shot).GetAwaiter().GetResult();
		rune.BeforeAttack(shot).GetAwaiter().GetResult();
		rune.BeforeAttack(nested).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, rune);
		Equal(3, AimOf(fool), "aim is not added mid-attack");
		rune.AfterAttack(ArrowTestContext(), nested).GetAwaiter().GetResult();
		Equal(3, AimOf(fool), "the inner attack ending does not grant yet");

		// 同一生物的能力先于遗物收到 AfterAttack：原版扣掉开始时的 3 层并移除实例。
		spentAim.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		Expect(!fool.Creature.Powers.Contains(spentAim), "vanilla removes the spent aim instance");
		rune.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		AimPower freshAim = fool.Creature.GetPower<AimPower>()!;
		Expect(freshAim != spentAim, "deferred aim lands on a fresh instance");
		Equal(aimPerDislodge, freshAim.Amount, "deferred aim granted once at the end of the outer attack");
		rune.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		Equal(aimPerDislodge, AimOf(fool), "a repeated AfterAttack grants nothing more");

		// 新实例能正常登记下一次射击并被整笔消耗。
		AttackCommand nextShot = CreateArrowTestAttack(fool.Creature);
		freshAim.BeforeAttack(nextShot).GetAwaiter().GetResult();
		freshAim.AfterAttack(ArrowTestContext(), nextShot).GetAwaiter().GetResult();
		Equal(0, AimOf(fool), "the fresh instance is spent by the next shot");
	}

	/// <summary>忠诚：持有者回合结束时按战斗编号逐个让自己的箭全部松脱；队友的箭不动；敌方回合与非愚者不触发。</summary>
	[HextechTest]
	private static void FoolLoyaltyDislodgesOwnArrowsAtOwnTurnEnd()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 3);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature first = combat.AddEnemy();
		Creature second = combat.AddEnemy();
		// 加入顺序与战斗编号相反，检验结算按编号而不是列表顺序。
		first.CombatId = 20;
		second.CombatId = 10;
		combat.Impale(first, fool);
		combat.Impale(first, fool);
		combat.Impale(second, fool);
		(ImpaledPower mate, ArrowSlot mateArrow) = combat.Impale(first, teammate);
		List<Creature> retrievalOrder = [];
		foreach (ImpaledPower power in new[] { first, second }.SelectMany(static creature => creature.GetPowerInstances<ImpaledPower>()))
		{
			Creature target = power.Owner;
			foreach (ArrowSlot arrow in power.Arrows)
			{
				arrow.ArrowStatusUpdated += () => retrievalOrder.Add(target);
			}
		}

		LoyaltyRune rune = CreateArrowRune<LoyaltyRune>(fool);
		rune.BeforeTurnEnd(ArrowTestContext(), CombatSide.Enemy).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(fool), "the enemy turn end does not trigger");

		rune.BeforeTurnEnd(ArrowTestContext(), CombatSide.Player).GetAwaiter().GetResult();
		Equal(3, ArrowCmd.GetAvailableArrowCount(fool), "all own arrows are back");
		Expect(!ArrowCmd.CreatureIsImpaledByPlayersArrow(first, fool) && !ArrowCmd.CreatureIsImpaledByPlayersArrow(second, fool), "own impaled powers are removed");
		SequenceEqual(new[] { second, first, first }, retrievalOrder, "targets resolve by combat id");
		Equal(ArrowStatus.Away, mateArrow.Status, "the teammate's arrow stays");
		Expect(first.Powers.Contains(mate), "the teammate's impaled power stays");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		combat.Impale(second, ironclad);
		CreateArrowRune<LoyaltyRune>(ironclad).BeforeTurnEnd(ArrowTestContext(), CombatSide.Player).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 穿云箭：新扎穿与数满重置后的计数都压到 3−{HitReduction}=2，于是第二次攻击就松脱；
	/// 已经数过的计数不抬高，最后一支箭松脱时不动，队友的箭与非愚者不受影响，下限为 1。
	/// </summary>
	[HextechTest]
	private static void FoolCloudPiercerLowersHitCounter()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		CloudPiercerRune rune = CreateArrowRune<CloudPiercerRune>(fool);
		int vanillaHits = ModelDb.Power<ImpaledPower>().HitCounter;
		int expected = vanillaHits - rune.DynamicVars["HitReduction"].IntValue;

		(ImpaledPower power, ArrowSlot firstArrow) = combat.Impale(enemy, fool);
		Equal(vanillaHits, power.HitCounter, "vanilla starts at its canonical count");
		rune.AfterArrowImpaled(power, firstArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(expected, power.HitCounter, "a fresh impale starts lowered");
		(_, ArrowSlot secondArrow) = combat.Impale(enemy, fool);
		DamageResult hit = new(enemy, ArrowAttackProps);
		power.AfterDamageReceived(ArrowTestContext(), enemy, hit, ValueProp.Move, fool.Creature, null).GetAwaiter().GetResult();
		rune.AfterArrowImpaled(power, secondArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(expected - 1, power.HitCounter, "an existing count is not raised by a later impale");

		// 原版数到 0：写死重置为 3，松脱第一支（钩子在夹具里不分发，由测试补调），仍剩一支。
		power.AfterDamageReceived(ArrowTestContext(), enemy, hit, ValueProp.Move, fool.Creature, null).GetAwaiter().GetResult();
		Equal(ArrowStatus.Retrieved, firstArrow.Status, "the second hit already dislodges");
		Equal(vanillaHits, power.HitCounter, "vanilla reset");
		rune.AfterArrowDislodged(power, firstArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(expected, power.HitCounter, "the reset count is lowered while arrows remain");

		ArrowSlot last = power.RemoveFirstArrow();
		power.HitCounter = vanillaHits;
		rune.AfterArrowDislodged(power, last, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(vanillaHits, power.HitCounter, "nothing to adjust once the last arrow leaves");

		(ImpaledPower mate, ArrowSlot mateArrow) = combat.Impale(enemy, teammate);
		rune.AfterArrowImpaled(mate, mateArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(vanillaHits, mate.HitCounter, "a teammate's impale keeps the vanilla count");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		(ImpaledPower foreign, ArrowSlot foreignArrow) = combat.Impale(enemy, ironclad);
		CreateArrowRune<CloudPiercerRune>(ironclad).AfterArrowImpaled(foreign, foreignArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(vanillaHits, foreign.HitCounter, "non-Fool holders do not trigger");

		Creature other = combat.AddEnemy();
		(ImpaledPower floored, ArrowSlot flooredArrow) = combat.Impale(other, fool);
		rune.DynamicVars["HitReduction"].BaseValue = vanillaHits + 2;
		rune.AfterArrowImpaled(floored, flooredArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(1, floored.HitCounter, "never below one hit");
	}

	/// <summary>
	/// 百步穿杨：原版扣光开始时的层数后补回一半（向下取整：5 → 留 2，2 → 留 1，1 → 留 0），
	/// 补回的新实例照常被下一次射击消耗；非箭矢攻击、没有瞄准、队友的攻击、非愚者都不补；同一攻击只补一次。
	/// </summary>
	[HextechTest]
	private static void FoolDeadeyeKeepsHalfOfSpentAim()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 1);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		combat.AddEnemy();
		DeadeyeRune rune = CreateArrowRune<DeadeyeRune>(fool);
		rune.BeforeCombatStart().GetAwaiter().GetResult();

		// 同一生物的能力先于遗物收到 BeforeAttack / AfterAttack；AfterAttack 重复一次，检验只补一次。
		void Shoot(Player shooter, DeadeyeRune holder, ValueProp props = ArrowAttackProps)
		{
			AttackCommand command = CreateArrowTestAttack(shooter.Creature, props);
			AimPower? aim = shooter.Creature.GetPower<AimPower>();
			aim?.BeforeAttack(command).GetAwaiter().GetResult();
			holder.BeforeAttack(command).GetAwaiter().GetResult();
			aim?.AfterAttack(ArrowTestContext(), command).GetAwaiter().GetResult();
			holder.AfterAttack(ArrowTestContext(), command).GetAwaiter().GetResult();
			holder.AfterAttack(ArrowTestContext(), command).GetAwaiter().GetResult();
		}

		Shoot(fool, rune);
		Equal(0, AimOf(fool), "no aim, nothing to keep");

		GiveAim(fool, 5);
		Shoot(fool, rune, ValueProp.Move);
		Equal(5, AimOf(fool), "a non-arrow attack spends no aim");
		Shoot(fool, rune);
		Equal(2, AimOf(fool), "spending 5 keeps 2 (rounded down), only once per attack");
		Shoot(fool, rune);
		Equal(1, AimOf(fool), "the kept aim is spent normally by the next shot");
		Shoot(fool, rune);
		Equal(0, AimOf(fool), "spending 1 keeps nothing");

		GiveAim(teammate, 5);
		Shoot(teammate, rune);
		Equal(0, AimOf(teammate), "a teammate's shot is not refunded");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 0);
		GiveAim(ironclad, 5);
		Shoot(ironclad, CreateArrowRune<DeadeyeRune>(ironclad));
		Equal(0, AimOf(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>百步穿杨 + 箭无虚发：射击途中松脱所得的瞄准不计入本次消耗，也不被补回；两者各结算一次，不互相触发。</summary>
	[HextechTest]
	private static void FoolDeadeyeAndSureShotStackWithoutDoubleRefund()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Creature enemy = combat.AddEnemy();
		DeadeyeRune deadeye = CreateArrowRune<DeadeyeRune>(fool);
		SureShotRune sureShot = CreateArrowRune<SureShotRune>(fool);
		deadeye.BeforeCombatStart().GetAwaiter().GetResult();
		sureShot.BeforeCombatStart().GetAwaiter().GetResult();
		int aimPerDislodge = sureShot.DynamicVars["AimPower"].IntValue;

		GiveAim(fool, 5);
		(ImpaledPower power, _) = combat.Impale(enemy, fool);
		AttackCommand shot = CreateArrowTestAttack(fool.Creature);
		AimPower aim = fool.Creature.GetPower<AimPower>()!;
		aim.BeforeAttack(shot).GetAwaiter().GetResult();
		deadeye.BeforeAttack(shot).GetAwaiter().GetResult();
		sureShot.BeforeAttack(shot).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, deadeye, sureShot);
		aim.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		deadeye.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		sureShot.AfterAttack(ArrowTestContext(), shot).GetAwaiter().GetResult();
		Equal(2 + aimPerDislodge, AimOf(fool), "kept half of 5 plus the deferred dislodge aim");
	}

	/// <summary>
	/// 回马枪：记下每支箭命中时对扎穿目标造成的总伤害（含被格挡，只算持有者的箭矢伤害），松脱时对同一敌人造成等量伤害；
	/// 按箭区分多支箭与多名敌人；同一记录只结算一次；目标已死、队友的箭不结算。
	/// 夹具的伤害命令只在全额格挡时能跑完（未格挡的伤害要创建伤害数字节点），所以用格挡的减少量观察伤害值。
	/// </summary>
	[HextechTest]
	private static void FoolPartingShotRepeatsImpactDamageOnDislodge()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 3);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature first = combat.AddEnemy();
		Creature second = combat.AddEnemy();
		PartingShotRune rune = CreateArrowRune<PartingShotRune>(fool);
		rune.BeforeCombatStart().GetAwaiter().GetResult();
		SetArrowTestBlock(first, 100);
		SetArrowTestBlock(second, 100);

		// 按原版 ShootArrowAtTarget 的顺序：AfterArrowShot → 攻击结算（AfterDamageGiven）→ AddArrow → AfterArrowImpaled。
		(ImpaledPower Power, ArrowSlot Arrow) Shoot(Player shooter, Creature target, int unblocked, int blocked)
		{
			ArrowSlot next = shooter.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
			rune.AfterArrowShot(shooter, next, ArrowTestContext(), null).GetAwaiter().GetResult();
			DamageResult result = new(target, ArrowAttackProps) { UnblockedDamage = unblocked, BlockedDamage = blocked };
			rune.AfterDamageGiven(ArrowTestContext(), shooter.Creature, result, ArrowAttackProps, target, null).GetAwaiter().GetResult();
			DamageResult nonArrow = new(target, ValueProp.Move) { UnblockedDamage = 50 };
			rune.AfterDamageGiven(ArrowTestContext(), shooter.Creature, nonArrow, ValueProp.Move, target, null).GetAwaiter().GetResult();
			(ImpaledPower power, ArrowSlot arrow) = combat.Impale(target, shooter);
			Expect(arrow == next, "the fixture impales the arrow that was shot");
			rune.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
			return (power, arrow);
		}

		(ImpaledPower onFirst, ArrowSlot firstArrow) = Shoot(fool, first, 6, 3);
		(ImpaledPower onSecond, ArrowSlot secondArrow) = Shoot(fool, second, 4, 0);
		rune.AfterArrowDislodged(onFirst, secondArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(100, first.Block, "an arrow's record only hits the enemy it impaled");
		DislodgeFirstArrowWithHook(onFirst, rune);
		Equal(100 - 9, first.Block, "dislodging repeats the 9 total damage on the same enemy");
		Equal(100, second.Block, "the other arrow's enemy is untouched");
		rune.AfterArrowDislodged(onFirst, firstArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(100 - 9, first.Block, "a record resolves only once");

		(ImpaledPower again, _) = Shoot(fool, first, 0, 0);
		DislodgeFirstArrowWithHook(again, rune);
		Equal(100 - 9, first.Block, "a shot that dealt no damage repeats nothing");

		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(second, 0);
		DislodgeFirstArrowWithHook(onSecond, rune);
		Equal(100, second.Block, "a dead target is not damaged");

		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(second, 100);
		(ImpaledPower mate, _) = Shoot(teammate, second, 7, 0);
		DislodgeFirstArrowWithHook(mate, rune);
		Equal(100, second.Block, "a teammate's arrow is not yours");
	}

	/// <summary>回马枪 + 箭无虚发 + 穿云箭：同一次松脱各结算一次；回马枪的伤害不是箭矢伤害，不被记成下一支箭的命中伤害。</summary>
	[HextechTest]
	private static void FoolPartingShotStacksWithArrowGroupOnce()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Creature enemy = combat.AddEnemy();
		SetArrowTestBlock(enemy, 100);
		PartingShotRune partingShot = CreateArrowRune<PartingShotRune>(fool);
		SureShotRune sureShot = CreateArrowRune<SureShotRune>(fool);
		CloudPiercerRune cloudPiercer = CreateArrowRune<CloudPiercerRune>(fool);
		partingShot.BeforeCombatStart().GetAwaiter().GetResult();
		sureShot.BeforeCombatStart().GetAwaiter().GetResult();

		ArrowSlot next = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		partingShot.AfterArrowShot(fool, next, ArrowTestContext(), null).GetAwaiter().GetResult();
		partingShot.AfterDamageGiven(ArrowTestContext(), fool.Creature, new DamageResult(enemy, ArrowAttackProps) { UnblockedDamage = 8 }, ArrowAttackProps, enemy, null).GetAwaiter().GetResult();
		(ImpaledPower power, ArrowSlot arrow) = combat.Impale(enemy, fool);
		partingShot.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		cloudPiercer.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();

		// 下一支箭射出途中发生松脱：回马枪的追加伤害走 Unpowered，不计入那支箭的命中伤害。
		ArrowSlot following = fool.PlayerCombatState.Arrows.Last(static slot => slot.IsAvailable);
		following.Status = ArrowStatus.Away;
		partingShot.AfterArrowShot(fool, following, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, partingShot, sureShot, cloudPiercer);
		partingShot.AfterDamageGiven(ArrowTestContext(), fool.Creature, new DamageResult(enemy, ValueProp.Unpowered) { BlockedDamage = 8 }, ValueProp.Unpowered, enemy, null).GetAwaiter().GetResult();
		Equal(100 - 8, enemy.Block, "parting shot resolves once");
		Equal(sureShot.DynamicVars["AimPower"].IntValue, AimOf(fool), "sure shot resolves once");

		// 那支箭随后扎进同一敌人（复用持有者已有的扎穿能力），再松脱时没有可重复的命中伤害。
		power.AddArrow(following);
		partingShot.AfterArrowImpaled(power, following, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, partingShot);
		Equal(100 - 8, enemy.Block, "the parting-shot damage was not recorded as the next arrow's impact");
	}
}
#endif
