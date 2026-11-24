namespace HextechRunes;

/// <summary>
/// 敌方质变：与玩家质变"获得时移除自身"同口径。本幕敌方海克斯落定（CompleteStage 写入累积集）之前，
/// 质变被换成 <see cref="ObtainCount"/> 个目标品阶的随机敌方海克斯，所以它不会作为生效中的海克斯留下，
/// 效果类只承载身份；目标品阶见 <see cref="HextechEnemyTransmute.GetTargetRarity"/>。
/// </summary>
internal abstract class TransmuteEnemyHexBase : HextechEnemyHexEffect
{
	internal const int ObtainCount = 2;
}

internal static class HextechEnemyTransmute
{
	internal static HextechRarityTier? GetTargetRarity(MonsterHexKind kind)
	{
		return kind switch
		{
			MonsterHexKind.TransmuteSilver => HextechRarityTier.Silver,
			MonsterHexKind.TransmuteGold => HextechRarityTier.Gold,
			_ => null
		};
	}

	/// <summary>
	/// 按本局配置的敌方海克斯池展开质变。用稳定哈希抽取：联机各端独立算出同一结果，
	/// 单机也不推进原版随机流，读档重进同一阶段得到同样的结果。
	/// </summary>
	internal static IReadOnlyList<MonsterHexKind> ExpandForRun(
		RunState runState,
		HextechMayhemModifier modifier,
		string scope,
		IReadOnlyList<MonsterHexKind> hexes)
	{
		IReadOnlyList<MonsterHexKind> expanded = Expand(
			hexes,
			rarity => HextechMonsterHexRoller.GetConfiguredRarityPool(rarity, modifier.DisabledMonsterHexIdsForPool, runState),
			(pool, step) => HextechStableRandom.Index(
				runState,
				pool.Count,
				"enemy-transmute",
				scope,
				step.ToString(),
				string.Join(",", pool.Select(static kind => ((int)kind).ToString()))),
			HextechTransmuteNotice.ShowEnemy);
		if (!expanded.SequenceEqual(hexes))
		{
			HextechLog.Info("Mayhem", $"Enemy transmute expanded: scope={scope} before={string.Join(",", hexes)} after={string.Join(",", expanded)}");
		}

		return expanded;
	}

	/// <summary>
	/// 依次把每个质变替换为目标品阶里尚未拥有的随机海克斯，结果插在质变原来的位置。
	/// 抽到的海克斯本身也是质变时继续展开；同一次展开里已消耗的质变不会被再次抽到，保证终止。
	/// 候选不足时少给，不补其他品阶。每抽到一个就按顺序回调 onTransmuted(质变, 抽到的海克斯)。
	/// </summary>
	internal static IReadOnlyList<MonsterHexKind> Expand(
		IReadOnlyList<MonsterHexKind> hexes,
		Func<HextechRarityTier, IReadOnlyList<MonsterHexKind>> getRarityPool,
		Func<IReadOnlyList<MonsterHexKind>, int, int> chooseIndex,
		Action<MonsterHexKind, MonsterHexKind>? onTransmuted = null)
	{
		List<MonsterHexKind> result = hexes.Distinct().ToList();
		HashSet<MonsterHexKind> consumed = [];
		int step = 0;
		while (true)
		{
			int position = result.FindIndex(static kind => GetTargetRarity(kind).HasValue);
			if (position < 0)
			{
				return result;
			}

			MonsterHexKind transmute = result[position];
			HextechRarityTier targetRarity = GetTargetRarity(transmute)!.Value;
			result.RemoveAt(position);
			consumed.Add(transmute);
			for (int i = 0; i < TransmuteEnemyHexBase.ObtainCount; i++)
			{
				List<MonsterHexKind> pool = getRarityPool(targetRarity)
					.Where(kind => !result.Contains(kind) && !consumed.Contains(kind))
					.Distinct()
					.OrderBy(static kind => (int)kind)
					.ToList();
				if (pool.Count == 0)
				{
					break;
				}

				MonsterHexKind gained = pool[chooseIndex(pool, step++)];
				result.Insert(position++, gained);
				onTransmuted?.Invoke(transmute, gained);
			}
		}
	}
}
