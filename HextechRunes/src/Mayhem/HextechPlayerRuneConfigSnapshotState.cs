using System.Text.Json;

namespace HextechRunes;

internal sealed class HextechPlayerRuneConfigSnapshotState
{
	private HashSet<string>? _disabledIds;

	public bool HasSnapshot => _disabledIds != null;

	public int SnapshotCount => _disabledIds?.Count ?? 0;

	public void Set(IEnumerable<string>? disabledIds)
	{
		_disabledIds = Normalize(disabledIds);
	}

	// 没有本局快照时返回 null，不回落本机配置。
	public HashSet<string>? CopySnapshotOrNull()
	{
		return _disabledIds?.ToHashSet(StringComparer.Ordinal);
	}

	public HashSet<string> GetDisabledIdsForPool(bool isClient, IEnumerable<string> localDisabledIds)
	{
		if (_disabledIds != null)
		{
			return _disabledIds.ToHashSet(StringComparer.Ordinal);
		}

		return isClient
			? new HashSet<string>(StringComparer.Ordinal)
			: Normalize(localDisabledIds);
	}

	public static HashSet<string> Normalize(IEnumerable<string>? ids)
	{
		return HextechPlayerRuneConfigIds.Normalize(ids);
	}

	public string Serialize()
	{
		if (_disabledIds == null)
		{
			return "";
		}

		string[] ids = _disabledIds
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToArray();
		return JsonSerializer.Serialize(ids, HextechTelemetry.JsonOptions);
	}

	public bool TryRestore(string? json, out string? errorMessage)
	{
		errorMessage = null;
		if (string.IsNullOrWhiteSpace(json))
		{
			_disabledIds = null;
			return true;
		}

		try
		{
			string[]? ids = JsonSerializer.Deserialize<string[]>(json, HextechTelemetry.JsonOptions);
			Set(ids);
			return true;
		}
		catch (Exception ex)
		{
			_disabledIds = null;
			errorMessage = ex.Message;
			return false;
		}
	}
}
