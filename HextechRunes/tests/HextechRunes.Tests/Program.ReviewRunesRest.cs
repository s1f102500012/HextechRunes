using System.Reflection;
using System.Runtime.CompilerServices;
using HextechRunes;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>
	/// 私有反射改为 TryGet* 后，三个维护版本都必须能解析到这些原版成员，否则功能会静默降级。
	/// </summary>
	[HextechTest]
	private static void RuneReflectionTargetsResolveOnCurrentGameVersion()
	{
		foreach ((Type owner, string field) in new (Type, string)[]
		{
			(typeof(AutomationUpgradeRune), "AutomationDataType"),
			(typeof(AutomationUpgradeRune), "PowerGetInternalDataMethod"),
			(typeof(AutomationUpgradeRune), "AutomationCardsLeftField"),
			(typeof(EarthAwakensRune), "PowerFlashMethod"),
			(typeof(SolidTimeRune), "CardOnPlayMethod"),
			(typeof(DoubleVisionRune), "GoldRewardWasStolenBackField"),
			(typeof(SweepingBladeRune), "AttackCommandSingleTargetField"),
			(typeof(SweepingBladeRune), "AttackCommandCombatStateField"),
			(typeof(HextechTreasureRuneHooks), "CurrentRelicsField"),
			(typeof(HextechTreasureRuneHooks), "PlayerCollectionField")
		})
		{
			FieldInfo handle = owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
				?? throw new MissingFieldException(owner.Name, field);
			Expect(handle.GetValue(null) != null, $"{owner.Name}.{field} should resolve against sts2 {ModInfo.TargetGameVersion}");
		}
	}

	/// <summary>
	/// 退役的存档占位属性必须保留名称与类型(net-id 布局)，读取恒为类型默认值、写入被忽略。
	/// </summary>
	[HextechTest]
	private static void RetiredRuneSavedPropertiesStayInertPlaceholders()
	{
		foreach ((Type owner, string property, object legacyValue, object expected) in new (Type, string, object, object)[]
		{
			(typeof(TapDanceRune), "SavedPendingDraw", 7, 0),
			(typeof(SoulEaterRune), "SavedHpGainedThisCombat", 7, 0),
			(typeof(SoulEaterRune), "SavedMaxHpGainCapThisCombat", 7, 0),
			(typeof(UltimateRefreshRune), "SavedTriggeredThisTurn", true, false)
		})
		{
			PropertyInfo info = owner.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
				?? throw new MissingMemberException(owner.Name, property);
			Expect(info.PropertyType == expected.GetType(), $"{owner.Name}.{property} should keep its SavedProperty type");
			Expect(info.GetCustomAttribute<SavedPropertyAttribute>() != null, $"{owner.Name}.{property} should stay a SavedProperty");
			object rune = RuntimeHelpers.GetUninitializedObject(owner);
			info.SetValue(rune, legacyValue);
			Expect(Equals(info.GetValue(rune), expected), $"{owner.Name}.{property} should ignore legacy values");
		}
	}
}
