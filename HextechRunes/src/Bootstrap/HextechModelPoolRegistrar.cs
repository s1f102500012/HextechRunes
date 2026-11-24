using System.Collections;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace HextechRunes;

internal static class HextechModelPoolRegistrar
{
	// 原版 0.107.1–0.111.0:ModHelper 私有静态字段
	// Dictionary<Type, ModPoolContent> _moddedContentForPools,ModPoolContent 为私有嵌套类,
	// 其 public List<Type>? modelsToAdd 是待并入各池的模组模型。只用于查重,
	// 缺失时 HextechHookReflection 会进启动摘要,查重退化为“视为未登记”。
	private static readonly FieldInfo? ModdedContentForPoolsField =
		HextechHookReflection.TryGetField(typeof(ModHelper), "_moddedContentForPools", BindingFlags.NonPublic | BindingFlags.Static);

	private static readonly FieldInfo? ModelsToAddField = GetModelsToAddField();

	internal static void RegisterModels()
	{
		IReadOnlyList<Type> customRelicTypes = HextechModelTypeIdentity.Distinct(HextechCatalog.GetAllCustomRelicTypes());
		IReadOnlyList<Type> eventRelicTypes = HextechModelTypeIdentity.Distinct(HextechContentRegistry.EventRelicTypes);
		IReadOnlyList<Type> customCardTypes = HextechModelTypeIdentity.Distinct(HextechCatalog.GetAllCustomCardTypes());

		RegisterModelsInPool(typeof(SharedRelicPool), customRelicTypes);
		RegisterModelsInPool(typeof(EventRelicPool), eventRelicTypes);
		RegisterModelsInPool(typeof(TokenCardPool), customCardTypes);
	}

	internal static void RegisterPlayerRuneModels(IEnumerable<Type> runeTypes)
	{
		RegisterModelsInPool(typeof(SharedRelicPool), HextechModelTypeIdentity.Distinct(runeTypes));
	}

	internal static void RegisterForgeModels(IEnumerable<Type> forgeTypes)
	{
		RegisterModelsInPool(typeof(SharedRelicPool), HextechModelTypeIdentity.Distinct(forgeTypes));
	}

	// 外部敌方海克斯的展示遗物与内置 EnemyHexIconRelicTypes 同口径：进 SharedRelicPool 取得模型身份，
	// 原版自然遗物生成按注册表过滤（HextechNaturalRelicPoolHooks），玩家获取池不含它们。
	internal static void RegisterEnemyHexDisplayRelicModels(IEnumerable<Type> relicTypes)
	{
		RegisterModelsInPool(typeof(SharedRelicPool), HextechModelTypeIdentity.Distinct(relicTypes));
	}

	internal static void RegisterEventRelicModels(IEnumerable<Type> relicTypes)
	{
		RegisterModelsInPool(typeof(EventRelicPool), HextechModelTypeIdentity.Distinct(relicTypes));
	}

	private static void TryAddModelToPool(Type poolType, Type modelType)
	{
		if (IsModelAlreadyQueuedForPool(poolType, modelType))
		{
			HextechLog.Info("Bootstrap", $"Skipping duplicate pool registration for {modelType.FullName} in {poolType.FullName}.");
			return;
		}

		ModHelper.AddModelToPool(poolType, modelType);
	}

	private static void RegisterModelsInPool(Type poolType, IReadOnlyList<Type> modelTypes)
	{
		foreach (Type modelType in modelTypes)
		{
			TryAddModelToPool(poolType, modelType);
		}
	}

	internal static bool IsModelAlreadyQueuedForPool(Type poolType, Type modelType)
	{
		try
		{
			if (ModdedContentForPoolsField?.GetValue(null) is not IDictionary pools)
			{
				return false;
			}

			foreach (DictionaryEntry entry in pools)
			{
				if (entry.Key is Type existingPoolType
					&& HextechModelTypeIdentity.IsSame(existingPoolType, poolType)
					&& ContentContainsModelType(entry.Value, modelType))
				{
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Bootstrap", $"Could not inspect existing mod pool registrations: {ex.GetType().Name}: {ex.Message}");
		}

		return false;
	}

	private static bool ContentContainsModelType(object? content, Type modelType)
	{
		if (content == null)
		{
			return false;
		}

		if (ModelsToAddField?.GetValue(content) is not IEnumerable models)
		{
			return false;
		}

		foreach (object? model in models)
		{
			if (model is Type existingModelType && HextechModelTypeIdentity.IsSame(existingModelType, modelType))
			{
				return true;
			}
		}

		return false;
	}

	private static FieldInfo? GetModelsToAddField()
	{
		Type? poolContentType = HextechHookReflection.TryGetNestedType(typeof(ModHelper), "ModPoolContent");
		return poolContentType == null
			? null
			: HextechHookReflection.TryGetField(poolContentType, "modelsToAdd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	}
}
