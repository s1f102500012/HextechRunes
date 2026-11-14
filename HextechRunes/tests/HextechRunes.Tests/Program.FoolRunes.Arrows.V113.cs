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
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

// 愚者箭矢组（箭无虚发、拨弦、忠诚、穿云箭、百步穿杨、回马枪）的战斗逻辑测试。
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

	// 原版扎穿直接击杀时传进钩子的能力：没挂到任何生物上（Owner 为 null），施加者照常写入，箭照常 AddArrow。
	private static ImpaledPower CreateUnappliedImpale(Player shooter, ArrowSlot arrow)
	{
		arrow.Status = ArrowStatus.Away;
		ImpaledPower unapplied = (ImpaledPower)ModelDb.Power<ImpaledPower>().ToMutable();
		unapplied.Applier = shooter.Creature;
		unapplied.AddArrow(arrow);
		return unapplied;
	}

	/// <summary>
	/// 箭无虚发：自己的箭每松脱一次抽 {Cards} 张，击杀造成的松脱（能力没有持有者）也算；
	/// 队友的箭、非愚者持有者、已死的持有者不触发。
	/// </summary>
	[HextechTest]
	private static void FoolSureShotDrawsOnOwnDislodgeOnly()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		using FoolMelodyTestScope draws = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 3);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		SureShotRune rune = CreateArrowRune<SureShotRune>(fool);
		decimal cards = rune.DynamicVars.Cards.BaseValue;
		Equal(2m, cards, "draws two cards per dislodge");

		(ImpaledPower own, _) = combat.Impale(enemy, fool);
		combat.Impale(enemy, fool);
		DislodgeFirstArrowWithHook(own, rune);
		DislodgeFirstArrowWithHook(own, rune);
		SequenceEqual(new[] { (fool, cards), (fool, cards) }, FoolMelodyTestScope.Draws, "each own dislodge draws once");

		ArrowSlot lethal = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		DislodgeFirstArrowWithHook(CreateUnappliedImpale(fool, lethal), rune);
		Equal(3, FoolMelodyTestScope.Draws.Count, "the dislodge after a killing shot counts too");

		(ImpaledPower mate, _) = combat.Impale(enemy, teammate);
		DislodgeFirstArrowWithHook(mate, rune);
		Equal(3, FoolMelodyTestScope.Draws.Count, "a teammate's arrow does not count as yours");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		(ImpaledPower foreign, _) = combat.Impale(enemy, ironclad);
		DislodgeFirstArrowWithHook(foreign, CreateArrowRune<SureShotRune>(ironclad));
		Equal(3, FoolMelodyTestScope.Draws.Count, "non-Fool holders do not trigger");

		(ImpaledPower late, _) = combat.Impale(enemy, fool);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(fool.Creature, 0);
		DislodgeFirstArrowWithHook(late, rune);
		Equal(3, FoolMelodyTestScope.Draws.Count, "a dead holder draws nothing");
	}

	/// <summary>
	/// 拨弦：持有者每射出一支箭抽 {Cards} 张（非卡牌来源也算）；没有可用箭时原版也派发钩子（箭为 null），不抽；
	/// 队友射箭、非愚者、已死的持有者不触发。
	/// </summary>
	[HextechTest]
	private static void FoolPluckDrawsOnOwnShotOnly()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		using FoolMelodyTestScope draws = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		PluckRune rune = CreateArrowRune<PluckRune>(fool);
		decimal cards = rune.DynamicVars.Cards.BaseValue;
		Equal(1m, cards, "draws one card per shot");

		rune.AfterArrowShot(fool, fool.PlayerCombatState!.Arrows[0], ArrowTestContext(), null).GetAwaiter().GetResult();
		rune.AfterArrowShot(fool, fool.PlayerCombatState.Arrows[1], ArrowTestContext(), CreateFoolTestCard<HeavyShot>(fool)).GetAwaiter().GetResult();
		SequenceEqual(new[] { (fool, cards), (fool, cards) }, FoolMelodyTestScope.Draws, "each own shot draws once, card source or not");

		rune.AfterArrowShot(fool, null, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.Draws.Count, "trying to shoot without an available arrow is not a shot");

		rune.AfterArrowShot(teammate, teammate.PlayerCombatState!.Arrows[0], ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.Draws.Count, "a teammate's shot does not count");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		CreateArrowRune<PluckRune>(ironclad).AfterArrowShot(ironclad, ironclad.PlayerCombatState!.Arrows[0], ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.Draws.Count, "non-Fool holders do not trigger");

		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(fool.Creature, 0);
		rune.AfterArrowShot(fool, fool.PlayerCombatState.Arrows[0], ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(2, FoolMelodyTestScope.Draws.Count, "a dead holder draws nothing");
	}

	/// <summary>
	/// 忠诚：持有者自己的回合开始时（含自己的额外回合）按战斗编号逐个让自己的箭全部松脱；队友的箭不动；
	/// 回合结束不再触发；敌方回合、队友的额外回合与非愚者不触发。
	/// </summary>
	[HextechTest]
	private static void FoolLoyaltyDislodgesOwnArrowsAtOwnTurnStart()
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
		rune.BeforeSideTurnEnd(ArrowTestContext(), CombatSide.Player, [fool.Creature]).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(fool), "the end of the holder's turn no longer triggers");
		rune.AfterSideTurnStart(CombatSide.Enemy, [], combat.Combat).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(fool), "the enemy turn start does not trigger");
		rune.AfterSideTurnStart(CombatSide.Player, [teammate.Creature], combat.Combat).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(fool), "a teammate's extra turn does not trigger");

		rune.AfterSideTurnStart(CombatSide.Player, [fool.Creature, teammate.Creature], combat.Combat).GetAwaiter().GetResult();
		Equal(3, ArrowCmd.GetAvailableArrowCount(fool), "all own arrows are back");
		Expect(!ArrowCmd.CreatureIsImpaledByPlayersArrow(first, fool) && !ArrowCmd.CreatureIsImpaledByPlayersArrow(second, fool), "own impaled powers are removed");
		SequenceEqual(new[] { second, first, first }, retrievalOrder, "targets resolve by combat id");
		Equal(ArrowStatus.Away, mateArrow.Status, "the teammate's arrow stays");
		Expect(first.Powers.Contains(mate), "the teammate's impaled power stays");

		combat.Impale(second, fool);
		rune.AfterSideTurnStart(CombatSide.Player, [fool.Creature], combat.Combat).GetAwaiter().GetResult();
		Equal(3, ArrowCmd.GetAvailableArrowCount(fool), "the holder's own extra turn triggers");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		combat.Impale(second, ironclad);
		CreateArrowRune<LoyaltyRune>(ironclad).AfterSideTurnStart(CombatSide.Player, [ironclad.Creature], combat.Combat).GetAwaiter().GetResult();
		Equal(0, ArrowCmd.GetAvailableArrowCount(ironclad), "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 穿云箭：自己的箭每扎穿一次给目标 {VulnerablePower} 层易伤（施加者是持有者）；
	/// 箭直接射死目标时（能力没有持有者）不抛异常、什么都不给，原版随后的取回照常；队友的箭、非愚者不触发。
	/// </summary>
	[HextechTest]
	private static void FoolCloudPiercerAppliesVulnerableOnOwnImpale()
	{
		using IDisposable models = InjectMissingModels(typeof(AimPower), typeof(ImpaledPower), typeof(VulnerablePower));
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 3);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		Creature other = combat.AddEnemy();
		CloudPiercerRune rune = CreateArrowRune<CloudPiercerRune>(fool);
		int perImpale = rune.DynamicVars.Vulnerable.IntValue;
		Equal(2, perImpale, "two Vulnerable per impale");

		(ImpaledPower power, ArrowSlot firstArrow) = combat.Impale(enemy, fool);
		rune.AfterArrowImpaled(power, firstArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		VulnerablePower vulnerable = enemy.GetPower<VulnerablePower>() ?? throw new InvalidOperationException("impale should apply Vulnerable");
		Equal(perImpale, vulnerable.Amount, "an impale applies Vulnerable");
		Expect(vulnerable.Applier == fool.Creature, "the holder applies it");
		(_, ArrowSlot secondArrow) = combat.Impale(enemy, fool);
		rune.AfterArrowImpaled(power, secondArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(perImpale * 2, enemy.GetPower<VulnerablePower>()!.Amount, "every arrow that impales applies it again");

		ArrowSlot lethal = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		ImpaledPower unapplied = CreateUnappliedImpale(fool, lethal);
		rune.AfterArrowImpaled(unapplied, lethal, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(unapplied, rune);
		Equal(ArrowStatus.Retrieved, lethal.Status, "the arrow that killed its target still comes back");
		Equal(perImpale * 2, enemy.GetPower<VulnerablePower>()!.Amount, "a killing shot applies nothing");
		Expect(!other.HasPower<VulnerablePower>(), "nor to anyone else");

		(ImpaledPower mate, ArrowSlot mateArrow) = combat.Impale(other, teammate);
		rune.AfterArrowImpaled(mate, mateArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Expect(!other.HasPower<VulnerablePower>(), "a teammate's impale does not count");

		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 1);
		(ImpaledPower foreign, ArrowSlot foreignArrow) = combat.Impale(other, ironclad);
		CreateArrowRune<CloudPiercerRune>(ironclad).AfterArrowImpaled(foreign, foreignArrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Expect(!other.HasPower<VulnerablePower>(), "non-Fool holders do not trigger");
	}

	/// <summary>
	/// 百步穿杨：额外加伤等于原版瞄准这次贡献的加伤 × ({AimMultiplier} − 1)，所以瞄准的总贡献翻倍；
	/// 与沉重射击的瞄准倍率相乘（2 倍 → 4 倍）；原版不加伤的场合（非箭矢、无力伤害、别人的伤害、已登记其他卡牌的攻击、没有瞄准）也不加；
	/// 非愚者不触发；倍率改为 3 时额外加两份。
	/// </summary>
	[HextechTest]
	private static void FoolDeadeyeMultipliesAimDamageIncludingHeavyShot()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 1);
		Player teammate = combat.AddPlayer(typeof(Fool), 2, 1);
		Creature enemy = combat.AddEnemy();
		DeadeyeRune rune = CreateArrowRune<DeadeyeRune>(fool);
		Equal(2m, rune.DynamicVars["AimMultiplier"].BaseValue, "aim is twice as effective");

		decimal Vanilla(Player dealer, ValueProp props, CardModel? card) =>
			dealer.Creature.GetPowerInstances<AimPower>().Sum(aim => aim.ModifyDamageAdditive(enemy, 10m, props, dealer.Creature, card, null));
		decimal Extra(DeadeyeRune holder, Player dealer, ValueProp props, CardModel? card) =>
			holder.ModifyDamageAdditiveCompat(enemy, 10m, props, dealer.Creature, card);

		Equal(0m, Extra(rune, fool, ArrowAttackProps, null), "no aim, nothing to multiply");

		GiveAim(fool, 5);
		Equal(5m, Vanilla(fool, ArrowAttackProps, null), "vanilla aim adds its amount");
		Equal(5m, Extra(rune, fool, ArrowAttackProps, null), "deadeye adds the same again: 10 in total");

		HeavyShot heavyShot = CreateFoolTestCard<HeavyShot>(fool);
		Equal(10m, Vanilla(fool, ArrowAttackProps, heavyShot), "heavy shot doubles aim in vanilla");
		Equal(10m, Extra(rune, fool, ArrowAttackProps, heavyShot), "deadeye doubles the heavy-shot aim again: 20 in total, 4 times the aim");

		Equal(0m, Extra(rune, fool, ValueProp.Move, null), "non-arrow damage");
		Equal(0m, Extra(rune, fool, ValueProp.Unpowered | ValueProp.FromArrow, null), "unpowered arrow damage");
		Equal(0m, rune.ModifyDamageAdditiveCompat(enemy, 10m, ArrowAttackProps, enemy, null), "someone else's damage");

		// 原版登记了别的卡牌的箭矢攻击后，其他卡牌的伤害不吃瞄准，百步穿杨也不加。
		AttackCommand registered = CreateArrowTestAttack(fool.Creature);
		SetAutoProperty(registered, nameof(AttackCommand.ModelSource), CreateFoolTestCard<HeavyShot>(fool));
		fool.Creature.GetPower<AimPower>()!.BeforeAttack(registered).GetAwaiter().GetResult();
		Equal(0m, Vanilla(fool, ArrowAttackProps, heavyShot), "vanilla gives other cards nothing during a registered attack");
		Equal(0m, Extra(rune, fool, ArrowAttackProps, heavyShot), "neither does deadeye");
		Equal(10m, Extra(rune, fool, ArrowAttackProps, (CardModel)registered.ModelSource!), "the registered card still gets doubled");

		GiveAim(teammate, 5);
		Equal(0m, Extra(rune, teammate, ArrowAttackProps, null), "a teammate's aim is not yours");
		Player ironclad = combat.AddPlayer(typeof(Ironclad), 3, 0);
		GiveAim(ironclad, 5);
		Equal(0m, Extra(CreateArrowRune<DeadeyeRune>(ironclad), ironclad, ArrowAttackProps, null), "non-Fool holders do not trigger");

		rune.DynamicVars["AimMultiplier"].BaseValue = 3m;
		Equal(20m, Extra(rune, fool, ArrowAttackProps, (CardModel)registered.ModelSource!), "a multiplier of 3 adds two more shares");
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

	/// <summary>
	/// 回马枪：箭命中直接击杀时，原版 ImpaleTarget 对已死目标施加扎穿不生效（CanReceivePowers 为 false），
	/// 传进钩子的 ImpaledPower 没有持有者；钩子不能抛异常，否则原版紧接着的"目标已死就松脱取回"不会执行，箭留在尸体上。
	/// </summary>
	[HextechTest]
	private static void FoolPartingShotSurvivesShotThatKillsTarget()
	{
		using IDisposable models = InjectArrowModels();
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Creature victim = combat.AddEnemy();
		Creature survivor = combat.AddEnemy();
		SetArrowTestBlock(survivor, 100);
		PartingShotRune rune = CreateArrowRune<PartingShotRune>(fool);
		rune.BeforeCombatStart().GetAwaiter().GetResult();

		ArrowSlot lethal = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		rune.AfterArrowShot(fool, lethal, ArrowTestContext(), null).GetAwaiter().GetResult();
		DamageResult killingBlow = new(victim, ArrowAttackProps) { UnblockedDamage = 100 };
		rune.AfterDamageGiven(ArrowTestContext(), fool.Creature, killingBlow, ArrowAttackProps, victim, null).GetAwaiter().GetResult();
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(victim, 0);

		// 原版此时拿到的是没挂到任何生物上的能力实例：Owner 为 null，箭照常 AddArrow。
		lethal.Status = ArrowStatus.Away;
		ImpaledPower unapplied = (ImpaledPower)ModelDb.Power<ImpaledPower>().ToMutable();
		unapplied.Applier = fool.Creature;
		unapplied.AddArrow(lethal);
		rune.AfterArrowImpaled(unapplied, lethal, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(unapplied, rune);
		Equal(ArrowStatus.Retrieved, lethal.Status, "the arrow that killed its target comes back");

		// 击杀那一箭的记录不残留：下一支箭照常记录并在松脱时结算。
		ArrowSlot next = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		rune.AfterArrowShot(fool, next, ArrowTestContext(), null).GetAwaiter().GetResult();
		DamageResult hit = new(survivor, ArrowAttackProps) { BlockedDamage = 7 };
		rune.AfterDamageGiven(ArrowTestContext(), fool.Creature, hit, ArrowAttackProps, survivor, null).GetAwaiter().GetResult();
		(ImpaledPower power, ArrowSlot arrow) = combat.Impale(survivor, fool);
		Expect(arrow == next, "the fixture impales the arrow that was shot");
		rune.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, rune);
		Equal(100 - 7, survivor.Block, "the next arrow still repeats its own impact damage");
	}

	/// <summary>
	/// 回马枪 + 箭无虚发 + 穿云箭：扎穿时穿云箭给一次易伤；同一次松脱回马枪与箭无虚发各结算一次；
	/// 回马枪的伤害不是箭矢伤害，不被记成下一支箭的命中伤害。
	/// </summary>
	[HextechTest]
	private static void FoolPartingShotStacksWithArrowGroupOnce()
	{
		using IDisposable models = InjectMissingModels(typeof(AimPower), typeof(ImpaledPower), typeof(VulnerablePower));
		using FoolArrowCombat combat = new();
		Player fool = combat.AddPlayer(typeof(Fool), 1, 2);
		Creature enemy = combat.AddEnemy();
		SetArrowTestBlock(enemy, 100);
		PartingShotRune partingShot = CreateArrowRune<PartingShotRune>(fool);
		SureShotRune sureShot = CreateArrowRune<SureShotRune>(fool);
		CloudPiercerRune cloudPiercer = CreateArrowRune<CloudPiercerRune>(fool);
		partingShot.BeforeCombatStart().GetAwaiter().GetResult();

		ArrowSlot next = fool.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
		partingShot.AfterArrowShot(fool, next, ArrowTestContext(), null).GetAwaiter().GetResult();
		partingShot.AfterDamageGiven(ArrowTestContext(), fool.Creature, new DamageResult(enemy, ArrowAttackProps) { UnblockedDamage = 8 }, ArrowAttackProps, enemy, null).GetAwaiter().GetResult();
		(ImpaledPower power, ArrowSlot arrow) = combat.Impale(enemy, fool);
		partingShot.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		cloudPiercer.AfterArrowImpaled(power, arrow, ArrowTestContext(), null).GetAwaiter().GetResult();
		Equal(cloudPiercer.DynamicVars.Vulnerable.IntValue, enemy.GetPower<VulnerablePower>()?.Amount ?? 0, "cloud piercer resolves once on impale");

		// 下一支箭射出途中发生松脱：回马枪的追加伤害走 Unpowered，不计入那支箭的命中伤害。
		// 抽牌记录用旋律夹具的截获前缀（它也会截掉新建能力，所以只包住松脱这一段）。
		ArrowSlot following = fool.PlayerCombatState.Arrows.Last(static slot => slot.IsAvailable);
		following.Status = ArrowStatus.Away;
		partingShot.AfterArrowShot(fool, following, ArrowTestContext(), null).GetAwaiter().GetResult();
		using (new FoolMelodyTestScope())
		{
			DislodgeFirstArrowWithHook(power, partingShot, sureShot, cloudPiercer);
			SequenceEqual(new[] { (fool, sureShot.DynamicVars.Cards.BaseValue) }, FoolMelodyTestScope.Draws, "sure shot resolves once");
		}

		partingShot.AfterDamageGiven(ArrowTestContext(), fool.Creature, new DamageResult(enemy, ValueProp.Unpowered) { BlockedDamage = 8 }, ValueProp.Unpowered, enemy, null).GetAwaiter().GetResult();
		Equal(100 - 8, enemy.Block, "parting shot resolves once");
		Equal(cloudPiercer.DynamicVars.Vulnerable.IntValue, enemy.GetPower<VulnerablePower>()!.Amount, "dislodging does not apply Vulnerable");

		// 那支箭随后扎进同一敌人（复用持有者已有的扎穿能力），再松脱时没有可重复的命中伤害。
		power.AddArrow(following);
		partingShot.AfterArrowImpaled(power, following, ArrowTestContext(), null).GetAwaiter().GetResult();
		DislodgeFirstArrowWithHook(power, partingShot);
		Equal(100 - 8, enemy.Block, "the parting-shot damage was not recorded as the next arrow's impact");
	}
}
#endif
