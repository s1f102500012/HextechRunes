using System.Globalization;

namespace HextechRunes;

internal readonly record struct HextechPlayerCoefficients(
	decimal Health,
	decimal Damage,
	decimal Block,
	decimal Healing);

internal static class HextechPlayerCoefficientHelper
{
	private static readonly object FailureLogLock = new();
	private static readonly HashSet<(string Coefficient, Type ProviderType)> LoggedProviderFailures = [];

	public static HextechPlayerCoefficients Get(Player player)
	{
		return new HextechPlayerCoefficients(
			GetHealthMultiplier(player),
			GetDamageMultiplier(player),
			GetBlockMultiplier(player),
			GetHealingMultiplier(player));
	}

	public static decimal GetHealingMultiplier(Player player)
	{
		if (NearDeathFeastRune.IsDyingButAlive(player.Creature))
		{
			return 0m;
		}

		// 自带治疗系数的符文实现 IHextechHealingMultiplierProvider，由末尾的遍历统一相乘；
		// 这里只做 decimal 连乘、不取整，乘法可交换，结果与各项的先后无关。
		decimal multiplier = HextechForgeCoefficientHelper.GetSustainMultiplier(player);

		// 全心为你是全队效果,按全队存活持有者计,不只看自己的遗物。
		multiplier *= AllForYouRune.GetTeamHealingMultiplier(player);

		foreach (RelicModel relic in player.Relics)
		{
			if (relic is not IHextechHealingMultiplierProvider provider)
			{
				continue;
			}

			try
			{
				multiplier *= provider.ModifyHealingMultiplicative(player, player.Creature, 1m);
			}
			catch (Exception ex)
			{
				WarnProviderFailureOnce("healing", relic.GetType(), ex);
			}
		}

		return multiplier;
	}

	public static string FormatPercent(decimal multiplier)
	{
		decimal percent = Math.Round(multiplier * 100m, 1, MidpointRounding.AwayFromZero);
		return decimal.Remainder(percent, 1m) == 0m
			? $"{decimal.ToInt32(percent)}%"
			: $"{percent.ToString("0.#", CultureInfo.InvariantCulture)}%";
	}

	private static decimal GetHealthMultiplier(Player player)
	{
		return HextechMaxHpScaling.GetScale(player);
	}

	private static decimal GetDamageMultiplier(Player player)
	{
		return MultiplyRelicModifiers(
			player,
			"damage",
			static (relic, owner) => relic.ModifyDamageMultiplicative(null, 1m, ValueProp.Unpowered, owner.Creature, null, null));
	}

	private static decimal GetBlockMultiplier(Player player)
	{
		return MultiplyRelicModifiers(
			player,
			"block",
			static (relic, owner) => relic.ModifyBlockMultiplicative(owner.Creature, 1m, ValueProp.Unpowered, null, null))
			* AllForYouRune.GetBlockMultiplierFromTeammates(player);
	}

	private static decimal MultiplyRelicModifiers(
		Player player,
		string coefficient,
		Func<RelicModel, Player, decimal> getMultiplier)
	{
		decimal multiplier = 1m;
		foreach (RelicModel relic in player.Relics)
		{
			try
			{
				multiplier *= getMultiplier(relic, player);
			}
			catch (Exception ex)
			{
				WarnProviderFailureOnce(coefficient, relic.GetType(), ex);
			}
		}

		return multiplier;
	}

	private static void WarnProviderFailureOnce(string coefficient, Type providerType, Exception exception)
	{
		lock (FailureLogLock)
		{
			if (!LoggedProviderFailures.Add((coefficient, providerType)))
			{
				return;
			}
		}

		HextechLog.Warn(
			"Coefficient", $"Ignored {coefficient} multiplier failure from " +
			$"{providerType.FullName}: {exception.GetType().Name}: {exception.Message}");
	}
}
