using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

/// <summary>
/// 升级：自动化——触发自动化效果(每抽 10 张的能量结算)时,额外抽 2 张牌。
/// 不跳过原版:前缀读出抽牌前的计数,原版照常结算回能;本次会触发时,后缀在原版任务完成后追加抽牌。
/// 计数器读取走反射(AutomationPower.Data.cardsLeft 为私有嵌套类型)。
/// </summary>
public sealed class AutomationUpgradeRune : CardUpgradeRuneBase<Automation>
{
	// 私有访问均对照原版 0.111.0：AutomationPower 私有嵌套类 Data 的公有字段 cardsLeft，
	// PowerModel 的 protected GetInternalData<T>()。
	// 任一缺失时进启动摘要，ShouldUseUpgradedDraw 返回 false，自动化回落原版(不追加抽牌)。
	private static readonly Type? AutomationDataType = TryGetNestedType(typeof(AutomationPower), "Data");
	private static readonly MethodInfo? PowerGetInternalDataMethod = AutomationDataType == null
		? null
		: TryGetMethod(typeof(PowerModel), "GetInternalData", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
			?.MakeGenericMethod(AutomationDataType);
	private static readonly FieldInfo? AutomationCardsLeftField = AutomationDataType == null
		? null
		: TryGetField(AutomationDataType, "cardsLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static bool IsReflectionAvailable => PowerGetInternalDataMethod != null
		&& AutomationCardsLeftField != null;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(2)
	];

	protected override bool IsAvailableForCharacter(Player player)
	{
		return true;
	}

	internal static bool ShouldUseUpgradedDraw(AutomationPower power, CardModel card)
	{
		Player? owner = power.Owner?.Player;
		return IsReflectionAvailable
			&& owner != null
			&& card.Owner == owner
			&& owner.GetRelic<AutomationUpgradeRune>() != null;
	}

	// 原版本次抽牌会把计数减到 0 并回能（AutomationPower.AfterCardDrawn：cardsLeft-- 后 <= 0 即触发）。
	internal static bool WillTrigger(int cardsLeftBeforeDraw) => cardsLeftBeforeDraw <= 1;

	internal static int? ReadCardsLeft(AutomationPower power)
	{
		return PowerGetInternalDataMethod!.Invoke(power, null) is { } data
			&& AutomationCardsLeftField!.GetValue(data) is int cardsLeft
				? cardsLeft
				: null;
	}

	// 原版触发（回能、重置计数）完成后追加抽牌。
	internal static async Task DrawAfterVanillaTrigger(Task vanilla, PlayerChoiceContext choiceContext, Player owner)
	{
		await vanilla;
		if (owner.Creature.IsDead
			|| owner.Creature.CombatState == null
			|| owner.GetRelic<AutomationUpgradeRune>() is not AutomationUpgradeRune rune)
		{
			return;
		}

		rune.Flash();
		await CardPileCmd.Draw(choiceContext, rune.DynamicVars.Cards.BaseValue, owner, fromHandDraw: false);
	}

	[HarmonyPatch(typeof(AutomationPower), nameof(AutomationPower.AfterCardDrawn), typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool))]
	[HextechPatch("rune.automation", "升级自动化", Rune = typeof(AutomationUpgradeRune))]
	private static class AutomationPatch
	{
		[HarmonyPrefix]
		private static void Prefix(AutomationPower __instance, CardModel card, out int? __state)
		{
			__state = AutomationUpgradeRune.ShouldUseUpgradedDraw(__instance, card)
				? AutomationUpgradeRune.ReadCardsLeft(__instance)
				: null;
		}

		[HarmonyPostfix]
		private static void Postfix(AutomationPower __instance, PlayerChoiceContext choiceContext, int? __state, ref Task __result)
		{
			if (__state is int cardsLeftBeforeDraw && AutomationUpgradeRune.WillTrigger(cardsLeftBeforeDraw))
			{
				__result = AutomationUpgradeRune.DrawAfterVanillaTrigger(__result, choiceContext, __instance.Owner.Player!);
			}
		}
	}
}
