using System.Reflection;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

// 外部敌方海克斯（HextechRunesInterop.RegisterEnemyHex，ApiVersion 3）：身份、存档/联机编码、抽取池与通用效果。
// 成功登记走内部注册表（测试进程不能往原版模型池里加东西），每个测试结束时还原注册表并递增版本，不污染其他测试。
internal static partial class Program
{
	[HextechTest]
	private static void InteropEnemyHexRegistrationValidatesBeforeSideEffects()
	{
		Action[] restore =
		[
			SuppressCompatibilityWarnings("external-content.built-in-rejected")
		];
		try
		{
			int registryVersion = HextechExternalContentRegistry.Version;
			int enemyHexCount = HextechExternalContentRegistry.GetEnemyHexRegistrations().Count;

			Equal(3, HextechRunesInterop.ApiVersion, "enemy hex registration raises the contract version");
			Equal(EnemyHexFlags.Disabled, InvokeParseEnemyHexFlags(" disabled "), "flag names are case-insensitive");
			Equal(EnemyHexFlags.None, InvokeParseEnemyHexFlags(null), "null flags are None");

			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(CompatibilityEnemyHexAlphaRelic), rarity: "Bronze"),
				"unknown rarity name");
			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(CompatibilityEnemyHexAlphaRelic), rarity: "0"),
				"numeric rarity must not bind to an internal ordinal");
			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(CompatibilityEnemyHexAlphaRelic), flags: "Disabled,FirstActExcluded"),
				"only Disabled is accepted");
			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(string)),
				"display carrier must be a RelicModel");
			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(CompatibilityEnemyHexAlphaRelic), powerType: typeof(CompatibilityEnemyHexAlphaRelic)),
				"power type must be a PowerModel");
			ExpectThrows<ArgumentException>(
				() => RegisterInteropTestEnemyHex(typeof(RelicModel)),
				"abstract display carrier");
			ExpectThrows<ArgumentOutOfRangeException>(
				() => HextechRunesApi.RegisterEnemyHex(
					typeof(CompatibilityEnemyHexAlphaRelic),
					typeof(ThornsPower),
					HextechRarityTier.Silver,
					1,
					2,
					3,
					(EnemyHexFlags)8),
				"typed API rejects unknown flag bits");
			ExpectThrows<InvalidOperationException>(
				() => RegisterInteropTestEnemyHex(typeof(Thornmail)),
				"a config id equal to a built-in enum name (ignoring case) is refused");

			// 内置展示遗物只告警不抛出，也不登记。
			RegisterInteropTestEnemyHex(typeof(EntomancerHex));

			Equal(registryVersion, HextechExternalContentRegistry.Version, "registry version after rejected registrations");
			Equal(enemyHexCount, HextechExternalContentRegistry.GetEnemyHexRegistrations().Count, "enemy hex count after rejected registrations");
			Expect(
				!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(typeof(SharedRelicPool), typeof(CompatibilityEnemyHexAlphaRelic)),
				"rejected registration must not queue the display relic");
			Expect(
				!HextechModelPoolRegistrar.IsModelAlreadyQueuedForPool(typeof(SharedRelicPool), typeof(Thornmail)),
				"identity conflict is detected before the pool registration");
		}
		finally
		{
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	[HextechTest]
	private static void ExternalEnemyHexIdentityIsStableReservedAndCollisionChecked()
	{
		Equal(1986234385, (int)ExternalEnemyHexIdentity.ComputeKind("MY_HEX"), "FNV-1a low 30 bits | 0x40000000 never changes");
		Expect(ExternalEnemyHexIdentity.IsReserved(ExternalEnemyHexIdentity.ComputeKind("MY_HEX")), "external kinds sit in the reserved range");
		Expect(
			Enum.GetValues<MonsterHexKind>().All(static kind => !ExternalEnemyHexIdentity.IsReserved(kind)),
			"built-in enum values never reach the reserved range");

		// COLLIDE_90858 与 COLLIDE_318204 的哈希低 30 位相同。
		Equal(
			ExternalEnemyHexIdentity.ComputeKind("COLLIDE_90858"),
			ExternalEnemyHexIdentity.ComputeKind("COLLIDE_318204"),
			"fixture: the two entries collide");
		Dictionary<Type, string> entries = new()
		{
			[typeof(CompatibilityEnemyHexAlphaRelic)] = "COLLIDE_318204",
			[typeof(CompatibilityEnemyHexBetaRelic)] = "COLLIDE_90858"
		};
		ModelId GetId(Type type) => new("RELIC", entries[type]);
		ExternalEnemyHexRegistration alpha = CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic));
		ExternalEnemyHexRegistration beta = CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexBetaRelic));

		ExternalEnemyHexIdentity.EnsureAvailable(typeof(CompatibilityEnemyHexAlphaRelic), HextechMonsterHexRegistry.Registrations, [ alpha ], GetId);
		ExpectThrows<InvalidOperationException>(
			() => ExternalEnemyHexIdentity.EnsureAvailable(typeof(CompatibilityEnemyHexBetaRelic), HextechMonsterHexRegistry.Registrations, [ alpha ], GetId),
			"two external entries with the same kind are refused at registration");

		List<string> conflicts = [];
		IReadOnlyList<ResolvedExternalEnemyHex> resolved = ExternalEnemyHexIdentity.Resolve(
			HextechMonsterHexRegistry.Registrations,
			[ beta, alpha ],
			GetId,
			conflicts.Add);
		Equal(1, resolved.Count, "a late collision keeps one entry");
		Equal("COLLIDE_318204", resolved[0].ConfigId, "the survivor is chosen by sorting, not by registration order");
		Equal(1, conflicts.Count, "the dropped entry is reported");
		Equal(
			"COLLIDE_318204",
			ExternalEnemyHexIdentity.Resolve(HextechMonsterHexRegistry.Registrations, [ alpha, beta ], GetId, null).Single().ConfigId,
			"the same survivor for the other registration order");
	}

	[HextechTest]
	private static void ExternalEnemyHexMetadataOrderAndConfigIdsIgnoreRegistrationOrder()
	{
		MonsterHexKind alpha = default, beta = default;
		IReadOnlyList<MonsterHexKind> firstOrder = [], secondOrder = [];
		IReadOnlyList<MonsterHexKind> firstEffects = [], secondEffects = [];
		using IDisposable injected = InjectMissingModels(typeof(CompatibilityEnemyHexAlphaRelic), typeof(CompatibilityEnemyHexBetaRelic));
		MonsterHexKind[] builtInEffectOrder = OrderedEnemyHexEffects().Select(static effect => effect.Kind).ToArray();
		foreach (bool alphaFirst in new[] { true, false })
		{
			WithTestEnemyHexes(
				alphaFirst
					? [ CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic)), CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexBetaRelic)) ]
					: [ CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexBetaRelic)), CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic)) ],
				() =>
				{
					alpha = TestEnemyHexKind(typeof(CompatibilityEnemyHexAlphaRelic));
					beta = TestEnemyHexKind(typeof(CompatibilityEnemyHexBetaRelic));
					IReadOnlyList<MonsterHexKind> silver = MonsterHexCatalog.GetMonsterHexesForRarity(HextechRarityTier.Silver);
					IReadOnlyList<MonsterHexKind> effects = OrderedEnemyHexEffects().Select(static effect => effect.Kind).ToArray();
					if (alphaFirst)
					{
						firstOrder = silver;
						firstEffects = effects;
					}
					else
					{
						secondOrder = silver;
						secondEffects = effects;
					}

					Equal(
						HextechMonsterHexRegistry.Registrations.Count + 2,
						effects.Count,
						"every registration gets exactly one effect");
					Equal(typeof(ExternalPowerEnemyHex), OrderedEnemyHexEffects().Single(effect => effect.Kind == alpha).GetType(), "external entries use the generic effect");
					SequenceEqual(
						builtInEffectOrder,
						effects.Take(builtInEffectOrder.Length),
						"built-in dispatch order is unchanged");
					Equal(HextechRarityTier.Silver, MonsterHexCatalog.GetMonsterHexRarity(alpha), "external rarity");

					string alphaId = ModelDb.GetId(typeof(CompatibilityEnemyHexAlphaRelic)).Entry;
					// 注入 ModelDb 后不能再直接构造模型，取规范模型。
					RelicModel alphaRelic = ModelDb.GetById<RelicModel>(ModelDb.GetId(typeof(CompatibilityEnemyHexAlphaRelic)));
					Equal(alphaId, MonsterHexCatalog.GetConfigId(alpha), "external config id is the display relic entry");
					Expect(MonsterHexCatalog.TryGetMonsterHexByConfigId(alphaId, out MonsterHexKind parsed) && parsed == alpha, "config id round trip");
					Equal("Thornmail", MonsterHexCatalog.GetConfigId(MonsterHexKind.Thornmail), "built-in config id is the enum name");
					Expect(MonsterHexCatalog.TryGetMonsterHexByConfigId("Thornmail", out parsed) && parsed == MonsterHexKind.Thornmail, "built-in round trip");
					Expect(!MonsterHexCatalog.TryGetMonsterHexByConfigId("thornmail", out _), "config ids stay case-sensitive like the old Enum.TryParse");
					Expect(MonsterHexCatalog.TryGetRegisteredMonsterHex((int)alpha, out _), "registered external value");
					SetEqual([ typeof(ThornsPower) ], MonsterHexCatalog.GetEnemyHexPowerHoverTipTypes(alpha), "hover tips add the registered power");
					Expect(HextechCatalog.IsHextechEnemyHexIconRelic(alphaRelic), "display relic is registry-owned (kept out of natural pools)");
					Equal(
						"res://Compatibility.EnemyHex/images/relics/" + HextechAssets.ToImageFileStem(alphaId) + ".png",
						HextechAssets.TryGetCustomRelicIconPath(alphaRelic),
						"icon path follows the asset owner like external runes");
				});
		}

		SequenceEqual(firstOrder, secondOrder, "rarity pool order does not depend on registration order");
		SequenceEqual(firstEffects, secondEffects, "effect dispatch order does not depend on registration order");
		Expect(
			firstOrder.SkipWhile(static kind => !ExternalEnemyHexIdentity.IsReserved(kind)).All(ExternalEnemyHexIdentity.IsReserved),
			"external entries come after every built-in entry");
		Expect(!MonsterHexCatalog.TryGetRegisteredMonsterHex((int)alpha, out _), "after unregistering, the value is unknown again");
		Expect(!MonsterHexCatalog.TryGetMonsterHexByConfigId(ModelDb.GetId(typeof(CompatibilityEnemyHexAlphaRelic)).Entry, out _), "after unregistering, the config id is unknown again");
	}

	[HextechTest]
	private static void ExternalEnemyHexSaveAndProtocolRoundTrip()
	{
		HextechMayhemActState unloaded = new();
		int[] savedCarried = [];
		string savedByAct = "";
		WithTestEnemyHexes(
			[ CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic)) ],
			() =>
			{
				MonsterHexKind external = TestEnemyHexKind(typeof(CompatibilityEnemyHexAlphaRelic));
				int unknownReserved = (int)ExternalEnemyHexIdentity.ComputeKind("NOT_REGISTERED");

				HextechMayhemActState state = new();
				state.SavedCarriedMonsterHexes = [ (int)MonsterHexKind.Thornmail, (int)external, unknownReserved, 9999 ];
				SequenceEqual(new[] { (int)MonsterHexKind.Thornmail, (int)external }, state.SavedCarriedMonsterHexes, "save keeps registered values and drops unknown ones");
				state.SavedMonsterHexesByActJson = $"[[{(int)MonsterHexKind.Thornmail},{(int)external},{unknownReserved}]]";
				savedCarried = state.SavedCarriedMonsterHexes;
				savedByAct = state.SavedMonsterHexesByActJson;
				Expect(savedByAct.Contains(((int)external).ToString(), StringComparison.Ordinal), "per-act save keeps the external value");
				Expect(!savedByAct.Contains(unknownReserved.ToString(), StringComparison.Ordinal), "per-act save drops unknown values");

				PlayerChoiceResult actRoll = HextechChoiceCodec.CreateActRoll(
					actIndex: 0,
					rarity: HextechRarityTier.Silver,
					monsterHex: external,
					hostUsesBetterMultiplayerScaling: false,
					enemyHexCountsByAct: [ 1, 1, 1 ],
					disabledPlayerRuneIds: new HashSet<string>(),
					runConfigurationSnapshot: HextechRuneConfiguration.GetDefaultSnapshot() with
					{
						DisabledMonsterHexIds = [ MonsterHexCatalog.GetConfigId(external), MonsterHexKind.FrostWraith.ToString() ]
					});
				Expect(HextechChoiceCodec.TryGetIndexPayload(actRoll, out List<int> actRollPayload) && actRollPayload[4] == (int)external, "act roll carries the full int");
				Expect(HextechChoiceCodec.TryDecodeActRoll(
					actRoll,
					expectedActIndex: 0,
					out _,
					out MonsterHexKind? decodedHex,
					out _,
					out _,
					out _,
					out HextechRunConfigurationSnapshot decodedSnapshot), "act roll with an external hex decodes");
				Equal<MonsterHexKind?>(external, decodedHex, "act roll external hex");
				SetEqual(
					[ MonsterHexCatalog.GetConfigId(external), MonsterHexKind.FrostWraith.ToString() ],
					decodedSnapshot.DisabledMonsterHexIds,
					"disabled external config id survives the config snapshot");

				PlayerChoiceResult adjustment = HextechChoiceCodec.CreateEnemyHexAdjustment(
					77,
					new EnemyHexAdjustmentPayload(0, 3, [ external, null, MonsterHexKind.Thornmail ], [ 1, 0, 2 ], IsFinal: true));
				Expect(HextechChoiceCodec.TryDecodeEnemyHexAdjustment(adjustment, 77, 0, 3, out EnemyHexAdjustmentPayload decodedAdjustment), "enemy adjustment with an external hex decodes");
				SequenceEqual(new MonsterHexKind?[] { external, null, MonsterHexKind.Thornmail }, decodedAdjustment.MonsterHexes, "enemy adjustment hexes");

				PlayerChoiceResult unknownAdjustment = HextechChoiceCodec.CreateEnemyHexAdjustment(
					77,
					new EnemyHexAdjustmentPayload(0, 3, [ (MonsterHexKind)unknownReserved ], [ 0 ], IsFinal: true));
				Expect(!HextechChoiceCodec.TryDecodeEnemyHexAdjustment(unknownAdjustment, 77, 0, 3, out _), "an unknown value from the peer is rejected like before");
			});

		// 模组卸载后读档：外部数值不在注册表里，照现有逻辑丢弃，内置条目照常读回。
		unloaded.SavedCarriedMonsterHexes = savedCarried;
		SequenceEqual(new[] { (int)MonsterHexKind.Thornmail }, unloaded.SavedCarriedMonsterHexes, "uninstalled external hex is dropped on load");
		unloaded.SavedMonsterHexesByActJson = savedByAct;
		Expect(!unloaded.SavedMonsterHexesByActJson.Contains(((int)savedCarried[1]).ToString(), StringComparison.Ordinal), "uninstalled external hex is dropped from the per-act save");
	}

	[HextechTest]
	private static void ExternalEnemyHexPoolHonorsConfigAvailabilityAndStarterRule()
	{
		Action[] restore =
		[
			SuppressCompatibilityWarnings(
				"external-content.enemy-hex-availability-failed",
				"external-content.non-starter-enemy-hex")
		];
		IDisposable injected = InjectMissingModels(
			typeof(CompatibilityEnemyHexAlphaRelic),
			typeof(CompatibilityEnemyHexBetaRelic),
			typeof(CompatibilityEnemyHexGammaRelic),
			typeof(CompatibilityEnemyHexCommonRelic));
		try
		{
			RunState run = CreatePrismaticEnemyFixture().Context.RunState;
			bool betaAllowed = false;
			int betaCalls = 0;
			WithTestEnemyHexes(
				[
					CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic)),
					CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexBetaRelic)),
					CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexGammaRelic)),
					CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexCommonRelic))
				],
				() =>
				{
					MonsterHexKind alpha = TestEnemyHexKind(typeof(CompatibilityEnemyHexAlphaRelic));
					MonsterHexKind beta = TestEnemyHexKind(typeof(CompatibilityEnemyHexBetaRelic));
					MonsterHexKind gamma = TestEnemyHexKind(typeof(CompatibilityEnemyHexGammaRelic));
					MonsterHexKind common = TestEnemyHexKind(typeof(CompatibilityEnemyHexCommonRelic));

					IReadOnlyList<MonsterHexKind> pool = HextechMonsterHexRoller.GetConfiguredRarityPool(HextechRarityTier.Silver, null, networkMultiplayer: false, run);
					Expect(pool.Contains(alpha), "external entry without a predicate is drawable");
					Expect(!pool.Contains(beta), "predicate returning false excludes the entry");
					Expect(!pool.Contains(gamma), "throwing predicate excludes the entry");
					Expect(!pool.Contains(common), "non-Starter display relic is never drawn");
					Expect(betaCalls > 0, "predicate sees the run state");
					SequenceEqual(
						MonsterHexCatalog.GetMonsterHexesForRarity(HextechRarityTier.Silver).Where(kind => !ExternalEnemyHexIdentity.IsReserved(kind)),
						pool.Where(kind => !ExternalEnemyHexIdentity.IsReserved(kind)),
						"built-in pool is unchanged");

					betaAllowed = true;
					Expect(HextechMonsterHexRoller.GetConfiguredRarityPool(HextechRarityTier.Silver, null, networkMultiplayer: false, run).Contains(beta), "predicate is re-evaluated");

					IReadOnlySet<string> disabled = new HashSet<string>(StringComparer.Ordinal) { MonsterHexCatalog.GetConfigId(alpha) };
					Expect(!HextechMonsterHexRoller.GetConfiguredRarityPool(HextechRarityTier.Silver, disabled, networkMultiplayer: false, run).Contains(alpha), "config id disables the external entry");
					Expect(!HextechMonsterHexRoller.BuildActPool(HextechRarityTier.Silver, null, null, disabled, run).Contains(alpha), "act draws use the same filter");

					SetEqual(
						HextechRuneConfiguration.NormalizeDisabledMonsterHexIds([ MonsterHexCatalog.GetConfigId(alpha), "SOME_UNINSTALLED_HEX" ]),
						[ MonsterHexCatalog.GetConfigId(alpha), "SOME_UNINSTALLED_HEX" ],
						"config keeps ids of entries that are not registered right now, like player runes");
				},
				availability: new Dictionary<Type, Func<IRunState, bool>>
				{
					[typeof(CompatibilityEnemyHexBetaRelic)] = state =>
					{
						betaCalls++;
						return ReferenceEquals(state, run) && betaAllowed;
					},
					[typeof(CompatibilityEnemyHexGammaRelic)] = static _ => throw new InvalidOperationException("predicate failure")
				});
		}
		finally
		{
			injected.Dispose();
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	[HextechTest]
	private static void ExternalEnemyHexDisabledFlagOnlyChangesNewConfigDefaults()
	{
		WithTestEnemyHexes(
			[
				CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic), EnemyHexFlags.Disabled),
				CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexBetaRelic))
			],
			() =>
			{
				IReadOnlySet<string> defaults = HextechRuneConfiguration.GetDefaultDisabledMonsterHexIds();
				Expect(defaults.Contains(MonsterHexCatalog.GetConfigId(TestEnemyHexKind(typeof(CompatibilityEnemyHexAlphaRelic)))), "Disabled entries are off in new configs");
				Expect(!defaults.Contains(MonsterHexCatalog.GetConfigId(TestEnemyHexKind(typeof(CompatibilityEnemyHexBetaRelic)))), "other entries stay on");
				Expect(defaults.Contains(MonsterHexKind.StartupRoutine.ToString()) && defaults.Contains(MonsterHexKind.HastyScribble.ToString()), "built-in defaults are kept");
			});
		SetEqual(
			new[] { MonsterHexKind.StartupRoutine.ToString(), MonsterHexKind.HastyScribble.ToString(), MonsterHexKind.Mystery.ToString() },
			HextechRuneConfiguration.GetDefaultDisabledMonsterHexIds(),
			"built-in defaults alone once the entry is gone");
	}

	[HextechTest]
	private static void ExternalPowerEnemyHexAppliesOncePerEnemyByStrengthTier()
	{
		ExternalEnemyHexRegistration registration = CreateTestEnemyHexRegistration(typeof(CompatibilityEnemyHexAlphaRelic)) with
		{
			AmountAct1 = 0,
			AmountAct2 = 2,
			AmountAct3 = 5
		};
		MonsterHexKind kind = ExternalEnemyHexIdentity.ComputeKind(ModelDb.GetId(typeof(CompatibilityEnemyHexAlphaRelic)).Entry);
		List<(Creature Target, decimal Amount)> applied = [];
		ExternalPowerEnemyHex effect = new(
			new ResolvedExternalEnemyHex(kind, "ALPHA", registration),
			(target, amount, applier, _, _) =>
			{
				Expect(ReferenceEquals(target, applier), "the enemy is its own applier, like built-in persistent powers");
				applied.Add((target, amount));
				return Task.CompletedTask;
			});

		// 游戏里用的默认施加委托：构造时按能力类型封闭 HextechPowerCmdCompat.Apply<T>，封闭失败或 Task<T?> 转 Task 不成立会在这里抛出。
		_ = new ExternalPowerEnemyHex(new ResolvedExternalEnemyHex(kind, "ALPHA", registration));

		(HextechEnemyHexContext context, _, _) = CreatePrismaticEnemyFixture();
		FieldInfo actIndexField = AccessTools.Field(typeof(RunState), "_currentActIndex");
		CombatState combat = new(runState: context.RunState);
		Creature first = CreatePrismaticTestCreature(CombatSide.Enemy, combat);
		first.CombatId = 31;
		Creature joined = CreatePrismaticTestCreature(CombatSide.Enemy, combat);
		joined.CombatId = 32;

		actIndexField.SetValue(context.RunState, 0);
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		Equal(0, applied.Count, "tier 1 amount 0 applies nothing");

		actIndexField.SetValue(context.RunState, 1);
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		Equal(1, applied.Count, "applied once per enemy");
		Equal(2m, applied[0].Amount, "tier 2 amount");

		effect.ApplyPersistentToEnemy(context, joined, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		Equal(2, applied.Count, "an enemy joining mid-combat gets it too");
		Expect(ReferenceEquals(joined, applied[1].Target), "joined enemy is the target");

		actIndexField.SetValue(context.RunState, 2);
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: true).GetAwaiter().GetResult();
		Equal(3, applied.Count, "boss phase replay applies again");
		Equal(5m, applied[2].Amount, "tier 3 amount");
		SetEqual([ 31u, 32u ], context.Tracking.ExternalEnemyHexApplied[(int)kind], "marks live in the shared per-kind container");

		string snapshot = context.Tracking.Serialize();
		HextechMayhemCombatTrackingState restored = new();
		restored.Restore(snapshot);
		SetEqual([ 31u, 32u ], restored.ExternalEnemyHexApplied[(int)kind], "marks survive the combat snapshot");
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		Equal(3, applied.Count, "no re-application after the snapshot round trip");

		context.Tracking.Reset();
		Equal(0, context.Tracking.ExternalEnemyHexApplied.Count, "marks are cleared with the combat");
		effect.ApplyPersistentToEnemy(context, first, null, replayOneShotPowers: false).GetAwaiter().GetResult();
		Equal(4, applied.Count, "a new combat applies again");
	}

	private static EnemyHexFlags InvokeParseEnemyHexFlags(string? flags)
	{
		MethodInfo parse = typeof(HextechRunesInterop).GetMethod("ParseFlagNames", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new MissingMethodException(nameof(HextechRunesInterop), "ParseFlagNames");
		return (EnemyHexFlags)parse.MakeGenericMethod(typeof(EnemyHexFlags)).Invoke(null, [ flags, "flags" ])!;
	}

	private static void RegisterInteropTestEnemyHex(
		Type displayRelicType,
		Type? powerType = null,
		string rarity = "Silver",
		string? flags = null)
	{
		try
		{
			HextechRunesInterop.RegisterEnemyHex(
				displayRelicType,
				powerType ?? typeof(ThornsPower),
				rarity,
				amountAct1: 1,
				amountAct2: 2,
				amountAct3: 3,
				flags,
				assetModId: "Compatibility.EnemyHex",
				isAvailable: null);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			throw ex.InnerException;
		}
	}

	private static ExternalEnemyHexRegistration CreateTestEnemyHexRegistration(Type displayRelicType, EnemyHexFlags flags = EnemyHexFlags.None)
	{
		return new ExternalEnemyHexRegistration(displayRelicType, typeof(ThornsPower), HextechRarityTier.Silver, 1, 2, 3, flags);
	}

	private static MonsterHexKind TestEnemyHexKind(Type displayRelicType)
	{
		return HextechContentRegistry.MonsterHexMetadata.ExternalByKind.Values
			.Single(external => external.Registration.DisplayRelicType == displayRelicType)
			.Kind;
	}

	private static IReadOnlyList<HextechEnemyHexEffect> OrderedEnemyHexEffects()
	{
		return (IReadOnlyList<HextechEnemyHexEffect>)AccessTools.Property(typeof(HextechEnemyHexEffects), "OrderedEffects").GetValue(null)!;
	}

	private static void WithTestEnemyHexes(
		IReadOnlyList<ExternalEnemyHexRegistration> registrations,
		Action body,
		IReadOnlyDictionary<Type, Func<IRunState, bool>>? availability = null)
	{
		Action[] restore =
		[
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "EnemyHexRegistrations"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "EnemyHexAvailabilityByType"),
			CaptureStaticCollectionRestore(typeof(HextechExternalContentRegistry), "AssetModIdsByType"),
			CaptureExternalRegistryVersionRestore()
		];
		try
		{
			foreach (ExternalEnemyHexRegistration registration in registrations)
			{
				HextechExternalContentRegistry.RegisterEnemyHex(
					registration,
					"Compatibility.EnemyHex",
					availability?.GetValueOrDefault(registration.DisplayRelicType));
			}

			body();
		}
		finally
		{
			foreach (Action action in restore.Reverse())
			{
				action();
			}
		}
	}

	private sealed class CompatibilityEnemyHexAlphaRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class CompatibilityEnemyHexBetaRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class CompatibilityEnemyHexGammaRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}

	private sealed class CompatibilityEnemyHexCommonRelic : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Common;
	}

	// 类名的 ModelId 条目是 THORNMAIL，与内置枚举名 Thornmail 只差大小写。
	private sealed class Thornmail : RelicModel
	{
		public sealed override RelicRarity Rarity => RelicRarity.Starter;
	}
}
