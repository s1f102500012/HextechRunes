using System.Text.Json.Serialization;

namespace HextechRunes;

internal static partial class HextechTelemetry
{
	internal sealed record TelemetryConfig(bool Enabled, string Endpoint);

	private sealed record RunEndedPayload(
		int SchemaVersion,
		string ModId,
		string ModVersion,
		// 服务端契约字段名是 gameVersion,但填的是本变体编译目标版本(ModInfo.TargetGameVersion),
		// 不是运行时读到的宿主游戏版本;代码内按真实含义命名,上报 JSON 键名保持不变。
		[property: JsonPropertyName("gameVersion")] string TargetGameVersion,
		string UploadedAtUtc,
		RunTelemetry Run,
		IReadOnlyList<PlayerTelemetry> Players,
		IReadOnlyList<HextechRuneChoiceRecord> RuneChoices,
		IReadOnlyList<MonsterHexTelemetry> MonsterHexes);

	private sealed record RunTelemetry(
		string RunId,
		string SeedHash,
		bool IsVictory,
		string NetMode,
		int PlayerCount,
		int Ascension,
		int CurrentActIndex,
		int TotalFloor,
		long RunTime);

	private sealed record PlayerTelemetry(
		int Slot,
		string Character,
		IReadOnlyList<string> HextechRunes);

	private sealed record MonsterHexTelemetry(
		int ActIndex,
		string Rarity,
		string Hex);
}
