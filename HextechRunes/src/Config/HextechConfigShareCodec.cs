using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HextechRunes;

/// <summary>
/// 海克斯配置分享码：`HEXCFG1:` + base64(gzip(紧凑 JSON))。
/// 导出取当前配置快照（不含 ModEnabled 与 UI 偏好——导入别人的码不应关掉对方 mod 或改界面习惯）；
/// 导入只解析并生成快照与差异摘要，真正落盘由调用方确认后走 SaveSnapshot（内部全量 Normalize/Clamp，
/// 敌方海克斯禁用表按注册表丢弃未知 ID，我方符文与锻造器的未知 ID 原样保留；数值自动收敛，脏码不会产生非法状态）。
/// </summary>
internal static class HextechConfigShareCodec
{
	private const string Prefix = "HEXCFG1:";
	// 载荷版本:1 = 旧三段稀有度权重(只读 wn;同版本的 w1/w2 从不读取,反序列化按未知字段跳过);2 = 单组权重(wr)+防连续银;3 = 金色重掷概率;4 = 分幕权重(wa)。
	private const int OldestSupportedShareVersion = 1;
	private const int SingleRarityWeightsShareVersion = 2;
	private const int GoldenRerollShareVersion = 3;
	private const int RarityWeightsByActShareVersion = 4;
	private const int CurrentShareVersion = RarityWeightsByActShareVersion;
	private const int MaxEncodedLength = 64 * 1024;
	private const int MaxDecodedLength = 512 * 1024;

	/// <param name="Snapshot">规范化后的快照;<c>ModEnabled</c> 不在载荷里,恒为默认值,应用方不得读取。</param>
	internal sealed record ImportPreview(
		HextechRunConfigurationSnapshot Snapshot,
		int IgnoredUnknownCount);

	private sealed record SharePayload(
		[property: JsonPropertyName("v")] int Version,
		[property: JsonPropertyName("pc")] int[]? PlayerHexCountsByAct,
		[property: JsonPropertyName("ec")] int[]? EnemyHexCountsByAct,
		[property: JsonPropertyName("pr")] int PlayerRuneRerollLimit,
		[property: JsonPropertyName("mr")] int MonsterHexRerollLimit,
		[property: JsonPropertyName("dp")] string[]? DisabledPlayerRuneIds,
		[property: JsonPropertyName("dm")] string[]? DisabledMonsterHexIds,
		[property: JsonPropertyName("df")] string[]? DisabledForgeIds,
		[property: JsonPropertyName("wr")] int[]? RuneRarityWeights,
		[property: JsonPropertyName("wa")] int[][]? RuneRarityWeightsByAct,
		[property: JsonPropertyName("ns")] bool? PreventConsecutiveSilverRunes,
		[property: JsonPropertyName("gr")] int? GoldenRerollChancePercent,
		[property: JsonPropertyName("wn")] int[]? NormalRuneRarityWeights,
		[property: JsonPropertyName("wf")] int[]? ForgeRarityWeights,
		[property: JsonPropertyName("fp")] int RandomForgeShopPrice,
		[property: JsonPropertyName("fg")] bool RandomForgeDirectGrant,
		[property: JsonPropertyName("cc")] int? ChaosRuneChancePercent = null);

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static string Export(HextechRunConfigurationSnapshot snapshot)
	{
		SharePayload payload = new(
			Version: CurrentShareVersion,
			PlayerHexCountsByAct: snapshot.PlayerHexCountsByAct.ToArray(),
			EnemyHexCountsByAct: snapshot.EnemyHexCountsByAct.ToArray(),
			PlayerRuneRerollLimit: snapshot.PlayerRuneRerollLimit,
			MonsterHexRerollLimit: snapshot.MonsterHexRerollLimit,
			DisabledPlayerRuneIds: snapshot.DisabledPlayerRuneIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
			DisabledMonsterHexIds: snapshot.DisabledMonsterHexIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
			DisabledForgeIds: snapshot.DisabledForgeIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
			RuneRarityWeights: null,
			RuneRarityWeightsByAct: snapshot.RuneRarityWeightsByAct.Select(ToArray).ToArray(),
			PreventConsecutiveSilverRunes: snapshot.PreventConsecutiveSilverRunes,
			GoldenRerollChancePercent: snapshot.GoldenRerollChancePercent,
			NormalRuneRarityWeights: null,
			ForgeRarityWeights: ToArray(snapshot.ForgeRarityWeights),
			RandomForgeShopPrice: snapshot.RandomForgeShopPrice,
			RandomForgeDirectGrant: snapshot.RandomForgeDirectGrant,
			ChaosRuneChancePercent: snapshot.ChaosRuneChancePercent);

		byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
		using MemoryStream output = new();
		using (GZipStream gzip = new(output, CompressionLevel.Optimal, leaveOpen: true))
		{
			gzip.Write(json, 0, json.Length);
		}

		return Prefix + Convert.ToBase64String(output.ToArray());
	}

	/// <summary>解析分享码并生成导入预览。失败返回 null（格式错/超限/解压失败等一律视为无效码）。</summary>
	public static ImportPreview? TryParse(string? code)
	{
		try
		{
			string trimmed = code?.Trim() ?? string.Empty;
			if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal) || trimmed.Length > MaxEncodedLength)
			{
				return null;
			}

			byte[] compressed = Convert.FromBase64String(trimmed[Prefix.Length..]);
			using MemoryStream input = new(compressed);
			using GZipStream gzip = new(input, CompressionMode.Decompress);
			using MemoryStream output = new();
			CopyBounded(gzip, output, MaxDecodedLength);
			SharePayload? payload = JsonSerializer.Deserialize<SharePayload>(output.ToArray(), JsonOptions);
			if (payload == null || payload.Version is < OldestSupportedShareVersion or > CurrentShareVersion)
			{
				return null;
			}

			return BuildPreview(payload);
		}
		catch
		{
			return null;
		}
	}

	private static ImportPreview BuildPreview(SharePayload payload)
	{
		int rawDisabledCount = (payload.DisabledPlayerRuneIds?.Length ?? 0)
			+ (payload.DisabledMonsterHexIds?.Length ?? 0)
			+ (payload.DisabledForgeIds?.Length ?? 0);

		// 先按载荷版本组装原始快照,再统一交给 NormalizeSnapshot:与落盘、联机解码走同一套 clamp/回落规则,
		// 预览显示的就是保存后的值(例如全 0 权重在预览里就已回落为默认)。
		HextechRunConfigurationSnapshot snapshot = HextechRuneConfiguration.NormalizeSnapshot(new HextechRunConfigurationSnapshot(
			PlayerHexCountsByAct: payload.PlayerHexCountsByAct ?? HextechRuneConfiguration.GetDefaultPlayerHexCountsByAct(),
			EnemyHexCountsByAct: payload.EnemyHexCountsByAct ?? HextechRuneConfiguration.GetDefaultEnemyHexCountsByAct(),
			PlayerRuneRerollLimit: payload.PlayerRuneRerollLimit,
			MonsterHexRerollLimit: payload.MonsterHexRerollLimit,
			DisabledPlayerRuneIds: (payload.DisabledPlayerRuneIds ?? []).ToHashSet(StringComparer.Ordinal),
			DisabledMonsterHexIds: (payload.DisabledMonsterHexIds ?? []).ToHashSet(StringComparer.Ordinal),
			DisabledForgeIds: (payload.DisabledForgeIds ?? []).ToHashSet(StringComparer.Ordinal),
			RuneRarityWeightsByAct: ReadRuneRarityWeightsByAct(payload),
			PreventConsecutiveSilverRunes: payload.Version >= SingleRarityWeightsShareVersion
				? payload.PreventConsecutiveSilverRunes ?? HextechRuneConfiguration.GetDefaultPreventConsecutiveSilverRunes()
				: HextechRuneConfiguration.GetDefaultPreventConsecutiveSilverRunes(),
			GoldenRerollChancePercent: payload.Version >= GoldenRerollShareVersion
				? payload.GoldenRerollChancePercent ?? HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent()
				: HextechRuneConfiguration.GetDefaultGoldenRerollChancePercent(),
			ForgeRarityWeights: ToRarityWeights(payload.ForgeRarityWeights, HextechRuneConfiguration.GetDefaultForgeRarityWeights()),
			RandomForgeShopPrice: payload.RandomForgeShopPrice,
			RandomForgeDirectGrant: payload.RandomForgeDirectGrant,
			ModEnabled: HextechRuneConfiguration.DefaultModEnabled,
			ChaosRuneChancePercent: payload.ChaosRuneChancePercent ?? HextechRuneConfiguration.DefaultChaosRuneChancePercent));

		int normalizedDisabledCount = snapshot.DisabledPlayerRuneIds.Count
			+ snapshot.DisabledMonsterHexIds.Count
			+ snapshot.DisabledForgeIds.Count;
		return new ImportPreview(snapshot, Math.Max(0, rawDisabledCount - normalizedDisabledCount));
	}

	private static HextechRarityWeights[] ReadRuneRarityWeightsByAct(SharePayload payload)
	{
		HextechRarityWeights[] defaultsByAct = HextechRuneConfiguration.GetDefaultRuneRarityWeightsByAct();
		if (payload.Version >= RarityWeightsByActShareVersion)
		{
			IReadOnlyList<int[]> valuesByAct = payload.RuneRarityWeightsByAct ?? [];
			return defaultsByAct
				.Select((fallback, actIndex) => actIndex < valuesByAct.Count
					? ToRarityWeights(valuesByAct[actIndex], fallback)
					: fallback)
				.ToArray();
		}

		HextechRarityWeights legacyWeights = ToRarityWeights(
			payload.Version >= SingleRarityWeightsShareVersion ? payload.RuneRarityWeights : payload.NormalRuneRarityWeights,
			HextechRuneConfiguration.GetDefaultRuneRarityWeights());
		return [ legacyWeights, legacyWeights, legacyWeights ];
	}

	private static void CopyBounded(Stream source, MemoryStream destination, int maxBytes)
	{
		byte[] buffer = new byte[8192];
		int total = 0;
		int read;
		while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
		{
			total += read;
			if (total > maxBytes)
			{
				throw new InvalidDataException("Share code payload too large.");
			}

			destination.Write(buffer, 0, read);
		}
	}

	private static int[] ToArray(HextechRarityWeights weights)
	{
		return [weights.Silver, weights.Gold, weights.Prismatic];
	}

	// 原始值,clamp 与全 0 回落统一由 NormalizeSnapshot 处理。
	private static HextechRarityWeights ToRarityWeights(int[]? values, HextechRarityWeights fallback)
	{
		return values is { Length: 3 }
			? new HextechRarityWeights(values[0], values[1], values[2])
			: fallback;
	}
}
