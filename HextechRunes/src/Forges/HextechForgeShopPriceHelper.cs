namespace HextechRunes;

internal static class HextechForgeShopPriceHelper
{
	public static int GetCurrentRandomForgeShopPrice()
	{
		// 同 HextechForgeGrantHelper:有本局快照时不回落本地配置。
		if (RunManager.Instance.DebugOnlyGetState() is RunState runState
			&& TryGetRandomForgeShopPrice(runState, out int price))
		{
			return price;
		}

		return HextechForgeGrantHelper.GetForgeConfigurationWithoutRun().RandomForgeShopPrice;
	}

	public static int GetRandomForgeShopPriceFor(RunState? runState)
	{
		return HextechRunesApi.ApplyForgeShopPriceModifiers(runState, TryGetRandomForgeShopPrice(runState, out int price)
			? price
			: GetCurrentRandomForgeShopPrice());
	}

	public static void RefreshRandomForgeShopRelic(RandomForgeShopRelic shopRelic, RunState? runState = null)
	{
		shopRelic.SetDisplayedPrice(GetRandomForgeShopPriceFor(runState));
	}

	private static bool TryGetRandomForgeShopPrice(RunState? runState, out int price)
	{
		price = 0;
		HextechMayhemModifier? modifier = HextechMayhemModifier.FindIn(runState);
		if (modifier == null)
		{
			return false;
		}

		price = modifier.RandomForgeShopPrice;
		return true;
	}
}
