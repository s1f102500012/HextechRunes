using System.Reflection;
using HarmonyLib;
using HextechRunes;
using PatchOutcome = HextechRunes.HextechPatcher.PatchOutcome;
using PatchResult = HextechRunes.HextechPatcher.PatchResult;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>
	/// <c>[HextechPatch(MonsterHexes = ...)]</c> 只能列注册表里存在的敌方海克斯，且不重复；
	/// 补丁失败时按这份列表点名，列了不存在的种类等于告警指向空气。
	/// </summary>
	[HextechTest]
	private static void PatchMonsterHexMetadataReferencesRegisteredEnemyHexes()
	{
		HashSet<MonsterHexKind> registered = HextechMonsterHexRegistry.Registrations.Select(static row => row.Kind).ToHashSet();
		List<string> problems = [];
		Dictionary<string, MonsterHexKind[]> declared = new(StringComparer.Ordinal);
		foreach (Type type in typeof(ModEntry).Assembly.GetTypes())
		{
			HextechPatchAttribute? meta = type.GetCustomAttribute<HextechPatchAttribute>();
			if (meta?.MonsterHexes == null)
			{
				continue;
			}

			declared[meta.Id] = meta.MonsterHexes;
			if (meta.MonsterHexes.Length == 0)
			{
				problems.Add($"{meta.Id}: empty MonsterHexes (omit the property instead)");
			}

			if (meta.MonsterHexes.Distinct().Count() != meta.MonsterHexes.Length)
			{
				problems.Add($"{meta.Id}: duplicate MonsterHexes entries");
			}

			foreach (MonsterHexKind kind in meta.MonsterHexes.Where(kind => !registered.Contains(kind)))
			{
				problems.Add($"{meta.Id}: MonsterHexes lists unregistered enemy hex {kind}");
			}
		}

		Expect(problems.Count == 0, "patch MonsterHexes metadata problems:\n  " + string.Join("\n  ", problems));
		Expect(
			declared.TryGetValue("enemy.manipulate-reality.generated-cards", out MonsterHexKind[]? reality) && reality.SequenceEqual([MonsterHexKind.ManipulateReality]),
			"enemy Manipulate Reality patch should declare its enemy hex");
		Expect(
			declared.TryGetValue("reward.card-select", out MonsterHexKind[]? grimoire) && grimoire.SequenceEqual([MonsterHexKind.ForbiddenGrimoire]),
			"reward.card-select should declare enemy Forbidden Grimoire");
		string[] nearDeathIds = declared
			.Where(static pair => pair.Key.StartsWith("combat.near-death-feast.", StringComparison.Ordinal) && pair.Value.SequenceEqual([MonsterHexKind.NearDeathFeast]))
			.Select(static pair => pair.Key)
			.ToArray();
		Equal(10, nearDeathIds.Length, "near-death feast patches declaring enemy Near-Death Feast");
	}

	/// <summary>
	/// 同一目标上大号匕首替换前缀必须先于敌方「操控现实」翻倍前缀：翻倍要作用在替换后的整批卡上。
	/// 先后只由优先级表达（同一 Harmony owner 无法用 before/after 排序），这里冻结两者的目标与优先级关系，
	/// 并确认敌方补丁不挂我方符文（失败时不能把大号匕首或操控现实符文标成不可用）。
	/// </summary>
	[HextechTest]
	private static void PatchGeneratedCardsPrefixesRunBigKnifeBeforeManipulateReality()
	{
		Type bigKnife = typeof(BigKnifeRune).GetNestedType("GeneratedCardsPatch", BindingFlags.NonPublic)
			?? throw new InvalidOperationException("BigKnifeRune.GeneratedCardsPatch not found");
		Type reality = typeof(HextechEnemyManipulateRealityHooks).GetNestedType("GeneratedCardsPatch", BindingFlags.NonPublic)
			?? throw new InvalidOperationException("HextechEnemyManipulateRealityHooks.GeneratedCardsPatch not found");

		HarmonyMethod bigKnifeTarget = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(bigKnife));
		HarmonyMethod realityTarget = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(reality));
		Equal(DescribePatchTarget(bigKnifeTarget, bigKnife), DescribePatchTarget(realityTarget, reality), "both prefixes patch the same AddGeneratedCardsToCombat overload");

		int bigKnifePriority = PrefixPriority(bigKnife);
		int realityPriority = PrefixPriority(reality);
		Equal(Priority.Normal, bigKnifePriority, "Big Knife generated-cards prefix priority");
		Expect(realityPriority < bigKnifePriority, $"Manipulate Reality prefix ({realityPriority}) must run after Big Knife prefix ({bigKnifePriority})");

		HextechPatchAttribute meta = reality.GetCustomAttribute<HextechPatchAttribute>()!;
		Expect(!meta.AffectedRunes.Any(), "enemy Manipulate Reality patch must not be attributed to any player rune");
	}

	private static int PrefixPriority(Type patchType)
	{
		MethodInfo prefix = patchType.GetMethods(PatchMemberFlags).Single(static method => method.GetCustomAttribute<HarmonyPrefix>() != null);
		return prefix.GetCustomAttribute<HarmonyPriority>()?.info.priority
			?? throw new InvalidOperationException($"{patchType.FullName}.{prefix.Name} must declare an explicit [HarmonyPriority]");
	}

	/// <summary>补丁器把类处理器结果分成已应用 / 主动跳过 / 失败，摘要首行保持 <c>Applied </c> 开头并追加跳过数。</summary>
	[HextechTest]
	private static void PatchOutcomeClassificationSeparatesAppliedSkippedFailed()
	{
		Equal(PatchOutcome.Applied, HextechPatcher.ClassifyClassProcessorResult(1, hasPrepare: false, optional: false), "patched methods");
		Equal(PatchOutcome.Applied, HextechPatcher.ClassifyClassProcessorResult(2, hasPrepare: true, optional: true), "patched methods with prepare/optional");
		Equal(PatchOutcome.Skipped, HextechPatcher.ClassifyClassProcessorResult(0, hasPrepare: true, optional: false), "prepare returned false");
		Equal(PatchOutcome.Skipped, HextechPatcher.ClassifyClassProcessorResult(0, hasPrepare: false, optional: true), "optional without target");
		Equal(PatchOutcome.Failed, HextechPatcher.ClassifyClassProcessorResult(0, hasPrepare: false, optional: false), "required patch without target");

		PatchResult[] results =
		[
			new("a.applied", "A", typeof(object), PatchOutcome.Applied, null),
			new("z.skipped", "Z", typeof(object), PatchOutcome.Skipped, null),
			new("b.skipped", "B", typeof(object), PatchOutcome.Skipped, null),
			new("c.failed", "C", typeof(object), PatchOutcome.Failed, "InvalidOperationException: boom"),
		];
		IReadOnlyList<string> lines = HextechPatcher.BuildSummaryLines(results);
		Equal(2, lines.Count, "summary line count with skipped patches");
		Equal("Applied 1/4 patch classes, skipped 2, failed 1.", lines[0], "summary first line");
		Expect(lines[1].EndsWith(": b.skipped, z.skipped", StringComparison.Ordinal), $"skipped ids listed in ordinal order: {lines[1]}");

		IReadOnlyList<string> clean = HextechPatcher.BuildSummaryLines([results[0]]);
		Equal(1, clean.Count, "summary without skipped patches has no skipped line");
		Equal("Applied 1/1 patch classes, skipped 0, failed 0.", clean[0], "clean summary line");
	}
}
