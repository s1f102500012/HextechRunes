namespace HextechRunes;

/// <summary>可作为"基础最大生命"存放点的系数参与者(玩家遗物列表里第一个持有者为主符文)。</summary>
internal interface IHextechMaxHpBaseHolder
{
	int BaseMaxHp { get; set; }
}

/// <summary>最大生命系数海克斯；各自内部线性叠层，不同海克斯之间按乘积复合。</summary>
internal interface IHextechMaxHpScalingRune : IHextechMaxHpBaseHolder
{
	decimal MaxHpScale { get; }
}

/// <summary>加算类百分比生命锻造器(7.5%/15%/30%,含叠层):同类之间百分比相加,整组再与乘法系数复合。</summary>
internal interface IHextechPercentHpForge : IHextechMaxHpBaseHolder
{
	decimal MaxHpPercentTotal { get; }
}

/// <summary>
/// 最大生命系数管线:实际最大生命 = 基础值 × 各海克斯独立乘区 × (1 + Σ生命锻造器百分比)。
/// Gain/Lose/SetMaxHp 由 HextechCombatHooks.MaxHp 拦截,先改基础值再按系数换算实际值,
/// 保证后续所有最大生命增减按系数放大,顶栏"生命系数"面板同源显示。
/// </summary>
internal static class HextechMaxHpScaling
{
	// 优先取已经存下基础值的持有者：新加入的参与者（或旧存档里新增字段为 0 的参与者）排在前面时，
	// 不能抢走主持有者，否则会把已放大的当前最大生命误当基础值再放大一次。
	public static IHextechMaxHpBaseHolder? GetPrimary(Player? player)
	{
		IHextechMaxHpBaseHolder[] holders = player?.Relics.OfType<IHextechMaxHpBaseHolder>().ToArray() ?? [];
		return holders.FirstOrDefault(static holder => holder.BaseMaxHp > 0) ?? holders.FirstOrDefault();
	}

	public static decimal GetScale(Player player)
	{
		List<decimal> runeScales = [];
		List<decimal> forgePercents = [];
		foreach (RelicModel relic in player.Relics)
		{
			switch (relic)
			{
				case IHextechPercentHpForge forge:
					forgePercents.Add(forge.MaxHpPercentTotal);
					break;
				case IHextechMaxHpScalingRune rune:
					runeScales.Add(rune.MaxHpScale);
					break;
			}
		}

		return CombineScales(runeScales, forgePercents);
	}

	internal static decimal CombineScales(IEnumerable<decimal> runeScales, IEnumerable<decimal> forgePercents)
	{
		return runeScales.Aggregate(1m, static (total, runeScale) => total * runeScale)
			* (1m + forgePercents.Sum() / 100m);
	}

	public static int GetScaledMaxHp(Player player, IHextechMaxHpBaseHolder primary)
	{
		return Math.Max(1, (int)Math.Floor(primary.BaseMaxHp * GetScale(player)));
	}

	/// <summary>基础值缺失时初始化:未缩放场景直接取当前最大生命,已缩放场景按当前系数乘积反推。</summary>
	public static void EnsureBaseInitialized(Player player, IHextechMaxHpBaseHolder primary, bool assumeAlreadyScaled)
	{
		if (primary.BaseMaxHp > 0)
		{
			return;
		}

		primary.BaseMaxHp = assumeAlreadyScaled
			? Math.Max(1, (int)Math.Floor(player.Creature.MaxHp / GetScale(player)))
			: Math.Max(1, player.Creature.MaxHp);
	}

	/// <summary>
	/// 当前最大生命已按系数放大时补齐基础值（进房、叠层前调用）：主符文取遗物列表里第一个持有者，
	/// 没有时用 <paramref name="fallback"/>（调用方自己）；两者都没有就不处理。
	/// </summary>
	public static void EnsureScaledBaseInitialized(Player owner, IHextechMaxHpBaseHolder? fallback = null)
	{
		if ((GetPrimary(owner) ?? fallback) is IHextechMaxHpBaseHolder primary)
		{
			EnsureBaseInitialized(owner, primary, assumeAlreadyScaled: true);
		}
	}

	/// <summary>
	/// 系数参与者获得/叠层后重算实际最大生命并补上差值的当前生命。
	/// 基础值未初始化 ⟺ 此前没有任何参与者生效过,此时当前最大生命就是未缩放的基础值。
	/// </summary>
	/// <param name="healBaselineMaxHp">按这个最大生命计算要补的当前生命；不给时取调用时的最大生命（多步遗物变更按事务前的值算净增量）。</param>
	public static async Task ReapplyScale(Player owner, int? healBaselineMaxHp = null)
	{
		if (GetPrimary(owner) is not IHextechMaxHpBaseHolder primary)
		{
			return;
		}

		EnsureBaseInitialized(owner, primary, assumeAlreadyScaled: false);
		int oldMax = healBaselineMaxHp ?? owner.Creature.MaxHp;
		await CreatureCmd.SetMaxHp(owner.Creature, primary.BaseMaxHp);
		int delta = owner.Creature.MaxHp - oldMax;
		if (delta > 0)
		{
			await CreatureCmd.Heal(owner.Creature, delta);
		}
	}
}
