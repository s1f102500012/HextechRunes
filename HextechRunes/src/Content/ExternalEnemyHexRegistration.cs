using System.Text;

namespace HextechRunes;

[Flags]
public enum EnemyHexFlags
{
	None = 0,
	// 新配置默认关闭；已有配置文件的玩家照外部我方符文的口径，新出现的条目保持开启。
	Disabled = 1
}

/// <summary>
/// 其他模组经 <see cref="HextechRunesApi.RegisterEnemyHex(Type, Type, HextechRarityTier, int, int, int, EnemyHexFlags, string?, Func{IRunState, bool}?)"/>
/// 或 <see cref="HextechRunesInterop.RegisterEnemyHex"/> 登记的敌方海克斯：展示遗物只承载标题、图标与 relics 表的
/// <c>&lt;词干&gt;.enemyDescription</c>，效果是对每个敌人按强度档位施加一次 <see cref="PowerType"/>（见 <see cref="ExternalPowerEnemyHex"/>）。
/// 只存类型；身份（<see cref="MonsterHexKind"/> 数值与配置 ID）按展示遗物当前的 ModelId.Entry 派生，见 <see cref="ExternalEnemyHexIdentity"/>。
/// </summary>
internal readonly record struct ExternalEnemyHexRegistration(
	Type DisplayRelicType,
	Type PowerType,
	HextechRarityTier Rarity,
	int AmountAct1,
	int AmountAct2,
	int AmountAct3,
	EnemyHexFlags Flags);

/// <summary>
/// 外部敌方海克斯的身份：<see cref="MonsterHexKind"/> 数值 = 保留区间基址 | 展示遗物 ModelId.Entry 的 FNV-1a(UTF-8) 低 30 位，
/// 存档与联机仍按 int 传，格式不变；配置 ID（禁用列表、分享码、控制台、遥测）就是 Entry 本身。
/// 内置枚举只用到几百，保留区间与它不重叠。算法一经发布不再改动：改了等于给每个外部条目换身份，旧存档与配置随之失效。
/// </summary>
internal static class ExternalEnemyHexIdentity
{
	internal const int ReservedBase = 0x40000000;
	private const int HashMask = 0x3FFFFFFF;
	private const uint Fnv1aOffsetBasis = 2166136261U;
	private const uint Fnv1aPrime = 16777619U;

	internal static MonsterHexKind ComputeKind(string modelIdEntry)
	{
		ArgumentNullException.ThrowIfNull(modelIdEntry);
		uint hash = Fnv1aOffsetBasis;
		foreach (byte value in Encoding.UTF8.GetBytes(modelIdEntry))
		{
			hash = unchecked((hash ^ value) * Fnv1aPrime);
		}

		return (MonsterHexKind)(ReservedBase | (int)(hash & HashMask));
	}

	internal static bool IsReserved(MonsterHexKind kind)
	{
		return (int)kind >= ReservedBase;
	}

	/// <summary>
	/// 登记时的身份检查：数值不能撞内置或已登记的外部条目，配置 ID 不能（不区分大小写）撞内置枚举名或已登记的外部条目。
	/// 冲突时抛 <see cref="InvalidOperationException"/>；同一展示遗物类型的重复登记不算冲突（由注册表按首次登记处理）。
	/// </summary>
	internal static void EnsureAvailable(
		Type displayRelicType,
		IEnumerable<MonsterHexRegistration> builtIns,
		IEnumerable<ExternalEnemyHexRegistration> existing,
		Func<Type, ModelId> getModelId)
	{
		string configId = getModelId(displayRelicType).Entry;
		MonsterHexKind kind = ComputeKind(configId);
		foreach (MonsterHexRegistration builtIn in builtIns)
		{
			if (builtIn.Kind == kind || string.Equals(builtIn.Kind.ToString(), configId, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"External enemy hex {displayRelicType.FullName} (ModelId entry '{configId}', kind {(int)kind}) collides with built-in enemy hex {builtIn.Kind} ({(int)builtIn.Kind}); rename the display relic type.");
			}
		}

		foreach (ExternalEnemyHexRegistration other in existing)
		{
			if (HextechModelTypeIdentity.IsSame(other.DisplayRelicType, displayRelicType))
			{
				continue;
			}

			string otherId = getModelId(other.DisplayRelicType).Entry;
			if (ComputeKind(otherId) == kind || string.Equals(otherId, configId, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					$"External enemy hex {displayRelicType.FullName} (ModelId entry '{configId}', kind {(int)kind}) collides with external enemy hex {other.DisplayRelicType.FullName} (ModelId entry '{otherId}', kind {(int)ComputeKind(otherId)}); rename one of the display relic types.");
			}
		}
	}

	/// <summary>
	/// 解析全部外部条目的身份并按数值（再按配置 ID、类型名）排序，与模组加载顺序无关。登记时已拒绝冲突，
	/// 这里只兜底前置在登记之后才改写 ModelId 造成的冲突：保留排序在前的一项，其余交给 <paramref name="reportConflict"/> 后丢弃。
	/// </summary>
	internal static IReadOnlyList<ResolvedExternalEnemyHex> Resolve(
		IEnumerable<MonsterHexRegistration> builtIns,
		IEnumerable<ExternalEnemyHexRegistration> registrations,
		Func<Type, ModelId> getModelId,
		Action<string>? reportConflict)
	{
		HashSet<MonsterHexKind> usedKinds = [];
		HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
		foreach (MonsterHexRegistration builtIn in builtIns)
		{
			usedKinds.Add(builtIn.Kind);
			usedIds.Add(builtIn.Kind.ToString());
		}

		List<ResolvedExternalEnemyHex> resolved = [];
		foreach (ResolvedExternalEnemyHex candidate in registrations
			.Select(registration =>
			{
				string configId = getModelId(registration.DisplayRelicType).Entry;
				return new ResolvedExternalEnemyHex(ComputeKind(configId), configId, registration);
			})
			.OrderBy(static candidate => (int)candidate.Kind)
			.ThenBy(static candidate => candidate.ConfigId, StringComparer.Ordinal)
			.ThenBy(static candidate => candidate.Registration.DisplayRelicType.FullName, StringComparer.Ordinal))
		{
			if (usedKinds.Contains(candidate.Kind) || usedIds.Contains(candidate.ConfigId))
			{
				reportConflict?.Invoke(
					$"External enemy hex {candidate.Registration.DisplayRelicType.FullName} (ModelId entry '{candidate.ConfigId}', kind {(int)candidate.Kind}) collides with an enemy hex resolved earlier; skipped.");
				continue;
			}

			usedKinds.Add(candidate.Kind);
			usedIds.Add(candidate.ConfigId);
			resolved.Add(candidate);
		}

		return resolved;
	}
}

/// <summary>按展示遗物当前 ModelId 解析出身份的外部敌方海克斯。</summary>
internal sealed record ResolvedExternalEnemyHex(
	MonsterHexKind Kind,
	string ConfigId,
	ExternalEnemyHexRegistration Registration);
