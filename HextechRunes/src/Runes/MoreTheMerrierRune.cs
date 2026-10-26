namespace HextechRunes;

// 生命部分走最大生命系数管线（与星界躯体、巨人化相同）：遗物增减后由下方补丁按新遗物数重算。
public sealed class MoreTheMerrierRune : HextechRelicBase, IHextechMaxHpScalingRune, IHextechHealingMultiplierProvider
{
	// 替换遗物（原版先移除再获得）期间不在两步之间各算一次，结束后按事务前后的净增量重算一次。
	private static readonly HashSet<Player> ReplacingPlayers = [];

	private int _baseMaxHp;
	private bool _hpScalingActive;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => _baseMaxHp;
		set => _baseMaxHp = Math.Max(0, value);
	}

	// 旧版多多益善不加最大生命：读旧存档时为 false，系数先按 1 计，等进房间时按正确的基础值补一次迁移。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedHpScalingActive
	{
		get => _hpScalingActive;
		set => _hpScalingActive = value;
	}

	public int BaseMaxHp
	{
		get => _baseMaxHp;
		set => _baseMaxHp = Math.Max(1, value);
	}

	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PercentPerRelic", 1m)
	];

	private decimal SustainMultiplier => 1m + CountRelics() * DynamicVars["PercentPerRelic"].BaseValue / 100m;

	public decimal MaxHpScale => _hpScalingActive ? SustainMultiplier : 1m;

	public override Task AfterObtained() => EnsureHpScalingActive();

	public override Task AfterRoomEntered(AbstractRoom room) => EnsureHpScalingActive();

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner.Creature ? SustainMultiplier : 1m;
	}

	public override decimal ModifyDamageMultiplicativeCompat(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return IsDamageFromOwnerToEnemyOrPreview(target, dealer, cardSource) ? SustainMultiplier : 1m;
	}

	decimal IHextechHealingMultiplierProvider.ModifyHealingMultiplicative(Player player, Creature creature, decimal amount)
	{
		return IsFirstOwnedInstance(player) ? SustainMultiplier : 1m;
	}

	// 开启生命系数前先按“尚未放大”的口径补齐基础值（此时自身系数仍为 1），再整体重算。
	private async Task EnsureHpScalingActive()
	{
		if (_hpScalingActive || Owner.Creature.IsDead)
		{
			return;
		}

		HextechMaxHpScaling.EnsureScaledBaseInitialized(Owner, this);
		_hpScalingActive = true;
		await HextechMaxHpScaling.ReapplyScale(Owner);
	}

	private int CountRelics()
	{
		return Owner.Relics.Count;
	}

	private readonly record struct RelicChange(Player Player, int BaseMaxHp, int MaxHpBefore);

	private static RelicChange? Capture(Player? player)
	{
		if (player?.GetRelic<MoreTheMerrierRune>() == null || ReplacingPlayers.Contains(player))
		{
			return null;
		}

		return new RelicChange(player, HextechMaxHpScaling.GetPrimary(player)?.BaseMaxHp ?? 0, player.Creature.MaxHp);
	}

	// 变更前的基础值交给剩下的主持有者；一个都不剩（移除了唯一的多多益善）就把最大生命恢复到基础值。
	private static async Task Settle(RelicChange change)
	{
		Player player = change.Player;
		if (player.Creature.IsDead)
		{
			return;
		}

		if (player.GetRelic<MoreTheMerrierRune>() is { } rune)
		{
			await rune.EnsureHpScalingActive();
		}

		if (HextechMaxHpScaling.GetPrimary(player) is not IHextechMaxHpBaseHolder primary)
		{
			if (change.BaseMaxHp > 0 && player.Creature.MaxHp != change.BaseMaxHp)
			{
				await CreatureCmd.SetMaxHp(player.Creature, change.BaseMaxHp);
			}

			return;
		}

		if (primary.BaseMaxHp <= 0 && change.BaseMaxHp > 0)
		{
			primary.BaseMaxHp = change.BaseMaxHp;
		}

		await HextechMaxHpScaling.ReapplyScale(player, change.MaxHpBefore);
	}

	private static async Task<T> SettleAfter<T>(Task<T> original, RelicChange change)
	{
		T result = await original;
		await Settle(change);
		return result;
	}

	private static async Task SettleAfter(Task original, RelicChange change)
	{
		await original;
		await Settle(change);
	}

	// 原版获得、移除、替换遗物都没有可挂的 Hook；包住命令的返回任务，结算完后按新遗物数重算最大生命。
	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), typeof(RelicModel), typeof(Player), typeof(int))]
	[HextechPatch("rune.more-the-merrier.relic-obtain", "多多益善", Rune = typeof(MoreTheMerrierRune))]
	private static class RelicObtainPatch
	{
		[HarmonyPrefix]
		private static void Prefix(Player player, out RelicChange? __state) => __state = Capture(player);

		[HarmonyPostfix]
		private static void Postfix(RelicChange? __state, ref Task<RelicModel> __result)
		{
			if (__state is { } change)
			{
				__result = SettleAfter(__result, change);
			}
		}
	}

	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove), typeof(RelicModel))]
	[HextechPatch("rune.more-the-merrier.relic-remove", "多多益善", Rune = typeof(MoreTheMerrierRune))]
	private static class RelicRemovePatch
	{
		[HarmonyPrefix]
		private static void Prefix(RelicModel relic, out RelicChange? __state) => __state = Capture(relic.Owner);

		[HarmonyPostfix]
		private static void Postfix(RelicChange? __state, ref Task __result)
		{
			if (__state is { } change)
			{
				__result = SettleAfter(__result, change);
			}
		}
	}

	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Replace), typeof(RelicModel), typeof(RelicModel))]
	[HextechPatch("rune.more-the-merrier.relic-replace", "多多益善", Rune = typeof(MoreTheMerrierRune))]
	private static class RelicReplacePatch
	{
		[HarmonyPrefix]
		private static void Prefix(RelicModel original, out RelicChange? __state)
		{
			__state = Capture(original.Owner);
			if (__state is { } change)
			{
				ReplacingPlayers.Add(change.Player);
			}
		}

		[HarmonyPostfix]
		private static void Postfix(RelicChange? __state, ref Task<RelicModel> __result)
		{
			if (__state is { } change)
			{
				__result = SettleReplace(__result, change);
			}
		}

		[HarmonyFinalizer]
		private static Exception? Finalizer(Exception? __exception, RelicChange? __state)
		{
			// 原方法同步抛出时后缀不会包上收尾任务，这里补上移除标记。
			if (__exception != null && __state is { } change)
			{
				ReplacingPlayers.Remove(change.Player);
			}

			return __exception;
		}

		private static async Task<RelicModel> SettleReplace(Task<RelicModel> original, RelicChange change)
		{
			RelicModel result;
			try
			{
				result = await original;
			}
			finally
			{
				ReplacingPlayers.Remove(change.Player);
			}

			await Settle(change);
			return result;
		}
	}
}
