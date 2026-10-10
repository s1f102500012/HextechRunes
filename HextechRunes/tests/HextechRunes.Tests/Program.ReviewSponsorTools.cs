using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using HextechRunesSponsorPack;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MainLoader = HextechRunes.Loader.LoaderBootstrap;
using SponsorLoader = HextechRunesSponsorPack.Loader.LoaderBootstrap;

namespace HextechRunes.Tests;

// 2026-09 审查整理(拓展包与工具链):共享加载器、拓展包 SavedProperty 快照、稳定哈希 golden 值、
// 锻造器售价登记点与拓展包补丁声明。
internal static partial class Program
{
	[HextechTest]
	private static void SponsorSavedPropertyManifestMatchesCheckedInList()
	{
		ExpectSavedPropertyManifest(
			"sponsor_saved_property_manifest.txt",
			CollectSavedPropertyNames(typeof(SponsorCatalog).Assembly, declaredInAssemblyOnly: true));
	}

	// 宿主已知却没有不高于它的变体时必须返回 null(停止加载),不能回退到更新的变体;宿主未知才用最新变体。
	// 拓展包加载器以链接方式编译同一份源码,选择规则只在这里测一次。
	[HextechTest]
	private static void LoaderSelectVariantNeverFallsBackToNewerVariant()
	{
		MainLoader.VariantCandidate[] main =
		[
			new("0.107.1", new Version(0, 107, 1), "a"),
			new("0.110.0", new Version(0, 110, 0), "b"),
			new("0.111.0", new Version(0, 111, 0), "c")
		];
		Equal("0.107.1", MainLoader.SelectVariant(main, new Version(0, 107, 1))?.CompatTarget, "exact oldest host");
		Equal("0.107.1", MainLoader.SelectVariant(main, new Version(0, 109, 0))?.CompatTarget, "between targets picks the older one");
		Equal("0.111.0", MainLoader.SelectVariant(main, new Version(0, 112, 0))?.CompatTarget, "newer host picks the newest variant");
		Equal("0.111.0", MainLoader.SelectVariant(main, null)?.CompatTarget, "unknown host picks the newest variant");
		Expect(MainLoader.SelectVariant(main, new Version(0, 106, 0)) == null, "host older than every variant refuses to load");
		Expect(MainLoader.SelectVariant([], null) == null, "no variants");
	}

	// 智能应用控制拦截时 LoadFromAssemblyPath 抛带 0x800711C7 的 FileLoadException;
	// 初始化器经反射调用时还会被 TargetInvocationException 包一层,两种都要认出来。
	[HextechTest]
	private static void LoaderRecognizesApplicationControlBlock()
	{
		var blocked = new FileLoadException("blocked") { HResult = unchecked((int)0x800711C7) };
		Equal(MainLoader.LoadFailureKind.ApplicationControlBlocked, MainLoader.ClassifyLoadFailure(blocked), "direct block");
		Equal(MainLoader.LoadFailureKind.ApplicationControlBlocked, MainLoader.ClassifyLoadFailure(new TargetInvocationException(blocked)), "wrapped block");
		Equal(MainLoader.LoadFailureKind.Other, MainLoader.ClassifyLoadFailure(new FileLoadException("other")), "other load failure");
	}

	// 弹窗文字写死在加载器里:每种语言都要有文案,模组名与各语言 main_menu_ui 里的名称一致;
	// 没覆盖的语言回退英文,版本过旧的正文带上游戏版本。
	[HextechTest]
	private static void LoaderFailureNoticeCoversModLanguages()
	{
		string repoRoot = Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "..", "..", ".."));
		foreach (string language in ModListLocLanguages)
		{
			string mainName = ReadMainMenuString(Path.Combine(repoRoot, "HextechRunes", "assets"), language, "HEXTECH_MOD_NAME");
			string sponsorName = ReadMainMenuString(Path.Combine(repoRoot, "HextechRunesSponsorPack", "assets"), language, "HEXTECH_SPONSOR_MOD_NAME");
			Expect(MainLoader.BuildFailureNotice(MainLoader.LoadFailureKind.Other, language, null).Title.Contains(mainName), $"main notice title for {language} uses {mainName}");
			Expect(SponsorLoader.BuildFailureNotice(SponsorLoader.LoadFailureKind.RequiredModMissing, language, null).Title.Contains(sponsorName), $"sponsor notice title for {language} uses {sponsorName}");
		}

		Equal(MainLoader.BuildFailureNotice(MainLoader.LoadFailureKind.Other, "eng", null), MainLoader.BuildFailureNotice(MainLoader.LoadFailureKind.Other, "deu", null), "unlisted language falls back to English");
		Expect(MainLoader.BuildFailureNotice(MainLoader.LoadFailureKind.UnsupportedGameVersion, "zhs", "v0.106.0").Body.Contains("v0.106.0"), "game version appears in the too-old notice");
	}

	private static string ReadMainMenuString(string assetsRoot, string language, string key)
	{
		string path = Path.Combine(assetsRoot, "localization", language, "main_menu_ui.json");
		Dictionary<string, string> table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
			?? throw new InvalidOperationException($"{path} is not a string table");
		return table[key];
	}

	// 拓展包加载器只认自己的变体清单名与程序集名。
	// 只走不写日志的成功/拒绝路径:加载器的 Log.Error 在测试进程里会触碰 Godot 原生层。
	[HextechTest]
	private static void SponsorLoaderReadsItsOwnVariantManifest()
	{
		string root = Path.Combine(Path.GetTempPath(), "hextech-sponsor-loader-test-" + Guid.NewGuid().ToString("N"));
		try
		{
			string libRoot = Path.Combine(root, "lib");
			string variantDirectory = Path.Combine(libRoot, "0.110.0");
			Directory.CreateDirectory(variantDirectory);
			File.WriteAllText(Path.Combine(variantDirectory, "compat-target.txt"), "0.110.0");
			byte[] dll = "not a real assembly"u8.ToArray();
			File.WriteAllBytes(Path.Combine(variantDirectory, "HextechRunesSponsorPack.dll"), dll);
			string manifest = JsonSerializer.Serialize(new
			{
				variants = new[] { new { compatTarget = "0.110.0", directory = "lib/0.110.0", assembly = "HextechRunesSponsorPack.dll", sha256 = Convert.ToHexString(SHA256.HashData(dll)) } }
			});

			File.WriteAllText(Path.Combine(root, "hextech-runes-sponsor-pack-variants.manifest"), manifest);
			Equal("0.110.0", SponsorLoader.PickVariant(root, libRoot, new Version(0, 111, 0))?.CompatTarget, "sponsor loader reads its own manifest");
			Expect(SponsorLoader.PickVariant(root, libRoot, new Version(0, 107, 1)) == null, "known older host: sponsor loader stops instead of loading 0.110.0");
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	// 稳定哈希的历史结果必须逐位不变(神迹事件、附魔大师的存档与联机两端都依赖它)。golden 值由已删除的
	// 拓展包 SponsorStableRandom(0.9.x 起在用)实际算出并核对过,算法:FNV-1a(64) over seed|act:|floor:|("|" + salt)... + MurmurHash3 终混。
	[HextechTest]
	private static void StableIndexMatchesGoldenValuesAndRejectsEmptyPool()
	{
		foreach (int count in new[] { 7, 100, int.MaxValue })
		{
			Equal(
				(int)(0x4A7ED4341511E801UL % (ulong)count),
				HextechStableRandom.IndexFromRawParts(count, "SEED-1", "|act:", "2", "|floor:", "17", "|", "enchantment-master", "|", "3", "|", "card"),
				$"golden raw hash (act 2, floor 17, count {count})");
		}

		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		FieldInfo history = AccessTools.Field(typeof(RunState), "_mapPointHistory");
		history.SetValue(run, Activator.CreateInstance(history.FieldType));
		AccessTools.Property(typeof(RunState), "Rng").SetValue(run, new RunRngSet("SEED-1"));
		Equal(0, run.CurrentActIndex, "fixture act");
		Equal(0, run.TotalFloor, "fixture floor");

		(ulong Hash, string?[] Salt)[] golden =
		[
			(0x386496AA02236625UL, ["miracle.gift", "2", "1"]),
			(0xAD1E1F354E18B659UL, ["miracle.forge.rarity", "forge:3:0"]),
			(0x09BB89CC8D0D0A94UL, ["enchantment-master", "76561198000000000", "enchant"]),
			(0x1ABA9BA114268ADAUL, ["miracle.cardpack", "2", null])
		];
		foreach ((ulong hash, string?[] salt) in golden)
		{
			string label = string.Join("|", salt.Select(static part => part ?? "<null>"));
			foreach (int count in new[] { 1, 7, 100 })
			{
				int expected = (int)(hash % (ulong)count);
				Equal(expected, HextechRunesApi.StableIndex(run, count, salt), $"public StableIndex golden ({label}, count {count})");
				Equal(expected, HextechStableRandom.Index(run, count, salt), $"HextechStableRandom golden ({label}, count {count})");
			}
		}

		ExpectThrows<ArgumentOutOfRangeException>(() => HextechRunesApi.StableIndex(run, 0, "empty"), "an empty pool is rejected instead of dividing by zero");
	}

	// 信徒的售价修正:叠加所有玩家的信徒,结果不低于 0;没有登记修正器时本体算价原样返回。
	[HextechTest]
	private static void BelieverForgePriceModifierSumsDeltasAndClampsAtZero()
	{
		(HextechEnemyHexContext _, Player first, Player second) = CreatePrismaticEnemyFixture();
		RunState run = (RunState)first.RunState;
		BelieverRune firstBeliever = CreateMutableTestModel<BelieverRune>();
		BelieverRune secondBeliever = CreateMutableTestModel<BelieverRune>();
		firstBeliever.SavedForgePriceDelta = 50;
		secondBeliever.SavedForgePriceDelta = -25;
		AccessTools.Field(typeof(Player), "_relics").SetValue(first, new List<RelicModel> { firstBeliever });
		AccessTools.Field(typeof(Player), "_relics").SetValue(second, new List<RelicModel> { secondBeliever });

		Equal(125, BelieverRune.ApplyForgePriceDeltas(run, 100), "deltas from every player's Believer are summed");
		secondBeliever.SavedForgePriceDelta = -500;
		Equal(0, BelieverRune.ApplyForgePriceDeltas(run, 100), "the modified price never goes below zero");
		firstBeliever.SavedForgePriceDelta = 0;
		secondBeliever.SavedForgePriceDelta = 0;
		Equal(100, BelieverRune.ApplyForgePriceDeltas(run, 100), "no delta keeps the base price");

		Equal(80, HextechRunesApi.ApplyForgeShopPriceModifiers(run, 80), "no registered modifier: base price unchanged");
	}

	// 拓展包补丁的声明约束(SponsorPatcher 是本体 HextechPatcher 的独立实现,本体那份是 internal):
	// 每个 [HarmonyPatch] 类都带唯一 id 的 [SponsorPatch];跳过型前缀(返回 bool)必须 Priority.Low 或更低。
	[HextechTest]
	private static void SponsorPatchDeclarationsAreCompleteAndSkipPrefixesYield()
	{
		List<string> problems = [];
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (Type type in typeof(SponsorCatalog).Assembly.GetTypes())
		{
			SponsorPatchAttribute? meta = type.GetCustomAttribute<SponsorPatchAttribute>();
			bool hasHarmonyTarget = HarmonyMethodExtensions.GetFromType(type).Count > 0;
			if (meta == null)
			{
				if (hasHarmonyTarget)
				{
					problems.Add($"{type.FullName}: [HarmonyPatch] without [SponsorPatch]");
				}

				continue;
			}

			if (!ids.Add(meta.Id))
			{
				problems.Add($"{type.FullName}: duplicate patch id {meta.Id}");
			}

			if (!hasHarmonyTarget)
			{
				problems.Add($"{type.FullName} ({meta.Id}): no [HarmonyPatch] target");
			}

			foreach (MethodInfo method in type.GetMethods(PatchMemberFlags))
			{
				if (method.GetCustomAttribute<HarmonyPrefix>() == null || method.ReturnType != typeof(bool))
				{
					continue;
				}

				int priority = method.GetCustomAttribute<HarmonyPriority>()?.info.priority ?? Priority.Normal;
				if (priority > Priority.Low)
				{
					problems.Add($"{type.FullName}.{method.Name} ({meta.Id}): skip prefix must use Priority.Low or lower, got {priority}");
				}
			}
		}

		Expect(ids.Count > 0, "sponsor pack declares patches");
		Expect(problems.Count == 0, "sponsor patch declaration problems:\n  " + string.Join("\n  ", problems));
	}
}
