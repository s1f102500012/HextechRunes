namespace HextechRunes;

// 生命部分走最大生命系数管线（与星界躯体、巨人化相同）：遗物数变化时由 rune.more-the-merrier.relic-count 补丁重算。
public sealed class MoreTheMerrierRune : HextechRelicBase, IHextechMaxHpScalingRune, IHextechHealingMultiplierProvider
{
	private int _baseMaxHp;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedBaseMaxHp
	{
		get => _baseMaxHp;
		set => _baseMaxHp = Math.Max(0, value);
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

	public decimal MaxHpScale => SustainMultiplier;

	public override async Task AfterObtained()
	{
		await HextechMaxHpScaling.ReapplyScale(Owner);
	}

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

	internal static async Task RefreshMaxHpAfterRelicChange(Task original, Player? player)
	{
		await original;
		if (player?.GetRelic<MoreTheMerrierRune>() != null && !player.Creature.IsDead)
		{
			await HextechMaxHpScaling.ReapplyScale(player);
		}
	}

	private int CountRelics()
	{
		return Owner.Relics.Count;
	}

	// 原版获得、移除遗物都没有可挂的 Hook；包住命令的返回任务，结算完后按新遗物数重算最大生命。
	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), typeof(RelicModel), typeof(Player), typeof(int))]
	[HextechPatch("rune.more-the-merrier.relic-obtain", "多多益善", Rune = typeof(MoreTheMerrierRune))]
	private static class RelicObtainPatch
	{
		[HarmonyPostfix]
		private static void Postfix(Player player, ref Task<RelicModel> __result)
		{
			if (player.GetRelic<MoreTheMerrierRune>() == null)
			{
				return;
			}

			Task<RelicModel> original = __result;
			__result = Refresh(original, player);
		}

		private static async Task<RelicModel> Refresh(Task<RelicModel> original, Player player)
		{
			RelicModel relic = await original;
			await RefreshMaxHpAfterRelicChange(Task.CompletedTask, player);
			return relic;
		}
	}

	[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove), typeof(RelicModel))]
	[HextechPatch("rune.more-the-merrier.relic-remove", "多多益善", Rune = typeof(MoreTheMerrierRune))]
	private static class RelicRemovePatch
	{
		[HarmonyPrefix]
		private static void Prefix(RelicModel relic, out Player? __state)
		{
			__state = relic.Owner;
		}

		[HarmonyPostfix]
		private static void Postfix(Player? __state, ref Task __result)
		{
			if (__state?.GetRelic<MoreTheMerrierRune>() != null)
			{
				__result = RefreshMaxHpAfterRelicChange(__result, __state);
			}
		}
	}
}
