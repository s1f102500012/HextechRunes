using System.Runtime.CompilerServices;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private sealed class ImmediateGoldListener : HextechRelicBase
	{
		internal Func<Task>? OnGold;
		internal int Calls;
		public override Task AfterGoldGained(Player player)
		{
			if (player != Owner)
			{
				return Task.CompletedTask;
			}
			Calls++;
			Expect(Calls < 20, "gold feedback must terminate");
			return OnGold?.Invoke() ?? Task.CompletedTask;
		}
	}

	private static void WithImmediateGoldFixture(Action<Player, Player, Creature, ImmediateGoldListener> action)
	{
		Harmony harmony = new("HextechRunes.Tests.ImmediateGold");
		ulong? localId = LocalContext.NetId;
		try
		{
			// 只替换完整爬塔监听者枚举与 UI；保留原版 GainGold 和金币 Hook 的真实调用。
			LocalContext.NetId = null;
			harmony.Patch(AccessTools.Method(typeof(RunState), nameof(RunState.IterateHookListeners)),
				prefix: new HarmonyMethod(typeof(Program), nameof(CrossOrbTestListeners)));
			harmony.Patch(AccessTools.Method(typeof(CombatState), nameof(CombatState.IterateHookListeners)),
				prefix: new HarmonyMethod(typeof(Program), nameof(ImmediateGoldCombatListeners)));
			harmony.Patch(AccessTools.Method(typeof(RelicModel), nameof(RelicModel.Flash)),
				prefix: new HarmonyMethod(typeof(Program), nameof(SkipImmediateGoldFlash)));
			harmony.Patch(AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.AddExtraReward)),
				prefix: new HarmonyMethod(typeof(Program), nameof(CaptureUpgradeGoldReward)));
			(HextechEnemyHexContext _, Player first, Player second) = CreatePrismaticEnemyFixture();
			foreach (Player player in new[] { first, second })
			{
				AccessTools.Field(typeof(Player), "_relics").SetValue(player, new List<RelicModel>());
				AccessTools.Field(typeof(Creature), "_powers").SetValue(player.Creature, new List<PowerModel>());
				SetAutoProperty(player.Creature, nameof(Creature.Player), player);
			}
			CombatState combat = (CombatState)first.Creature.CombatState!;
			Creature enemy = CreatePrismaticTestCreature(CombatSide.Enemy, combat);
			((List<Creature>)AccessTools.Field(typeof(CombatState), "_enemies").GetValue(combat)!).Add(enemy);
			ImmediateGoldListener listener = CreateMutableTestModel<ImmediateGoldListener>();
			listener.Owner = first;
			((List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(first)!).Add(listener);
			action(first, second, enemy, listener);
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
			LocalContext.NetId = localId;
			UpgradeGoldRewards.Clear();
		}
	}

	private static bool ImmediateGoldCombatListeners(CombatState __instance, ref IEnumerable<AbstractModel> __result)
	{
		__result = __instance.RunState.Players.SelectMany(p => p.Relics);
		return false;
	}

	private static bool SkipImmediateGoldFlash() => false;

	[HextechTest]
	private static void ImmediateGoldPaysOwnerAndDoesNotRepeatAtVictory()
	{
		WithImmediateGoldFixture((owner, other, enemy, listener) =>
		{
			SacrificeRune sacrifice = CreateMutableTestModel<SacrificeRune>(); sacrifice.Owner = owner;
			GoldrendRune goldrend = CreateMutableTestModel<GoldrendRune>(); goldrend.Owner = owner;
			BurningInterestRune interest = CreateMutableTestModel<BurningInterestRune>(); interest.Owner = owner;
			CollectorRune collector = CreateMutableTestModel<CollectorRune>(); collector.Owner = owner;
			PiggyBankRune piggy = CreateMutableTestModel<PiggyBankRune>(); piggy.Owner = owner;
			int before = owner.Gold;
			sacrifice.AfterPlayerTurnStart(null!, other).GetAwaiter().GetResult();
			Equal(before, owner.Gold, "teammate turn grants nothing");
			sacrifice.AfterPlayerTurnStart(null!, owner).GetAwaiter().GetResult();
			Equal(before + 5, owner.Gold, "one living enemy pays immediately");
			DamageResult hit = new(enemy, ValueProp.Move) { BlockedDamage = 3 };
			goldrend.AfterDamageGiven(null!, other.Creature, hit, hit.Props, enemy, null).GetAwaiter().GetResult();
			Equal(before + 5, owner.Gold, "teammate damage grants nothing");
			goldrend.AfterDamageGiven(null!, owner.Creature, hit, hit.Props, enemy, null).GetAwaiter().GetResult();
			Equal(before + 15, owner.Gold, "owned damage pays even if blocked");
			interest.AfterDamageReceived(null!, enemy, hit, hit.Props, null, null).GetAwaiter().GetResult();
			Equal(before + 15, owner.Gold, "ordinary damage is not burn income");
			HextechBurnPower.RunWithDamageResolutionGuard(() => interest.AfterDamageReceived(null!, enemy, hit, hit.Props, null, null)).GetAwaiter().GetResult();
			Equal(before + 21, owner.Gold, "three burn damage pays six immediately");
			collector.RecordExecution(enemy, false).GetAwaiter().GetResult();
			Equal(before + 21, owner.Gold, "phase transition is not a credited death");
			collector.RecordExecution(enemy, true).GetAwaiter().GetResult();
			collector.RecordExecution(enemy, true).GetAwaiter().GetResult();
			Equal(before + 41, owner.Gold, "shared execute entry pays once per victim");
			piggy.AfterObtained().GetAwaiter().GetResult();
			piggy.AfterDamageReceived(null!, owner.Creature, hit, hit.Props, null, null).GetAwaiter().GetResult();
			Equal(before + 241, owner.Gold, "pickup pays 200 but blocked damage pays nothing");
			DamageResult wound = new(owner.Creature, ValueProp.Unpowered) { UnblockedDamage = 1 };
			piggy.AfterDamageReceived(null!, other.Creature, wound, wound.Props, null, null).GetAwaiter().GetResult();
			piggy.AfterDamageReceived(null!, owner.Creature, wound, wound.Props, null, null).GetAwaiter().GetResult();
			Equal(before + 261, owner.Gold, "own unblocked damage pays twenty immediately");
			Equal(6, listener.Calls, "native gold hooks run for each payout");
			CombatRoom room = (CombatRoom)RuntimeHelpers.GetUninitializedObject(typeof(CombatRoom));
			foreach (RelicModel rune in new RelicModel[] { sacrifice, goldrend, interest, collector })
			{
				rune.AfterCombatEnd(room).GetAwaiter().GetResult();
			}
			Equal(0, UpgradeGoldRewards.Count, "no duplicate battle-end reward");
			Equal(0, other.Gold, "teammate gold is unchanged");
		});
	}

	[HextechTest]
	private static void ImmediateGoldFeedbackStopsAndLaterDamageStillPays()
	{
		WithImmediateGoldFixture((owner, _, enemy, listener) =>
		{
			PiggyBankRune piggy = CreateMutableTestModel<PiggyBankRune>(); piggy.Owner = owner;
			GoldrendRune goldrend = CreateMutableTestModel<GoldrendRune>(); goldrend.Owner = owner;
			BurningInterestRune interest = CreateMutableTestModel<BurningInterestRune>(); interest.Owner = owner;
			DamageResult wound = new(owner.Creature, ValueProp.Unpowered) { UnblockedDamage = 1 };
			DamageResult hit = new(enemy, ValueProp.Unpowered) { UnblockedDamage = 3 };
			Func<Task> hurt = () => piggy.AfterDamageReceived(null!, owner.Creature, wound, wound.Props, null, null);
			Func<Task> strike = () => goldrend.AfterDamageGiven(null!, owner.Creature, hit, hit.Props, enemy, null);
			Func<Task> burn = () => HextechBurnPower.RunWithDamageResolutionGuard(() => interest.AfterDamageReceived(null!, enemy, hit, hit.Props, null, null));
			listener.OnGold = hurt; // 鲜血神像的金币回调再次造成未格挡伤害。
			piggy.AfterObtained().GetAwaiter().GetResult();
			Equal(200, owner.Gold, "pickup feedback must not recursively pay");
			hurt().GetAwaiter().GetResult();
			hurt().GetAwaiter().GetResult();
			Equal(240, owner.Gold, "independent hits resume after feedback guard");
			listener.OnGold = strike;
			strike().GetAwaiter().GetResult();
			Equal(250, owner.Gold, "gold-induced retaliation does not loop");
			listener.OnGold = burn;
			burn().GetAwaiter().GetResult();
			Equal(256, owner.Gold, "nested damage inside burn scope does not recursively earn interest");
			listener.OnGold = () => throw new InvalidOperationException("fixture gold callback failure");
			try { hurt().GetAwaiter().GetResult(); throw new Exception("expected callback exception"); }
			catch (InvalidOperationException) { }
			listener.OnGold = hurt;
			hurt().GetAwaiter().GetResult();
			Equal(296, owner.Gold, "guard clears even when a downstream callback throws");
		});
	}

#if STS2_113_OR_NEWER
	/// <summary>升级：我的小钱钱：只在持有者自己的我的小钱钱命中敌人时，按这一段实际扣掉的生命立即发金币。</summary>
	[HextechTest]
	private static void MyShiniesUpgradePaysUnblockedHpLossPerHit()
	{
		Expect(CreateMutableTestModel<MyShiniesUpgradeRune>().IsAvailableForPlayer(CreateUpgradeAvailabilityTestPlayer(typeof(Silent))),
			"Upgrade: My Shinies is not character-limited");
		WithImmediateGoldFixture((owner, other, enemy, listener) =>
		{
			MyShiniesUpgradeRune rune = CreateMutableTestModel<MyShiniesUpgradeRune>(); rune.Owner = owner;
			MyShinies shinies = CreateMutableTestModel<MyShinies>(); shinies.Owner = owner;
			MyShinies foreign = CreateMutableTestModel<MyShinies>(); foreign.Owner = other;
			int before = owner.Gold;
			DamageResult hit = new(enemy, ValueProp.Move) { BlockedDamage = 2, UnblockedDamage = 4, OverkillDamage = 7 };
			rune.AfterDamageGiven(null!, owner.Creature, hit, hit.Props, enemy, shinies).GetAwaiter().GetResult();
			Equal(before + 4, owner.Gold, "gold equals HP actually lost; blocked and overkill damage do not count");
			rune.AfterDamageGiven(null!, owner.Creature, hit, hit.Props, enemy, shinies).GetAwaiter().GetResult();
			Equal(before + 8, owner.Gold, "each hit pays separately");
			DamageResult blocked = new(enemy, ValueProp.Move) { BlockedDamage = 5 };
			rune.AfterDamageGiven(null!, owner.Creature, blocked, blocked.Props, enemy, shinies).GetAwaiter().GetResult();
			rune.AfterDamageGiven(null!, other.Creature, hit, hit.Props, enemy, foreign).GetAwaiter().GetResult();
			rune.AfterDamageGiven(null!, owner.Creature, hit, hit.Props, enemy, null).GetAwaiter().GetResult();
			DamageResult self = new(other.Creature, ValueProp.Move) { UnblockedDamage = 4 };
			rune.AfterDamageGiven(null!, owner.Creature, self, self.Props, other.Creature, shinies).GetAwaiter().GetResult();
			Equal(before + 8, owner.Gold, "fully blocked hits, teammates' My Shinies, other damage and non-enemy targets pay nothing");
			Equal(2, listener.Calls, "native gold hooks run once per paying hit");
			Equal(0, other.Gold, "teammate gold is unchanged");
		});
	}
#endif
}
