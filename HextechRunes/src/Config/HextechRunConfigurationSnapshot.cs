using System.Text.Json.Serialization;

namespace HextechRunes;

internal sealed record HextechRunConfigurationSnapshot(
	int[] PlayerHexCountsByAct,
	int[] EnemyHexCountsByAct,
	int PlayerRuneRerollLimit,
	int MonsterHexRerollLimit,
	HashSet<string> DisabledPlayerRuneIds,
	HashSet<string> DisabledMonsterHexIds,
	HashSet<string> DisabledForgeIds,
	HextechRarityWeights[] RuneRarityWeightsByAct,
	bool PreventConsecutiveSilverRunes,
	int GoldenRerollChancePercent,
	HextechRarityWeights ForgeRarityWeights,
	int RandomForgeShopPrice,
	bool RandomForgeDirectGrant,
	bool ModEnabled,
	// 默认值同时是旧存档 JSON(无此字段)反序列化时的回退值。
	int ChaosRuneChancePercent = HextechRuneConfiguration.DefaultChaosRuneChancePercent,
	// 弑神试炼局(只在单人开局时由本机开关写入，见 HextechGodslayerTrial)；不进分享码与联机同步，为 false 时不写进存档。
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	bool GodslayerTrial = false)
{
	public HextechRunConfigurationSnapshot Copy()
	{
		return this with
		{
			PlayerHexCountsByAct = PlayerHexCountsByAct.ToArray(),
			EnemyHexCountsByAct = EnemyHexCountsByAct.ToArray(),
			RuneRarityWeightsByAct = RuneRarityWeightsByAct.ToArray(),
			DisabledPlayerRuneIds = DisabledPlayerRuneIds.ToHashSet(StringComparer.Ordinal),
			DisabledMonsterHexIds = DisabledMonsterHexIds.ToHashSet(StringComparer.Ordinal),
			DisabledForgeIds = DisabledForgeIds.ToHashSet(StringComparer.Ordinal)
		};
	}

	public HextechRarityWeights GetRuneRarityWeightsForAct(int actIndex)
	{
		return RuneRarityWeightsByAct[Math.Clamp(actIndex, 0, RuneRarityWeightsByAct.Length - 1)];
	}
}
