using System.Text.RegularExpressions;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 源码守卫覆盖所有玩法目录，也扫描 UI/Services，例外必须逐文件给出理由。
	// 注释被移除，字符串保留：插值表达式里的危险调用同样不能逃过检查。
	private static string AuditSource(string path) => Regex.Replace(
		File.ReadAllText(path), @"/\*[\s\S]*?\*/|(?m)^\s*//.*$", "");

	private static string AuditRoot => Path.GetFullPath(Path.Combine(FindTestsSourceDirectory(), "../.."));

	[HextechTest]
	private static void GameplayDeterminismApisRequireReviewedExceptions()
	{
		string pattern = @"\bSystem\s*\.\s*Random\b|\bnew\s+Random\s*\(|\bRandom\s*\.\s*Shared\b|\bGuid\s*\.\s*NewGuid\b|\bDateTime\s*\.\s*(?:Now|UtcNow)\b|\bStopwatch\b|\bTime\s*\.\s*GetTicks\w*\b|\bGodot\s*\.\s*Timer\b";
		// 每个例外文件只放行一种 API,且必须写明为什么它不影响共享状态。
		Dictionary<string, (string Api, string Reason)> exceptions = new(StringComparer.Ordinal)
		{
			["src/Services/HextechFeaturedConfigs.cs"] = ("DateTime.UtcNow", "只给推荐配置HTTP缓存定时，不驱动战斗状态或共享RNG。"),
			["src/Selection/UI/HextechRuneSelectionScreen.Interaction.cs"] = ("Time.GetTicksMsec", "仅防本地重复点击，最终选择按模型ID同步。"),
			["src/Hooks/UI/HextechRelicVisibilityHooks.ToggleUi.cs"] = ("Godot.Timer", "只重定位隐藏遗物按钮，不改模型或共享RNG。")
		};
		List<string> violations = [];
		HashSet<string> seenExceptions = new(StringComparer.Ordinal);
		foreach (string file in Directory.EnumerateFiles(Path.Combine(AuditRoot, "src"), "*.cs", SearchOption.AllDirectories))
		{
			if (IsGeneratedAuditPath(file))
			{
				continue;
			}
			string relative = Path.GetRelativePath(AuditRoot, file).Replace('\\', '/');
			foreach (Match match in Regex.Matches(AuditSource(file), pattern))
			{
				string api = Regex.Replace(match.Value, @"\s+", "");
				if (exceptions.TryGetValue(relative, out (string Api, string Reason) exception) && api == exception.Api && !string.IsNullOrWhiteSpace(exception.Reason))
				{
					seenExceptions.Add(relative);
				}
				else
				{
					violations.Add($"{relative}: {match.Value}");
				}
			}
		}
		Expect(violations.Count == 0, "unreviewed nondeterministic gameplay APIs: " + string.Join("; ", violations));
		Expect(seenExceptions.Count == exceptions.Count, "remove stale determinism API exceptions after reviewing their replacement");
	}

	private static bool IsGeneratedAuditPath(string path) => path.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal)
		|| path.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal);

	[HextechTest]
	private static void RandomGenerationClassesMatchReviewedManifest()
	{
		// 保守地登记命中生成入口文件中的所有类；包括具体符文及共用工厂，新增路径必须人工复核。
		string generation = @"\b(?:CardFactory|RelicFactory|PotionFactory)\s*\.|\b(?:TransformToStableRandom|CreateStableOptionTransformation|PickStableGeneratedCard|BuildStableCombatGenerationPool|CreateMinionCard|CreateRepeatableWaxRelic|CreateRandomNonupeipeRelic|GetPotionOptions|ObtainRandomRunes|ConsumeAndObtainRandomRunes|ReplaceOwnedHextechRunesWithRandomRunes|ObtainRandomForges|TryObtainRandomForges|AddRandomForgeReward|AddWeightedRandomForgeReward)\s*\(";
		List<string> actual = [];
		foreach (string file in Directory.EnumerateFiles(Path.Combine(AuditRoot, "src"), "*.cs", SearchOption.AllDirectories))
		{
			if (IsGeneratedAuditPath(file))
			{
				continue;
			}
			string source = AuditSource(file);
			if (!Regex.IsMatch(source, generation))
			{
				continue;
			}
			string relative = Path.GetRelativePath(AuditRoot, file).Replace('\\', '/');
			foreach (string type in Regex.Matches(source, @"\bclass\s+(\w+)").Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal))
			{
				actual.Add($"{relative} | {type}");
			}
		}
		string manifest = Path.Combine(FindTestsSourceDirectory(), "random_generation_audit.txt");
		string[] expected = File.ReadAllLines(manifest).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
		string[] found = actual.Order(StringComparer.Ordinal).ToArray();
		Expect(expected.SequenceEqual(found, StringComparer.Ordinal),
			"random generation audit drift; unreviewed: " + string.Join("; ", found.Except(expected, StringComparer.Ordinal))
			+ "; removed: " + string.Join("; ", expected.Except(found, StringComparer.Ordinal)));
	}
}
