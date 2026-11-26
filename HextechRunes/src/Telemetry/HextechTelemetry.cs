using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

internal static partial class HextechTelemetry
{
	private const string DefaultEndpoint = HextechServerEndpoints.TelemetryEndpoint;
	private const string ConfigFileName = "telemetry_config.json";
	private const string PendingFileName = "telemetry_pending.jsonl";
	private const int MaxPendingLines = 64;
	private const long MinRunTimeForUploadSeconds = 60;
	// 上报载荷结构版本(服务端按它解析);改字段结构时递增。
	private const int TelemetrySchemaVersion = 1;

	// 只用于遥测自己的配置文件与上报载荷；对局存档的 JSON 用 HextechMayhemSavedJson。
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	// 进程内防重:同一局可能从多条结束路径(胜利/失败结算、放弃)各触发一次 OnRunEnded。
	// 有意不清理——每局只多一个 RunId 字符串,进程生命周期内的增长可忽略,清掉反而会让重复上传重新可能。
	private static readonly HashSet<string> SubmittedRunIds = new(StringComparer.Ordinal);

	public static void Initialize()
	{
		try
		{
			EnsureConfigFile();
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Telemetry config init failed: {ex.Message}");
		}
	}

	public static void OnRunEnded(RunState? runState, SerializableRun serializableRun, bool isVictory)
	{
		try
		{
			TelemetryConfig config = LoadConfig();
			if (!config.Enabled)
			{
				return;
			}

			if (serializableRun.RunTime < MinRunTimeForUploadSeconds)
			{
				HextechLog.Info("Mayhem", $"Telemetry upload skipped for short run runTime={serializableRun.RunTime}s");
				return;
			}

			NetGameType gameType = RunManager.Instance.NetService.Type;
			if (gameType is NetGameType.Client or NetGameType.Replay)
			{
				HextechLog.Info("Mayhem", $"Telemetry upload skipped for netMode={gameType}");
				return;
			}

			RunEndedPayload? payload = BuildPayload(runState, serializableRun, isVictory, gameType);
			if (payload == null)
			{
				return;
			}

			if (!SubmittedRunIds.Add(payload.Run.RunId))
			{
				return;
			}

			string json = JsonSerializer.Serialize(payload, JsonOptions);
			_ = Task.Run(() => UploadSerializedAsync(config.Endpoint, json, payload.Run.RunId));
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Telemetry upload scheduling failed: {ex.Message}");
		}
	}
}
