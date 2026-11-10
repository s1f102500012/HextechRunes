using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.TestSupport;

namespace HextechRunes;

public sealed class EarthAwakensRune : DrawThresholdRuneBase
{
	// 同时出发的滚石之间错开的时间（秒），最多错开 MaxStaggeredBoulders 块，免得一堆滚石完全重叠。
	private const float BoulderStaggerSeconds = 0.08f;
	private const int MaxStaggeredBoulders = 8;

	private static readonly MethodInfo? PowerFlashMethod = AccessTools.Method(typeof(PowerModel), "Flash", Type.EmptyTypes);

	// 记录每块滚石最近一次被合并滚动时所在的回合开始分发（原版对所有监听者传同一个 choiceContext）。
	private static readonly ConditionalWeakTable<RollingBoulderPower, PlayerChoiceContext> RolledInDispatch = new();

	// 旧版本存档兼容占位：原为"本场已施加首层"标记，已不再使用；名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedInitialPowerAppliedThisCombat
	{
		get => false;
		set { }
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedCardsDrawnThisCombat
	{
		get => SavedDrawProgress;
		set => SavedDrawProgress = value;
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("CardsNeeded", 8m),
		new PowerVar<RollingBoulderPower>(5m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<RollingBoulderPower>()
	];

	// 原版滚石是 Instanced 能力：每次施加都新增一块初始层数为 5 的滚石，各自成长。
	protected override async Task ApplyDrawThresholdReward()
	{
		if (Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<RollingBoulderPower>(Owner.Creature, DynamicVars["RollingBoulderPower"].BaseValue, Owner.Creature, null);
	}

	/// <summary>
	/// 一起滚：所有滚石同时出发（彼此错开一点），伤害仍按每块滚石的层数分别结算。
	/// 每个敌人第一次被任意一块滚石碰到时，按滚石顺序依次结算全部滚石对它的伤害：
	/// 结算顺序只取决于滚石顺序和敌人站位，不受各端动画帧时间影响。滚完后每块各自按原版成长。
	/// </summary>
	private static async Task RollTogether(IReadOnlyList<RollingBoulderPower> boulders, PlayerChoiceContext choiceContext)
	{
		foreach (RollingBoulderPower boulder in boulders)
		{
			PowerFlashMethod?.Invoke(boulder, null);
		}

		HextechCombatState combatState = boulders[0].CombatState;
		if (TestMode.IsOn)
		{
			foreach (RollingBoulderPower boulder in boulders)
			{
				await DamageWith(boulder, choiceContext, combatState.HittableEnemies);
			}
		}
		else
		{
			IReadOnlyList<Creature> enemies = combatState.HittableEnemies;
			List<Task> damageTasks = [];
			HashSet<Creature> hitCreatures = [];
			void OnHit(NCreature creature)
			{
				if (hitCreatures.Add(creature.Entity))
				{
					damageTasks.Add(DamageAllBoulders(boulders, choiceContext, creature.Entity));
				}
			}

			Task[] rolls = boulders
				.Select((boulder, index) => RollOne(enemies, boulder.Amount, Math.Min(index, MaxStaggeredBoulders) * BoulderStaggerSeconds, OnHit))
				.ToArray();
			await Task.WhenAll(rolls);
			await Task.WhenAll(damageTasks);
		}

		foreach (RollingBoulderPower boulder in boulders)
		{
			boulder.SetAmount(boulder.Amount + boulder.DynamicVars.Damage.IntValue);
		}
	}

	private static async Task RollOne(IReadOnlyList<Creature> enemies, decimal damage, float delay, Action<NCreature> onHit)
	{
		if (delay > 0f)
		{
			await Cmd.Wait(delay);
		}

		NRollingBoulderVfx? vfx = NRollingBoulderVfx.Create(enemies, damage);
		if (vfx == null)
		{
			return;
		}

		vfx.Connect(NRollingBoulderVfx.SignalName.HitCreature, Callable.From(onHit));
		SignalAwaiter finished = vfx.ToSignal(vfx, NRollingBoulderVfx.SignalName.Finished);
		NCombatRoom.Instance?.CombatVfxContainer.CallDeferred(Node.MethodName.AddChild, vfx);
		await finished;
	}

	private static async Task DamageAllBoulders(IReadOnlyList<RollingBoulderPower> boulders, PlayerChoiceContext choiceContext, Creature target)
	{
		foreach (RollingBoulderPower boulder in boulders)
		{
			await DamageWith(boulder, choiceContext, [ target ]);
		}
	}

	private static Task DamageWith(RollingBoulderPower boulder, PlayerChoiceContext choiceContext, IEnumerable<Creature> targets)
	{
		return CreatureCmd.Damage(choiceContext, targets, boulder.Amount, ValueProp.Unpowered, boulder.Owner);
	}

	// 跳过理由：原版 RollingBoulderPower.AfterPlayerTurnStart（0.107.1/0.111.0 反编译一致）每块滚石各自播放
	// 滚石动画并等它结束，原版 Hook 逐个等待监听者，几块滚石就要依次滚几遍；没有能合并多块 Instanced 能力的 Hook。
	// 激活条件：持有本符文且身上有两块以上滚石时，第一块被调用时一起滚全部滚石，同一次分发里其余滚石直接跳过；
	// 只有一块或没有本符文时走原版。替换体保留原版的闪光、伤害（Unpowered、来源为持有者）与滚后 +Damage 成长。
	// 原方法 IL 由 vanilla_copy_guard 冻结。
	[HarmonyPatch(typeof(RollingBoulderPower), nameof(RollingBoulderPower.AfterPlayerTurnStart))]
	[HextechPatch("rune.earth-awakens.roll-together", "大地苏醒", Rune = typeof(EarthAwakensRune))]
	private static class RollTogetherPatch
	{
		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(RollingBoulderPower __instance, PlayerChoiceContext choiceContext, Player player, ref Task __result)
		{
			Creature owner = __instance.Owner;
			if (player != owner.Player || owner.Player?.GetRelic<EarthAwakensRune>() == null)
			{
				return true;
			}

			if (RolledInDispatch.TryGetValue(__instance, out PlayerChoiceContext? rolledIn) && ReferenceEquals(rolledIn, choiceContext))
			{
				__result = Task.CompletedTask;
				return false;
			}

			List<RollingBoulderPower> boulders = owner.Powers.OfType<RollingBoulderPower>().ToList();
			if (boulders.Count < 2)
			{
				return true;
			}

			foreach (RollingBoulderPower boulder in boulders)
			{
				RolledInDispatch.AddOrUpdate(boulder, choiceContext);
			}

			__result = RollTogether(boulders, choiceContext);
			return false;
		}
	}
}
