using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using HextechRunes;
using FormVfxKind = HextechRunes.HextechFormVfxSafetyHooks.FormVfxKind;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using System.Text.Json;

namespace HextechRunes.Tests;

internal static partial class Program
{
	private const int Magic = 0x48585452; // HXTR
	private const int ChoiceKindActRoll = 1;
	private const int ChoiceKindRuneSelection = 2;
	private const int ChoiceKindActSelectionApplied = 3;
	private const int ChoiceKindEnemyHexAdjustment = 4;
	private const int ChoiceKindForgeSelection = 5;
	private const int ChoiceKindRandomRuneGrant = 6;
	private const int ChoiceKindRelicOptionSelection = 7;
	private const int EnemyHexAdjustmentListVersion = -2;
	private const int StableModelIdListVersion = -3;

	public static int Main(string[] args)
	{
#if STS2_109_OR_NEWER
		// 0.109 起游戏引用 System.IO.Hashing(XxHash32);它不在测试的 deps.json 里(仅作文件复制),
		// 默认加载上下文按 deps.json 解析会失败,这里从输出目录兜底加载。
		System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += static (context, name) =>
		{
			string candidate = Path.Combine(AppContext.BaseDirectory, $"{name.Name}.dll");
			return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
		};

		// 0.109 起缓存并入 Multiplayer.Serialization.ModelIdSerializationCache,守卫语义同 0.108。
		typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache)
			.GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)
			?.SetValue(null, true);
#elif STS2_108_OR_NEWER
		// 0.108 起 SavedPropertiesTypeCache 未初始化即用会抛;真实游戏由启动流程 Init(),但 Init 又依赖
		// AssemblyInfo 等更多游戏启动态。测试环境直接置 _initialized 标志,恢复 0.107.1 的无守卫语义。
		typeof(MegaCrit.Sts2.Core.Saves.Runs.SavedPropertiesTypeCache)
			.GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)
			?.SetValue(null, true);
#endif
		TestCase[] tests = DiscoverTests();

		if (args.Length > 0)
		{
			foreach (string name in args)
			{
				if (tests.All(test => test.Name != name))
				{
					Console.Error.WriteLine($"Unknown test: {name}");
					return 1;
				}
			}
			tests = tests.Where(test => args.Contains(test.Name)).ToArray();
		}

		int failed = 0;
		foreach (TestCase test in tests)
		{
			try
			{
				test.Run();
				Console.WriteLine($"PASS {test.Name}");
			}
			catch (Exception ex)
			{
				failed++;
				Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
			}
		}

		Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed");
		return failed == 0 ? 0 : 1;
	}

	// v15(0.8.4 出厂)默认禁用集的冻结快照。这是历史事实,不随注册表演进——注册表每次翻转默认启停
	// 都必须新增迁移链段,链走完应恰好落在当前出厂默认上(由下方测试守护)。
	private static readonly Type[] Version15FactoryDisabledRuneTypes =
	[
		typeof(AdaptiveCapacitorRune),
		typeof(AdvanceToRetreatRune),
		typeof(AnthonyBiasRune),
		typeof(AstralBodyRune),
		typeof(CorruptedBranchRune),
		typeof(CuttingEdgeAlchemistRune),
		typeof(DawnbringersResolveRune),
		typeof(EarthAwakensRune),
		typeof(EndlessRecoveryRune),
		typeof(EscapePlanRune),
		typeof(FeelTheBurnRune),
		typeof(HappyAccidentRune),
		typeof(HardBonesRune),
		typeof(MasterOfDualityRune),
		typeof(NeowsGrudgeRune),
		typeof(OkBoomerangRune),
		typeof(PrimitiveMadnessRune),
		typeof(RegenerationSuppressionRune),
		typeof(SuperBrainRune),
		typeof(SwordFlightRune),
		typeof(WarmogsSpiritRune)
	];

	private static (object? Value, int? Count) SnapshotStaticCollection(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException($"{type.FullName}.{fieldName} should exist");
		object? value = field.GetValue(null);
		int? count = value switch
		{
			ICollection collection => collection.Count,
			null => null,
			_ => value.GetType().GetProperty("Count")?.GetValue(value) as int?
		};
		return (value, count);
	}

	private static Action CaptureStaticCollectionRestore(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException($"{type.FullName}.{fieldName} should exist");
		object value = field.GetValue(null)
			?? throw new InvalidOperationException($"{type.FullName}.{fieldName} should not be null");
		if (value is IDictionary dictionary)
		{
			// 泛型 Dictionary 的 IEnumerable 枚举器返回 KeyValuePair；通过 IDictionary
			// 的专用枚举器取 Entry，避免在实际保存守卫执行前就因测试快照类型转换失败。
			List<DictionaryEntry> entries = [];
			IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
			while (enumerator.MoveNext())
			{
				entries.Add(enumerator.Entry);
			}
			return () =>
			{
				dictionary.Clear();
				foreach (DictionaryEntry entry in entries)
				{
					dictionary.Add(entry.Key, entry.Value);
				}
			};
		}
		if (value is IList list)
		{
			object?[] items = list
				.Cast<object?>()
				.ToArray();
			return () =>
			{
				list.Clear();
				foreach (object? item in items)
				{
					list.Add(item);
				}
			};
		}

		throw new InvalidOperationException(
			$"{type.FullName}.{fieldName} is not a mutable dictionary or list");
	}

	private static void RunBeforeSavedPropertyCacheInitialization(Action action)
	{
#if STS2_109_OR_NEWER
		FieldInfo initializedField = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache)
			.GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException("0.109 SavedProperty cache initialized field should exist");
		bool originalInitialized = initializedField.GetValue(null) is true;
		try
		{
			initializedField.SetValue(null, false);
			action();
		}
		finally
		{
			initializedField.SetValue(null, originalInitialized);
		}
#else
		action();
#endif
	}

	private static MethodInfo GetAsyncStateMachineMoveNext(MethodInfo asyncMethod)
	{
		Type stateMachineType = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
			?? throw new InvalidOperationException($"{asyncMethod.DeclaringType?.Name}.{asyncMethod.Name} is not async");
		return stateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
			?? throw new MissingMethodException(stateMachineType.FullName, "MoveNext");
	}

	private static void ExpectCombatGenerationFilters(MethodBase method, string label)
	{
		List<MethodInfo> calledMethods = [];
		CollectReferencedMethods(method, calledMethods, []);
		Expect(
			calledMethods.Any(static called => called.DeclaringType == typeof(CardFactory)
				&& called.Name == nameof(CardFactory.FilterForCombat)),
			$"{label} should use CardFactory.FilterForCombat");
		Expect(
			calledMethods.Any(static called => called.DeclaringType == typeof(CardModel)
				&& called.Name == "get_CanBeGeneratedByModifiers"),
			$"{label} should reject cards that modifiers cannot generate");
	}

	private static void CollectReferencedMethods(
		MethodBase method,
		List<MethodInfo> referencedMethods,
		HashSet<MethodBase> visited)
	{
		if (!visited.Add(method))
		{
			return;
		}

		foreach (MethodInfo referenced in PatchProcessor.GetOriginalInstructions(method)
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>())
		{
			referencedMethods.Add(referenced);
			if (referenced.DeclaringType?.Assembly == typeof(BlankCheckRune).Assembly)
			{
				CollectReferencedMethods(referenced, referencedMethods, visited);
			}
		}
	}

	private static HashSet<string> GetConfigurableRuneEntries(params HextechRarityTier[] rarities)
	{
		return rarities
			.SelectMany(HextechCatalog.GetConfigurablePlayerRuneTypesForRarity)
			.Select(static type => ModelDb.GetId(type).Entry)
			.ToHashSet(StringComparer.Ordinal);
	}

	private static T UninitializedCard<T>() where T : CardModel
	{
		return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	}

	private static T CreateMutableTestModel<T>()
		where T : AbstractModel, new()
	{
		T model = new();
		SetAutoProperty(model, nameof(AbstractModel.IsMutable), true);
		return model;
	}

	// 0.109 起 CardPlay 才有 Player 成员；未设所有者的可变卡 Owner 为 null，照原样传入。
	private static CardPlay CreateCardPlay(
		CardModel card,
		int playIndex = 0,
		int playCount = 1,
		bool isAutoPlay = true,
		int energySpent = 0,
		int starsSpent = 0)
	{
		return new CardPlay
		{
			Card = card,
#if STS2_109_OR_NEWER
			Player = card.Owner,
#endif
			Target = null,
			ResultPile = PileType.Discard,
			Resources = new ResourceInfo
			{
				EnergySpent = energySpent,
				EnergyValue = energySpent,
				StarsSpent = starsSpent,
				StarValue = starsSpent
			},
			IsAutoPlay = isAutoPlay,
			PlayIndex = playIndex,
			PlayCount = playCount
		};
	}

	private static DustyTome CreateTestDustyTome()
	{
		DustyTome dustyTome = CreateMutableTestModel<DustyTome>();
		SetTestDustyTomeAncientCard(dustyTome, ModelDb.GetId<Apotheosis>());
		return dustyTome;
	}

	private static void SetTestDustyTomeAncientCard(DustyTome dustyTome, ModelId ancientCard)
	{
		typeof(DustyTome)
			.GetField("_ancientCard", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(dustyTome, (ModelId?)ancientCard);
	}

	private sealed class ExternalRegistrationTestRune : HextechRelicBase
	{
	}

	private sealed class ExternalRegistrationEventRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private sealed class ExternalRegistrationForge : HextechForgeBase
	{
	}

	private sealed class BurningBlood : HextechRelicBase
	{
	}

	private sealed class Anchor : HextechForgeBase
	{
	}

	private sealed class ExternalRegistrationEnchantment : EnchantmentModel
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int PersistentCounter { get; set; } = 7;
	}

	private sealed class PreInitSavedPropertyCarrier : EnchantmentModel
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int PreInitCounter { get; set; } = 3;
	}

	private sealed class LateSavedPropertyCarrier : EnchantmentModel
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int LateCounter { get; set; } = 5;
	}

	private sealed class SameNameSavedPropertyCarrierA : EnchantmentModel
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int SharedCounter { get; set; } = 1;
	}

	private sealed class SameNameSavedPropertyCarrierB : EnchantmentModel
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int SharedCounter { get; set; } = 2;
	}

	private sealed class LateExternalRegistrationRune : HextechRelicBase
	{
		[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
		private int LateExternalCounter { get; set; } = 1;
	}

	private sealed class RuneSelectionTestRelicA : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private sealed class RuneSelectionTestRelicB : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private sealed class RuneSelectionTestRelicC : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Event;
	}

	private static (HextechRarityTier Rarity, IReadOnlyList<MonsterHexKind> Pool) GetMonsterHexPoolWithMinimum(int minimumCount)
	{
		foreach (HextechRarityTier rarity in Enum.GetValues<HextechRarityTier>())
		{
			IReadOnlyList<MonsterHexKind> pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity);
			if (pool.Count >= minimumCount)
			{
				return (rarity, pool);
			}
		}

		throw new InvalidOperationException($"no monster hex rarity pool has at least {minimumCount} entries");
	}

	private static RelicModel[] CreateRuneSelectionTestOptions(int count)
	{
		RelicModel[] options =
		[
			new RuneSelectionTestRelicA(),
			new RuneSelectionTestRelicB(),
			new RuneSelectionTestRelicC()
		];
		return options.Take(count).ToArray();
	}

	private static ModelId TestMonsterHexIconId(MonsterHexKind kind)
	{
		return new ModelId("HEXTECH_TEST", $"MONSTER_HEX_{(int)kind}");
	}

	// Player 的构造函数会触达 SaveManager/PlatformUtil 等只在真实 Godot 运行时下可用的原生绑定,
	// 纯 CLI 测试进程里直接 new 会段错误。这里只需要一个能承载稳定 NetId 的壳子来复用
	// GetPlayerRuneProcKey 的联机计费键,故跳过构造函数,直接反射写入 NetId 的自动属性支持字段。
	private static Player CreateOrdinalTestPlayer(ulong netId)
	{
		Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
		SetAutoProperty(player, nameof(Player.NetId), netId);
		return player;
	}

	private static void Expect([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static TException ExpectThrows<TException>(Action action, string message)
		where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException ex)
		{
			return ex;
		}

		throw new InvalidOperationException(message);
	}

	private static void Equal<T>(T expected, T actual, string label)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
		}
	}

	private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
	{
		T[] expectedArray = expected.ToArray();
		T[] actualArray = actual.ToArray();
		if (!expectedArray.SequenceEqual(actualArray))
		{
			throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expectedArray)}], got [{string.Join(", ", actualArray)}]");
		}
	}

	private static void SetEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string label)
	{
		HashSet<T> expectedSet = expected.ToHashSet();
		HashSet<T> actualSet = actual.ToHashSet();
		if (!expectedSet.SetEquals(actualSet))
		{
			throw new InvalidOperationException($"{label}: expected [{string.Join(", ", expectedSet)}], got [{string.Join(", ", actualSet)}]");
		}
	}

	private static bool IsModuloStepCycle(IReadOnlyList<int> values, int modulo)
	{
		if (values.Count < 3)
		{
			return false;
		}

		int step = PositiveModulo(values[1] - values[0], modulo);
		for (int i = 2; i < values.Count; i++)
		{
			if (PositiveModulo(values[i] - values[i - 1], modulo) != step)
			{
				return false;
			}
		}

		return true;
	}

	private static int PositiveModulo(int value, int modulo)
	{
		int result = value % modulo;
		return result < 0 ? result + modulo : result;
	}

	private readonly record struct TestCase(string Name, Action Run);

	/// <summary>
	/// 收集 <see cref="Program"/> 上所有标了 <see cref="HextechTestAttribute"/> 的方法，按名称 Ordinal 排序，
	/// 保证每次运行顺序一致、与源文件编译顺序无关。标在别的类型上或签名不对都直接报错，不会被静默漏掉。
	/// </summary>
	private static TestCase[] DiscoverTests()
	{
		const BindingFlags allDeclared = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		List<TestCase> tests = [];
		foreach (Type type in typeof(Program).Assembly.GetTypes())
		{
			foreach (MethodInfo method in type.GetMethods(allDeclared))
			{
				if (!method.IsDefined(typeof(HextechTestAttribute), inherit: false))
				{
					continue;
				}

				if (type != typeof(Program)
					|| !method.IsStatic
					|| method.IsGenericMethodDefinition
					|| method.ReturnType != typeof(void)
					|| method.GetParameters().Length != 0)
				{
					throw new InvalidOperationException($"[HextechTest] {type.FullName}.{method.Name} must be a parameterless static void method on Program");
				}

				tests.Add(new TestCase(method.Name, method.CreateDelegate<Action>()));
			}
		}

		tests.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
		return tests.ToArray();
	}
}

/// <summary>标记一个测试用例：<c>Program</c> 上无参 static void 方法，方法名即测试名（命令行筛选与 hextech_dev.py 都按它）。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
internal sealed class HextechTestAttribute : Attribute
{
}
