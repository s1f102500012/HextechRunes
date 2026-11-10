#if STS2_107_1
using MegaCrit.Sts2.Core.Helpers;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

/// <summary>
/// 把 <c>SavedPropertiesTypeCache</c> 的模组 net-id 布局规范化为确定性顺序,消除联机「1014 模组不匹配」的非确定性根因
/// (运行时 SavedProperties net-id 因模组加载顺序在两端错位 → (反)序列化抛异常 → 联机中断)。
/// 仅 0.107.1 需要:0.109 起 ModelIdSerializationCache.Init 由游戏自己做确定性排序与哈希。
///
/// 时序:挂在 <c>OneTimeInitialization.ExecuteEssential</c> 的后缀。该方法是固定启动状态机里唯一一次、且在
/// 所有 SavedProperty 注入完成之后(ModInitializer 阶段的自注入 + LocManager.Initialize 阶段 RitsuLib 的补注入)、
/// 任何联机序列化之前运行的点,因此是规范化的安全收口位置。
///
/// 规范化规则与可单测的纯逻辑见 <see cref="HextechSavedPropertyNetIdCanonicalizer"/>。
/// </summary>
internal static class HextechSavedPropertyNetIdHooks
{
	private const BindingFlags StaticNonPublic = BindingFlags.NonPublic | BindingFlags.Static;

	private static bool _registrationFrozen;

	/// <summary>
	/// 冻结点(ExecuteEssential 后缀)是否已过。此后再注入 SavedProperty 载体会绕过规范排序
	/// (见 HextechSavedPropertyBootstrap.InjectModelType 的告警)。与规范化成功与否无关。
	/// </summary>
	internal static bool IsRegistrationFrozen => _registrationFrozen;

	private static void CanonicalizeNetIdMapPostfix()
	{
		if (_registrationFrozen)
		{
			return;
		}

		_registrationFrozen = true;

		try
		{
			IReadOnlySet<string> vanillaNames = BuildVanillaPropertyNameSet();
			if (vanillaNames.Count == 0)
			{
				HextechLog.Error("MultiplayerCompat", $"Could not determine vanilla SavedProperty names; net-id canonicalization NOT applied (multiplayer with other SavedProperty mods may desync).");
				return;
			}

			// SavedPropertiesTypeCache._netIdToPropertyNameMap / _propertyNameToNetIdMap:0.107.1 原版私有静态字段
			// (本变体只在 0.107.1 目标编译)。缺失时经 HookReflection 进启动摘要,下面的空引用由外层 catch 记错误。
			List<string> netIdToName = (List<string>)TryGetField(typeof(SavedPropertiesTypeCache), "_netIdToPropertyNameMap", StaticNonPublic)!.GetValue(null)!;
			Dictionary<string, int> nameToNetId = (Dictionary<string, int>)TryGetField(typeof(SavedPropertiesTypeCache), "_propertyNameToNetIdMap", StaticNonPublic)!.GetValue(null)!;
			List<string> canonical = HextechSavedPropertyNetIdCanonicalizer.Canonicalize(netIdToName, vanillaNames);

			// 原地重建两张表,保持原引用不变(其它代码可能持有同一 List/Dictionary 引用)。
			netIdToName.Clear();
			netIdToName.AddRange(canonical);
			nameToNetId.Clear();
			for (int i = 0; i < canonical.Count; i++)
			{
				nameToNetId[canonical[i]] = i;
			}

			int bitSize = HextechSavedPropertyNetIdCanonicalizer.ComputeNetIdBitSize(canonical.Count);
			if (!TrySetNetIdBitSize(bitSize))
			{
				// 两张表已按规范布局重建(条目数不变,旧位宽仍能容纳),但位宽没有按公式写入:不宣称成功。
				HextechLog.Error("MultiplayerCompat", $"SavedProperty net-id maps canonicalized but NetIdBitSize could not be written (expected {bitSize}, actual {SavedPropertiesTypeCache.NetIdBitSize}); canonicalization NOT marked complete.");
				return;
			}

			HextechLog.Info("MultiplayerCompat", $"Canonicalized SavedProperty net-id map: vanilla={vanillaNames.Count} total={canonical.Count} bitSize={SavedPropertiesTypeCache.NetIdBitSize}.");
		}
		catch (Exception ex)
		{
			HextechLog.Error("MultiplayerCompat", $"SavedProperty net-id canonicalization failed (registration window is frozen, map may be unchanged): {ex.GetType().Name}: {ex.Message}");
		}
		finally
		{
			// 载体漏注入自检只在这里做一次(规范化成败都做,自检自带 catch):此刻本体与拓展包/二创包的注册窗口都已关闭,
			// 扫描所有引用本模组的程序集,抓"新增 [SavedProperty] 载体却忘了登记"的漏项。
			HextechSavedPropertyBootstrap.WarnOnUninjectedSavedPropertyCarriers();
		}
	}

	/// <summary>
	/// 重放原版种子:遍历 <c>AbstractModelSubtypes</c> 收集所有带 <c>[SavedProperty]</c> 的属性名。
	/// 只需名字集合(用于把原版前缀与模组后缀区分开),不依赖游戏的排序细节,且随游戏版本自适应。
	/// 必须与游戏 <c>CachePropertiesForType</c> 一致地用 Instance|Public|NonPublic 取属性。
	/// </summary>
	private static IReadOnlySet<string> BuildVanillaPropertyNameSet()
	{
		const BindingFlags propertyFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (Type type in AbstractModelSubtypes.All)
		{
			foreach (PropertyInfo property in type.GetProperties(propertyFlags))
			{
				if (property.GetCustomAttribute<SavedPropertyAttribute>() != null)
				{
					names.Add(property.Name);
				}
			}
		}

		return names;
	}

	private static bool TrySetNetIdBitSize(int bitSize)
	{
		// SavedPropertiesTypeCache.NetIdBitSize 的自动属性后备字段（0.107.1 原版只有 private set）。
		TryGetField(typeof(SavedPropertiesTypeCache), "<NetIdBitSize>k__BackingField", StaticNonPublic)!.SetValue(null, bitSize);
		return SavedPropertiesTypeCache.NetIdBitSize == bitSize;
	}

	// 仅 0.107.1：原版在固定启动状态机里调用一次 OneTimeInitialization.ExecuteEssential（public static）。
	// 由属性声明目标，缺失即安装失败，避免 Patcher 误报成功。
	[HarmonyPatch(typeof(OneTimeInitialization), nameof(OneTimeInitialization.ExecuteEssential))]
	[HextechPatch("compat.saved-property-net-id", "SavedProperty net-id 规范化")]
	private static class CanonicalizePatch
	{
		[HarmonyPostfix]
		private static void Postfix() => CanonicalizeNetIdMapPostfix();
	}
}
#endif
