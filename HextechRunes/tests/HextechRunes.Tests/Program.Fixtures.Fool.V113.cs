#if STS2_113_OR_NEWER
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;

namespace HextechRunes.Tests;

// 愚者海克斯测试的共享夹具（只在 0.113.0 编译）。后续各组需要更多夹具时追加到本文件，不要在各自的测试文件里另写一份。
internal static partial class Program
{
	/// <summary>
	/// 愚者玩家壳：存活的玩家侧生物 + 战斗状态（只有箭矢槽，不跑会订阅 CombatManager 的构造函数），
	/// 箭矢槽数取原版愚者的 BaseArrowSlotCount（1），与原版 PlayerCombatState 构造时一致。没有牌堆、RunState 与遗物列表。
	/// </summary>
	private static Player CreateFoolCombatTestPlayer(ulong netId = 1)
	{
		Player player = CreateCharacterTestPlayer(typeof(Fool), netId);
		AttachArrowOnlyCombatState(player, 1);
		return player;
	}

	/// <summary>给玩家壳挂上只含 <paramref name="arrowSlots"/> 个已就位箭矢槽的战斗状态与存活生物。</summary>
	private static void AttachArrowOnlyCombatState(Player player, int arrowSlots)
	{
		Creature creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(creature, nameof(Creature.Side), CombatSide.Player);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, 50);
		SetAutoProperty(player, nameof(Player.Creature), creature);

		PlayerCombatState state = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
		AccessTools.Field(typeof(PlayerCombatState), "_arrows").SetValue(state, Enumerable.Range(0, arrowSlots).Select(_ => new ArrowSlot(player)).ToList());
		AccessTools.Property(typeof(Player), nameof(Player.PlayerCombatState)).SetValue(player, state);
	}
	/// <summary>
	/// 两名愚者（NetId 1、2）同在一个真实 <see cref="CombatState"/>（第 1 轮、玩家阵营）的夹具：各自有存活生物（带能力列表）、
	/// 空牌堆、回合号 1、空遗物列表，和带种子的 RunState（稳定随机可用）。战斗历史用测试进程里的 CombatManager.Instance.History。
	/// </summary>
	private static (CombatState Combat, Player Fool, Player Teammate) CreateFoolMelodyCombatFixture()
	{
		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		Player fool = CreateCharacterTestPlayer(typeof(Fool), 1);
		Player teammate = CreateCharacterTestPlayer(typeof(Fool), 2);
		AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { fool, teammate });
		FieldInfo history = AccessTools.Field(typeof(RunState), "_mapPointHistory");
		history.SetValue(run, Activator.CreateInstance(history.FieldType));
		AccessTools.Property(typeof(RunState), "Rng").SetValue(run, new RunRngSet("FOOL-MELODY"));
		CombatState combat = new(runState: run);
		foreach (Player player in new[] { fool, teammate })
		{
			AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
			AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
			Creature creature = CreatePrismaticTestCreature(CombatSide.Player, combat);
			SetAutoProperty(creature, nameof(Creature.Player), player);
			AccessTools.Field(typeof(Creature), "_powers").SetValue(creature, new List<PowerModel>());
			SetAutoProperty(player, nameof(Player.Creature), creature);
			PlayerCombatState state = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
			SetAutoProperty(state, nameof(PlayerCombatState.Hand), new CardPile(PileType.Hand));
			SetAutoProperty(state, nameof(PlayerCombatState.DrawPile), new CardPile(PileType.Draw));
			SetAutoProperty(state, nameof(PlayerCombatState.DiscardPile), new CardPile(PileType.Discard));
			SetAutoProperty(state, nameof(PlayerCombatState.ExhaustPile), new CardPile(PileType.Exhaust));
			SetAutoProperty(state, nameof(PlayerCombatState.PlayPile), new CardPile(PileType.Play));
			SetAutoProperty(state, nameof(PlayerCombatState.TurnNumber), 1);
			SetAutoProperty(player, nameof(Player.Deck), new CardPile(PileType.Deck));
			AccessTools.Property(typeof(Player), nameof(Player.PlayerCombatState)).SetValue(player, state);
		}

		return (combat, fool, teammate);
	}

	/// <summary>创建可变海克斯实例，设持有者并放进该玩家的遗物列表（原版旋律判定补丁经遗物列表查询）。</summary>
	private static T AttachFoolTestRune<T>(Player owner)
		where T : RelicModel, new()
	{
		T rune = CreateMutableTestModel<T>();
		rune.Owner = owner;
		((List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(owner)!).Add(rune);
		return rune;
	}

	/// <summary>推进到下一个完整轮次：轮次加一、回到玩家阵营，指定玩家的回合号加一（与原版 SwitchSides 一致）。</summary>
	private static void AdvanceFoolTestRound(CombatState combat, params Player[] players)
	{
		combat.RoundNumber++;
		combat.CurrentSide = CombatSide.Player;
		foreach (Player player in players)
		{
			SetAutoProperty(player.PlayerCombatState!, nameof(PlayerCombatState.TurnNumber), player.PlayerCombatState!.TurnNumber + 1);
		}
	}

	/// <summary>在当前轮次与阵营写入一次已开始并结算完的出牌（与原版 CardModel 出牌流程写历史的两步一致）。</summary>
	private static CardPlay RecordFoolTestCardPlay(CombatState combat, CardModel card)
	{
		CombatHistory history = CombatManager.Instance.History;
		List<CombatHistoryEntry> entries = (List<CombatHistoryEntry>)AccessTools.Field(typeof(CombatHistory), "_entries").GetValue(history)!;
		CardPlay play = CreateCardPlay(card, isAutoPlay: false);
		entries.Add(new CardPlayStartedEntry(play, combat.RoundNumber, combat.CurrentSide, history, []));
		entries.Add(new CardPlayFinishedEntry(play, combat.RoundNumber, combat.CurrentSide, history, []));
		return play;
	}

	private static T CreateFoolTestCard<T>(Player owner)
		where T : CardModel, new()
	{
		T card = CreateMutableTestModel<T>();
		card.Owner = owner;
		return card;
	}

	/// <summary>给玩家的生物加一层数为 <paramref name="amount"/> 的原版能力（不派发钩子）；能力模型要先经 InjectMissingModels 登记。</summary>
	private static T AddFoolTestPower<T>(Player owner, int amount)
		where T : PowerModel
	{
		T power = (T)ModelDb.Power<T>().ToMutable();
		power.ApplyInternal(owner.Creature, amount, silent: true);
		return power;
	}

	/// <summary>
	/// 测试期间装上"直接拥有旋律"补丁，并把获得能量、抽牌、新建能力三条原版命令换成只记录调用的前缀（跳过原方法：
	/// 它们需要真实战斗流程与 Godot 节点；测试里的生物身上没有拉伸，Apply&lt;StretchPower&gt; 总是走新建能力这一步）。释放时卸下补丁、清空战斗历史与记录。
	/// </summary>
	private sealed class FoolMelodyTestScope : IDisposable
	{
		internal static readonly List<(Player Player, decimal Amount)> EnergyGains = [];
		internal static readonly List<(Player Player, decimal Count)> Draws = [];
		internal static readonly List<(Creature Target, decimal Amount, Creature? Applier)> StretchApplications = [];

		private readonly Harmony _harmony = new("Natsuki.HextechRunes.Tests.FoolMelody");
		private IDisposable? _injectedModels;

		public FoolMelodyTestScope()
		{
			try
			{
				ApplyNestedPatches(_harmony, typeof(HextechGrantedMelodies));
				_harmony.Patch(
					AccessTools.Method(typeof(PlayerCmd), nameof(PlayerCmd.GainEnergy), [typeof(decimal), typeof(Player)]),
					prefix: new HarmonyMethod(typeof(FoolMelodyTestScope), nameof(CaptureEnergy)));
				_harmony.Patch(
					AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw), [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]),
					prefix: new HarmonyMethod(typeof(FoolMelodyTestScope), nameof(CaptureDraw)));
				// 泛型 Apply<T> 补不上（引用类型实例化共用代码），改截它新建能力时调用的非泛型 Apply；Apply<T> 需要能力模型已登记。
				_injectedModels = InjectMissingModels(typeof(StretchPower));
				MethodInfo apply = AccessTools.Method(
					typeof(MegaCrit.Sts2.Core.Commands.PowerCmd),
					nameof(MegaCrit.Sts2.Core.Commands.PowerCmd.Apply),
					[typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)]);
				_harmony.Patch(apply, prefix: new HarmonyMethod(typeof(FoolMelodyTestScope), nameof(CaptureStretch)));
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			_harmony.UnpatchAll(_harmony.Id);
			_injectedModels?.Dispose();
			CombatManager.Instance.History.Clear();
			EnergyGains.Clear();
			Draws.Clear();
			StretchApplications.Clear();
		}

		private static bool CaptureEnergy(decimal amount, Player player, ref Task __result)
		{
			EnergyGains.Add((player, amount));
			__result = Task.CompletedTask;
			return false;
		}

		private static bool CaptureDraw(decimal count, Player player, ref Task<IEnumerable<CardModel>> __result)
		{
			Draws.Add((player, count));
			__result = Task.FromResult(Enumerable.Empty<CardModel>());
			return false;
		}

		private static bool CaptureStretch(PowerModel power, Creature target, decimal amount, Creature? applier, ref Task __result)
		{
			if (power is StretchPower)
			{
				StretchApplications.Add((target, amount, applier));
			}

			__result = Task.CompletedTask;
			return false;
		}
	}

	/// <summary>
	/// 同一局里的 <paramref name="count"/> 名玩家壳（NetId 从 1 起，角色默认愚者）：共用一个带随机种子与玩家列表的 RunState
	/// （稳定随机与 PlayerKey 走真实路径）和一个空 CombatState；每人有存活生物、五个战斗牌堆与卡组。
	/// 不跑 PlayerCombatState 构造函数，也不开始战斗（CombatManager 不在进行中，原版生成/加入牌堆命令会提前返回）。
	/// </summary>
	private static List<Player> CreateFoolPileTestPlayers(int count, string seed = "FOOL-PILES", params Type[] characterTypes)
	{
		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		List<Player> players = Enumerable.Range(0, count)
			.Select(i => CreateCharacterTestPlayer(i < characterTypes.Length ? characterTypes[i] : typeof(Fool), (ulong)(i + 1)))
			.ToList();
		AccessTools.Field(typeof(RunState), "_players").SetValue(run, players);
		FieldInfo history = AccessTools.Field(typeof(RunState), "_mapPointHistory");
		history.SetValue(run, Activator.CreateInstance(history.FieldType));
		AccessTools.Property(typeof(RunState), "Rng").SetValue(run, new RunRngSet(seed));
		CombatState combat = new(runState: run);
		foreach (Player player in players)
		{
			AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
			SetAutoProperty(player, nameof(Player.Creature), CreatePrismaticTestCreature(CombatSide.Player, combat));
			PlayerCombatState state = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
			SetAutoProperty(state, nameof(PlayerCombatState.Hand), new CardPile(PileType.Hand));
			SetAutoProperty(state, nameof(PlayerCombatState.DrawPile), new CardPile(PileType.Draw));
			SetAutoProperty(state, nameof(PlayerCombatState.DiscardPile), new CardPile(PileType.Discard));
			SetAutoProperty(state, nameof(PlayerCombatState.ExhaustPile), new CardPile(PileType.Exhaust));
			SetAutoProperty(state, nameof(PlayerCombatState.PlayPile), new CardPile(PileType.Play));
			AccessTools.Field(typeof(PlayerCombatState), "_arrows").SetValue(state, new List<ArrowSlot>());
			SetAutoProperty(player, nameof(Player.Deck), new CardPile(PileType.Deck));
			AccessTools.Property(typeof(Player), nameof(Player.PlayerCombatState)).SetValue(player, state);
		}

		return players;
	}

	/// <summary>造一张归 <paramref name="owner"/> 所有的可变卡牌，放进其 <paramref name="pileType"/> 牌堆末尾（不派发任何钩子）。</summary>
	private static T AddFoolTestCard<T>(Player owner, PileType pileType)
		where T : CardModel, new()
	{
		T card = CreateMutableTestModel<T>();
		card.Owner = owner;
		pileType.GetPile(owner).AddInternal(card);
		return card;
	}

	/// <summary>
	/// 箭矢战斗夹具：一个真实的 <see cref="CombatState"/>（不开始战斗，原版 Hook 分发因 ShouldSkipCombatEffect 全部跳过），
	/// 里面有若干玩家（愚者或指定角色，各带只含箭矢槽的战斗状态）与若干敌人生物（战斗编号从 1 起按加入顺序）。
	/// 生物带空能力列表，可以直接走 PowerCmd / ArrowCmd / CreatureCmd 的数值路径；钩子由测试按原版顺序手动调用。
	/// 释放时清空 CombatManager 的战斗历史。
	/// </summary>
	private sealed class FoolArrowCombat : IDisposable
	{
		// 原版命令里的 Cmd.CustomScaledWait 在非测试模式下会等 Godot 计时器（测试进程没有原生层）；测试模式下直接跳过。
		private readonly bool _wasTestMode = TestMode.IsOn;

		public FoolArrowCombat()
		{
			TestMode.IsOn = true;
		}

		public CombatState Combat { get; } = new();
		public List<Player> Players { get; } = [];
		public List<Creature> Enemies { get; } = [];

		public Player AddPlayer(Type characterType, ulong netId, int arrowSlots)
		{
			Player player = CreateCharacterTestPlayer(characterType, netId);
			// 玩家壳不跑构造函数，字段初值 NullRunState 也没写入；伤害命令按玩家取 RunState。
			AccessTools.Field(typeof(Player), "_runState").SetValue(player, NullRunState.Instance);
			SetAutoProperty(player, nameof(Player.ExtraFields), new ExtraPlayerFields());
			Creature creature = CreateArrowCombatCreature(CombatSide.Player, 50);
			SetAutoProperty(creature, nameof(Creature.Player), player);
			SetAutoProperty(player, nameof(Player.Creature), creature);
			PlayerCombatState state = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
			AccessTools.Field(typeof(PlayerCombatState), "_arrows").SetValue(state, Enumerable.Range(0, arrowSlots).Select(_ => new ArrowSlot(player)).ToList());
			AccessTools.Property(typeof(Player), nameof(Player.PlayerCombatState)).SetValue(player, state);
			Combat.AddCreature(creature);
			Players.Add(player);
			return player;
		}

		public Creature AddEnemy(int hp = 100)
		{
			Creature creature = CreateArrowCombatCreature(CombatSide.Enemy, hp);
			// 战斗的钩子监听列表对没有玩家的生物会加入它的怪物模型；给一个不跑构造的壳，所有钩子都是基类默认实现。
			MonsterModel monster = (MonsterModel)RuntimeHelpers.GetUninitializedObject(typeof(Axebot));
			AccessTools.Field(typeof(MonsterModel), "_creature").SetValue(monster, creature);
			SetAutoProperty(creature, nameof(Creature.Monster), monster);
			Combat.AddCreature(creature);
			Enemies.Add(creature);
			return creature;
		}

		/// <summary>
		/// 按原版 ArrowCmd.ImpaleTarget 的能力处理把 <paramref name="shooter"/> 的下一支可用箭扎到目标上
		/// （每名施加者一份 ImpaledPower，复用已有实例）。跳过 AfterApplied（它要读联机平台的玩家名）与攻击、钩子分发。
		/// </summary>
		public (ImpaledPower Power, ArrowSlot Arrow) Impale(Creature target, Player shooter)
		{
			ArrowSlot arrow = shooter.PlayerCombatState!.Arrows.Last(static slot => slot.IsAvailable);
			arrow.Status = ArrowStatus.Away;
			ImpaledPower? power = target.GetPowerInstances<ImpaledPower>().FirstOrDefault(p => p.Applier == shooter.Creature);
			if (power == null)
			{
				power = (ImpaledPower)ModelDb.Power<ImpaledPower>().ToMutable();
				power.Applier = shooter.Creature;
				power.ApplyInternal(target, 1m, silent: true);
			}

			power.AddArrow(arrow);
			return (power, arrow);
		}

		private Creature CreateArrowCombatCreature(CombatSide side, int hp)
		{
			Creature creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
			SetAutoProperty(creature, nameof(Creature.Side), side);
			AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, hp);
			AccessTools.Field(typeof(Creature), "_maxHp").SetValue(creature, hp);
			AccessTools.Field(typeof(Creature), "_powers").SetValue(creature, new List<PowerModel>());
			creature.CombatState = Combat;
			creature.CombatId = (uint)(Players.Count + Enemies.Count + 1);
			return creature;
		}

		public void Dispose()
		{
			CombatManager.Instance.History.Clear();
			TestMode.IsOn = _wasTestMode;
		}
	}

	/// <summary>
	/// 测试期间把出牌外的牌组编辑命令换成只改测试牌组、记录调用的前缀：移除卡组牌、加牌入堆、RunState 造牌（跳过 RunState 登记）、
	/// 标记已见（不触达存档）；开测试模式让加牌预览直接返回。释放时卸下补丁并还原。
	/// </summary>
	private sealed class FoolDeckEditTestScope : IDisposable
	{
		internal static readonly List<CardModel> Removed = [];
		internal static readonly List<CardModel> Added = [];

		private readonly Harmony _harmony = new("Natsuki.HextechRunes.Tests.FoolDeckEdit");
		private readonly bool _wasTestMode = TestMode.IsOn;
		private readonly FieldInfo _saveManagerMock = AccessTools.Field(typeof(SaveManager), "_mockInstance");
		private readonly object? _previousSaveManagerMock;

		public FoolDeckEditTestScope()
		{
			_previousSaveManagerMock = _saveManagerMock.GetValue(null);
			try
			{
				TestMode.IsOn = true;
				_harmony.Patch(
					AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.RemoveFromDeck), [typeof(CardModel), typeof(bool)]),
					prefix: new HarmonyMethod(typeof(FoolDeckEditTestScope), nameof(CaptureRemove)));
				_harmony.Patch(
					AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add), [typeof(CardModel), typeof(PileType), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool)]),
					prefix: new HarmonyMethod(typeof(FoolDeckEditTestScope), nameof(CaptureAdd)));
				_harmony.Patch(
					AccessTools.Method(typeof(RunState), nameof(RunState.CreateCard), [typeof(CardModel), typeof(Player)]),
					prefix: new HarmonyMethod(typeof(FoolDeckEditTestScope), nameof(CreateUnregisteredCard)));
				_harmony.Patch(
					AccessTools.Method(typeof(SaveManager), nameof(SaveManager.MarkCardAsSeen)),
					prefix: new HarmonyMethod(typeof(FoolDeckEditTestScope), nameof(SkipOriginal)));
				SaveManager.MockInstanceForTesting((SaveManager)RuntimeHelpers.GetUninitializedObject(typeof(SaveManager)));
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			_harmony.UnpatchAll(_harmony.Id);
			_saveManagerMock.SetValue(null, _previousSaveManagerMock);
			TestMode.IsOn = _wasTestMode;
			Removed.Clear();
			Added.Clear();
		}

		private static bool CaptureRemove(CardModel card, ref Task __result)
		{
			Removed.Add(card);
			card.Owner.Deck.RemoveInternal(card, silent: true);
			__result = Task.CompletedTask;
			return false;
		}

		private static bool CaptureAdd(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
		{
			Added.Add(card);
			newPileType.GetPile(card.Owner).AddInternal(card, silent: true);
			__result = Task.FromResult(default(CardPileAddResult));
			return false;
		}

		private static bool CreateUnregisteredCard(CardModel canonicalCard, Player owner, ref CardModel __result)
		{
			__result = canonicalCard.ToMutable();
			__result.Owner = owner;
			return false;
		}

		private static bool SkipOriginal()
		{
			return false;
		}
	}

	/// <summary>
	/// 把旋律夹具里的玩家生物按给定顺序加入战斗（夹具默认不加入，<c>CombatState.Players</c> 为空），
	/// 需要更多队友时用 <paramref name="extraNetIds"/> 追加同样结构的愚者玩家（存活生物带能力列表、空遗物列表）。
	/// 返回按加入顺序排列的全部玩家。
	/// </summary>
	private static List<Player> JoinFoolMelodyPlayersToCombat(CombatState combat, IEnumerable<Player> players, params ulong[] extraNetIds)
	{
		List<Player> joined = players.ToList();
		RunState run = (RunState)joined[0].RunState;
		foreach (ulong netId in extraNetIds)
		{
			Player extra = CreateCharacterTestPlayer(typeof(Fool), netId);
			AccessTools.Field(typeof(Player), "_runState").SetValue(extra, run);
			AccessTools.Field(typeof(Player), "_relics").SetValue(extra, new List<RelicModel>());
			Creature creature = CreatePrismaticTestCreature(CombatSide.Player, combat);
			SetAutoProperty(creature, nameof(Creature.Player), extra);
			AccessTools.Field(typeof(Creature), "_powers").SetValue(creature, new List<PowerModel>());
			SetAutoProperty(extra, nameof(Player.Creature), creature);
			((List<Player>)AccessTools.Field(typeof(RunState), "_players").GetValue(run)!).Add(extra);
			joined.Add(extra);
		}

		foreach (Player player in joined)
		{
			combat.AddCreature(player.Creature);
		}

		return joined;
	}
}
#endif
