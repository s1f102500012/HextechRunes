#if STS2_107_1
using System.Collections;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

// 0.107.1:游戏不自动收录模组载体,启动期手动逐个注入 SavedPropertiesTypeCache,
// 再由 HextechSavedPropertyNetIdHooks 在 ExecuteEssential 后把 net-id 表规范化;规范化之后窗口即关闭。
internal static partial class HextechSavedPropertyBootstrap
{
	private const string RegistrationFreezePointName = "SavedProperty net-id canonicalization";

	// 窗口以"冻结点(ExecuteEssential 后缀)是否已过"为准,与规范化本身成功与否无关:
	// 冻结点之后再注入都会让两端 net-id 布局分叉,规范化失败时更不能放行。
	private static bool IsRegistrationWindowClosed()
	{
		return HextechSavedPropertyNetIdHooks.IsRegistrationFrozen;
	}

	private static void InjectModelTypeCore(Type type)
	{
		if (HextechSavedPropertyNetIdHooks.IsRegistrationFrozen)
		{
			return;
		}

		SavedPropertiesTypeCache.InjectTypeIntoCache(type);
	}

	internal static void InjectCaches()
	{
		foreach (Type type in HextechModelTypeIdentity.Distinct(HextechCatalog.GetAllCustomRelicTypes()))
		{
			SavedPropertiesTypeCache.InjectTypeIntoCache(type);
		}

		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechMayhemModifier));
		foreach (Type type in HextechCustomModelRegistry.AllCustomModifierTypes)
		{
			SavedPropertiesTypeCache.InjectTypeIntoCache(type);
		}

		// Power 与退役锻造不在任何内容注册表里(注册表只覆盖符文/锻造/敌方图标/卡牌/modifier),
		// 只能手写:0.107.1 不会自动收录模组载体,这里漏一个就会在联机(反)序列化时抛错。
		// ExecuteEssential 后缀里的 WarnOnUninjectedSavedPropertyCarriers 会扫描全部带 [SavedProperty] 的
		// 模型类型,新增载体忘了登记会直接告警。
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechBurnPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechNextTurnDamagePower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechGalvanicPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryDexterityPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryStrengthLossPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryDexterityLossPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechLethalTempoTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechBloodPactTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechSlapTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechPowerShieldTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechAttackReplayPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechPlayerSlowPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporarySlowPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechOceanDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechInfernalDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechMountainDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechChemtechDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechCloudDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechVitalSparkPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechHangPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechNeurosurgePower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechHexPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechSoarPower));
		// 已退出注册表但仍保留类型的锻造器:旧存档里的实例仍会带 SavedStackCount 往返,载体必须继续注入。
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(SilverPlatingForge));
		EnsureSavedPropertyNetIdBitSize();
	}

	private static void EnsureSavedPropertyNetIdBitSize()
	{
		// 兜底:按与游戏 / RitsuLib 一致的公式 CeilToInt(Log2(count)) 把位宽抬到能容纳当前属性数。
		// 联机一致性的权威设置由 HextechSavedPropertyNetIdHooks 在规范化后统一完成;此处仅保证即便该
		// 后缀钩子未能安装,本模组单独联机时位宽也够用。不再使用旧的固定下限 16——它与原版 / RitsuLib 的
		// 公式不一致,会让一端是 16、另一端是 CeilToInt(Log2(count)),造成 net-id 位宽错位而断连。
		const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

		FieldInfo? mapField = TryGetField(typeof(SavedPropertiesTypeCache), "_netIdToPropertyNameMap", flags);
		int propertyNameCount = (mapField?.GetValue(null) as ICollection)?.Count ?? 0;
		int targetBitSize = HextechSavedPropertyNetIdCanonicalizer.ComputeNetIdBitSize(propertyNameCount);
		int currentBitSize = SavedPropertiesTypeCache.NetIdBitSize;
		if (currentBitSize >= targetBitSize)
		{
			HextechLog.Info("Mayhem", $"SavedPropertiesTypeCache NetIdBitSize unchanged: bitSize={currentBitSize} propertyNames={propertyNameCount}");
			return;
		}

		FieldInfo? backingField = TryGetField(typeof(SavedPropertiesTypeCache), "<NetIdBitSize>k__BackingField", flags);
		if (backingField == null)
		{
			HextechLog.Warn("Mayhem", $"SavedPropertiesTypeCache NetIdBitSize backing field not found; custom saved properties may desync in multiplayer.");
			return;
		}

		backingField.SetValue(null, targetBitSize);
		HextechLog.Info("Mayhem", $"SavedPropertiesTypeCache NetIdBitSize updated: old={currentBitSize} new={targetBitSize} propertyNames={propertyNameCount}");
	}
}
#endif
