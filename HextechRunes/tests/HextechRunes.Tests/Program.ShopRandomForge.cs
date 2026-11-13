using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 七个商店随机锻造器补丁共用一次原版成员探测；任一目标缺失则整组 Prepare=false，商店不卖随机锻造器。
	[HextechTest]
	private static void RandomForgeShopHooksProbeFindsAllMerchantTargets()
	{
		object? available = AccessTools.Field(typeof(HextechShopForgeHooks), "RandomForgeShopHooksAvailable").GetValue(null);
		Equal<object?>(true, available, "random forge shop hooks probe");
	}

	// 购买替换与补货/清空跳过只作用于本模组条目，原版遗物条目照常走原方法。
	[HextechTest]
	private static void RandomForgeShopSkipPrefixesOnlyTouchOwnEntry()
	{
		MerchantRelicEntry own = CreateUninitializedRelicEntry(CreateMutableTestModel<RandomForgeShopRelic>());
		MerchantRelicEntry vanilla = CreateUninitializedRelicEntry(model: null);
		foreach (string patchName in new[] { "RestockPatch", "KeepEntryAfterPurchasePatch" })
		{
			MethodInfo prefix = AccessTools.Method(AccessTools.Inner(typeof(HextechShopForgeHooks), patchName), "Prefix");
			Equal<object?>(false, prefix.Invoke(null, [own]), $"{patchName} skips the random forge entry");
			Equal<object?>(true, prefix.Invoke(null, [vanilla]), $"{patchName} keeps vanilla entries");
		}

		MethodInfo purchase = AccessTools.Method(AccessTools.Inner(typeof(HextechShopForgeHooks), "PurchasePatch"), "Prefix");
		object?[] args = [vanilla, null, false, null];
		Equal<object?>(true, purchase.Invoke(null, args), "purchase prefix keeps vanilla entries");
		Expect(args[3] == null, "purchase prefix leaves vanilla result untouched");
	}

	private static MerchantRelicEntry CreateUninitializedRelicEntry(RelicModel? model)
	{
		MerchantRelicEntry entry = (MerchantRelicEntry)RuntimeHelpers.GetUninitializedObject(typeof(MerchantRelicEntry));
		SetAutoProperty(entry, nameof(MerchantRelicEntry.Model), model);
		return entry;
	}
}
