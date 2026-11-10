namespace HextechRunes;

// 遍历顺序即结算顺序：按注册顺序，每步按当时的生效状态取下一项（见 HextechActiveEnemyHexCursor）。
internal static class HextechEnemyHexDispatcher
{
	internal static Task ForEachActive(
		HextechMayhemModifier modifier,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, Task> handler)
	{
		return ForEachActive(modifier, turnParticipants: null, handler);
	}

	internal static async Task ForEachActive(
		HextechMayhemModifier modifier,
		IEnumerable<Creature>? turnParticipants,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, Task> handler)
	{
		HextechEnemyHexContext context = new(modifier, turnParticipants);
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			await handler(effect, context);
		}
	}

	// 排序前一次取全当时的生效效果（OrderBy 稳定），之后的增删不影响本轮。
	internal static async Task ForEachActiveOrdered(
		HextechMayhemModifier modifier,
		Func<HextechEnemyHexEffect, int> orderSelector,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, Task> handler)
	{
		HextechEnemyHexContext context = new(modifier);
		foreach (HextechEnemyHexEffect effect in modifier.GetActiveEnemyHexEffects(out _).OrderBy(orderSelector))
		{
			await handler(effect, context);
		}
	}

	internal static bool AnyModified(
		HextechMayhemModifier modifier,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, bool> handler)
	{
		HextechEnemyHexContext context = new(modifier);
		bool modified = false;
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			modified |= handler(effect, context);
		}

		return modified;
	}

	internal static bool All(
		HextechMayhemModifier modifier,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, bool> predicate)
	{
		HextechEnemyHexContext context = new(modifier);
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			if (!predicate(effect, context))
			{
				return false;
			}
		}

		return true;
	}

	internal static T Transform<T>(
		HextechMayhemModifier modifier,
		T value,
		Func<HextechEnemyHexEffect, HextechEnemyHexContext, T, T> transform)
	{
		HextechEnemyHexContext context = new(modifier);
		HextechActiveEnemyHexCursor cursor = new(modifier);
		while (cursor.MoveNext(out HextechEnemyHexEffect? effect))
		{
			value = transform(effect, context, value);
		}

		return value;
	}
}
