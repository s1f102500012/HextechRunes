namespace HextechRunes;

internal static class HextechRuntimeRuneCompatibility
{
	private static readonly HashSet<Type> HookFailedPlayerRuneTypes = [];

	public static bool IsAndroidRuntime => OperatingSystem.IsAndroid();

	public static void MarkPlayerRuneHookFailed(Type runeType, string label, Exception exception)
	{
		bool firstFailure = HookFailedPlayerRuneTypes.Add(runeType);
		string state = firstFailure ? "disabled" : "already disabled";
		HextechLog.Warn("Compat", $"Player rune hook failed; {state} for this runtime: rune={runeType.Name} hook={label} error={exception.GetType().Name}: {exception.Message}");
	}

	/// <summary>本机补丁未生效的我方海克斯类型（只读副本）。这份表每台机器各自一份，不能直接决定联机共享的候选。</summary>
	internal static IReadOnlyList<Type> GetHookFailedPlayerRuneTypes()
	{
		return HookFailedPlayerRuneTypes.ToArray();
	}

	/// <summary>
	/// 候选池对"本机补丁失败"的统一口径。单机按本机表过滤；联机不看本机表，各端只按房主冻结下发的
	/// 本局禁用列表过滤（房主已在冻结时把自己的失败项并进该列表），否则各端候选池长度不同、抽出的候选不同。
	/// </summary>
	internal static Func<Type, bool> GetPlayerRunePoolRuntimeFilter()
	{
		return GetPlayerRunePoolRuntimeFilter(HextechPlayerContextHelper.IsNetworkMultiplayerRun(), HookFailedPlayerRuneTypes);
	}

	internal static Func<Type, bool> GetPlayerRunePoolRuntimeFilter(bool isNetworkMultiplayer, IReadOnlySet<Type> hookFailedTypes)
	{
		if (isNetworkMultiplayer)
		{
			return static _ => true;
		}

		HashSet<Type> failed = hookFailedTypes.ToHashSet();
		return type => !failed.Contains(type);
	}

	/// <summary>
	/// 房主冻结本局禁用列表时调用：把本机补丁失败的可配置海克斯并进列表。只并入联机位图覆盖的可配置 ID，
	/// 其余（不可配置的特殊符文等）告警后跳过。只在房主冻结点调用，读档还原和客户端都不调用。
	/// </summary>
	internal static HashSet<string> MergeHookFailedIntoHostDisabledIds(IEnumerable<string>? disabledIds, string reason)
	{
		HashSet<string> merged = MergeHookFailedIntoDisabledIds(
			disabledIds,
			GetHookFailedPlayerRuneIds(),
			GetConfigurablePlayerRuneEntries(),
			out List<string> addedIds,
			out List<string> skippedIds);
		LogHostHookFailedMerge(reason, addedIds, skippedIds);
		return merged;
	}

	internal static IReadOnlyList<string> GetHookFailedPlayerRuneIds()
	{
		return GetHookFailedPlayerRuneIds(HookFailedPlayerRuneTypes);
	}

	/// <summary>联机位图覆盖的范围：注册表里的可配置我方海克斯 ID。</summary>
	internal static IReadOnlySet<string> GetConfigurablePlayerRuneEntries()
	{
		return HextechCatalog.GetConfigurablePlayerRuneIds()
			.Select(static id => id.Entry)
			.ToHashSet(StringComparer.Ordinal);
	}

	internal static void LogHostHookFailedMerge(string reason, IReadOnlyList<string> addedIds, IReadOnlyList<string> skippedIds)
	{
		if (addedIds.Count > 0)
		{
			HextechLog.Warn("Compat", $"Host run config disables player runes whose hooks failed on this runtime: reason={reason} ids={string.Join(",", addedIds)}");
		}

		if (skippedIds.Count > 0)
		{
			HextechLog.Warn("Compat", $"Hook-failed player runes are not configurable and cannot be disabled for the run: reason={reason} ids={string.Join(",", skippedIds)}");
		}
	}

	internal static HashSet<string> MergeHookFailedIntoDisabledIds(
		IEnumerable<string>? disabledIds,
		IEnumerable<string> hookFailedIds,
		IReadOnlySet<string> configurableIds,
		out List<string> addedIds,
		out List<string> skippedIds)
	{
		HashSet<string> merged = HextechPlayerRuneConfigIds.Normalize(disabledIds);
		addedIds = [];
		skippedIds = [];
		foreach (string id in hookFailedIds.Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal))
		{
			if (!configurableIds.Contains(id))
			{
				skippedIds.Add(id);
			}
			else if (merged.Add(id))
			{
				addedIds.Add(id);
			}
		}

		return merged;
	}

	/// <summary>客户端收到房主的本局禁用列表后，若本机补丁失败的海克斯里有房主没禁用的，告警（不改共享状态）。</summary>
	internal static void WarnHookFailedRunesEnabledByHost(IReadOnlySet<string> hostDisabledIds, string reason)
	{
		IReadOnlyList<string> enabledIds = GetHookFailedRuneIdsNotDisabled(GetHookFailedPlayerRuneIds(), hostDisabledIds);
		if (enabledIds.Count > 0)
		{
			HextechLog.Warn("Compat", $"Player rune hooks failed on this runtime but the host did not disable them; multiplayer results may diverge if anyone picks them: reason={reason} ids={string.Join(",", enabledIds)}");
		}
	}

	internal static IReadOnlyList<string> GetHookFailedRuneIdsNotDisabled(IEnumerable<string> hookFailedIds, IReadOnlySet<string> disabledIds)
	{
		return hookFailedIds
			.Where(id => !disabledIds.Contains(id))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToArray();
	}

	private static IReadOnlyList<string> GetHookFailedPlayerRuneIds(IEnumerable<Type> hookFailedTypes)
	{
		return hookFailedTypes
			.Select(ModelDb.GetId)
			.Select(static id => id.Entry)
			.ToArray();
	}
}
