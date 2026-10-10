using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HextechRunes;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>
	/// 头像系数悬浮不能再用房主(Players[0])或调试接口取状态:联机客户端会看到房主的系数。
	/// </summary>
	[HextechTest]
	private static void PlayerStatsHoverUsesLocalPortraitOwner()
	{
		Type hooks = typeof(HextechPlayerStatsHoverHooks);
		MethodInfo[] methods = hooks.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
			.Concat(hooks.GetNestedTypes(BindingFlags.NonPublic)
				.SelectMany(static nested => nested.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
			.Where(static method => method.GetMethodBody() != null)
			.ToArray();
		MethodInfo[] calls = methods
			.SelectMany(static method => PatchProcessor.GetOriginalInstructions(method))
			.Select(static instruction => instruction.operand)
			.OfType<MethodInfo>()
			.ToArray();
		Expect(
			calls.All(static method => method.Name != nameof(RunManager.DebugOnlyGetState)),
			"portrait stat hover must not read the debug-only run state");
		Expect(
			calls.All(static method => method.Name != "get_Players"),
			"portrait stat hover must not pick a player from the run's player list");
		Expect(
			calls.Any(static method => method.DeclaringType?.Name == "LocalContext" && method.Name == "GetMe"),
			"portrait stat hover should resolve the local player like the vanilla top bar");
	}

	/// <summary>社区面板的网络按钮不能挂 async void 处理器:异常会逃逸成未观察异常。</summary>
	[HextechTest]
	private static void ConfigMenuHasNoAsyncVoidHandlers()
	{
		IEnumerable<Type> types = new[] { typeof(HextechRuneConfigMenuHooks) }
			.Concat(typeof(HextechRuneConfigMenuHooks).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
		MethodInfo[] asyncVoid = types
			.SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			.Where(static method => method.ReturnType == typeof(void) && method.GetCustomAttribute<AsyncStateMachineAttribute>() != null)
			.ToArray();
		Expect(
			asyncVoid.Length == 0,
			$"config menu should start async work through TaskHelper.RunSafely, found async void: {string.Join(", ", asyncVoid.Select(static method => method.DeclaringType?.Name + "." + method.Name))}");
	}

	/// <summary>灼烧每次结算的掉血取层数与百分比两者较大者(血条预测直接调用同一函数)。</summary>
	[HextechTest]
	private static void BurnHpLossTakesLargerOfStacksAndPercent()
	{
		Equal(3, HextechBurnPower.CalculateHpLoss(50, 3), "low hp: stacks dominate");
		Equal(15, HextechBurnPower.CalculateHpLoss(500, 3), "high hp: percent dominates");
		Equal(1, HextechBurnPower.CalculateHpLoss(10, 1), "minimum loss is one stack");
	}

	/// <summary>图鉴子分类标题套用原版「初始」标题的富文本骨架,只换标题与正文,各语言通用。</summary>
	[HextechTest]
	private static void CollectionHeaderFollowsStarterTemplate()
	{
		MethodInfo format = typeof(HextechCollectionHooks).GetMethod("FormatLikeStarterHeader", BindingFlags.Static | BindingFlags.NonPublic)
			?? throw new MissingMethodException(nameof(HextechCollectionHooks), "FormatLikeStarterHeader");
		string Format(string? template, string own) => (string)format.Invoke(null, [template, own])!;

		Equal(
			"[gold][font_size=28][b]海克斯：[/b][/font_size][/gold]来自海克斯符文池的自定义遗物。",
			Format("[gold][font_size=28][b]初始：[/b][/font_size][/gold]角色们开始游戏时自身携带的遗物。", "[gold]海克斯：[/gold] 来自海克斯符文池的自定义遗物。"),
			"zhs header keeps the vanilla layout without the gap");
		Equal(
			"[gold][font_size=28][b]Hextech:[/b][/font_size][/gold] Custom relics from the Hextech rune pool.",
			Format("[gold][font_size=28][b]Starter:[/b][/font_size][/gold] Characters begin their run with these relics.", "[gold]Hextech:[/gold] Custom relics from the Hextech rune pool."),
			"eng header replaces both title and body");
		Equal(
			"[gold]Hextech:[/gold] Body",
			Format(null, "[gold]Hextech:[/gold] Body"),
			"missing starter template falls back to the loc text");
		Equal(
			"Plain text",
			Format("[gold][font_size=28][b]Starter:[/b][/font_size][/gold] Body", "Plain text"),
			"loc text without the gold title falls back unchanged");
	}

	/// <summary>
	/// 选择界面原版特效的数据文件：界面会按稀有度拼系统名，四类特效都要齐；引用的贴图都要在 assets 里，
	/// kiwi_selection 目录也不留无人引用的贴图。
	/// </summary>
	[HextechTest]
	private static void KiwiSelectionVfxDataIsCompleteAndAssetsExist()
	{
		string images = Path.Combine(AuditRoot, "assets", "images");
		string dataPath = Path.Combine(images, "effects", "kiwi_selection", "kiwi_selection_vfx.json");
		Expect(
			HextechAssets.KiwiSelectionVfxDataPath == HextechAssets.ImageRoot + "effects/kiwi_selection/kiwi_selection_vfx.json",
			"vfx data constant should point at the checked-in json");
		HextechKiwiVfxLibrary library = HextechKiwiVfxLibrary.Parse(File.ReadAllText(dataPath));

		List<string> expected = [HextechKiwiVfxLibrary.GoldenRerollIdle, HextechKiwiVfxLibrary.GoldenRerollClick];
		foreach (string suffix in new[] { "FlashInVFX", "RefreshVFX", "RefreshOverlayVFX" })
		{
			foreach (string rarityKey in new[] { "SILVER", "GOLD", "PRISMATIC" })
			{
				expected.Add(HextechKiwiVfxLibrary.TierSystemName(rarityKey, suffix));
			}
		}
		string[] missingSystems = expected.Where(name => !library.Systems.TryGetValue(name, out List<HextechKiwiVfxEmitter>? emitters) || emitters.Count == 0).ToArray();
		Expect(missingSystems.Length == 0, "missing vfx systems: " + string.Join(", ", missingSystems));

		// 只有挂在重随按钮上的系统才按按钮缩放；挂在卡上的系统误标成 button 会让粒子放大约 1.6 倍、飞出卡外。
		string[] cardSystemsWithButtonSpace = library.Systems
			.Where(system => !system.Key.StartsWith(HextechKiwiVfxLibrary.GoldenRerollIdle, StringComparison.Ordinal))
			.SelectMany(system => system.Value.Where(emitter => emitter.Space == "button").Select(emitter => $"{system.Key}/{emitter.Name}"))
			.ToArray();
		Expect(cardSystemsWithButtonSpace.Length == 0, "card systems with button-space emitters: " + string.Join(", ", cardSystemsWithButtonSpace));

		string[] referenced = library.ReferencedTextures().Distinct(StringComparer.Ordinal).ToArray();
		string[] missingTextures = referenced.Where(path => !File.Exists(Path.Combine(images, path + ".png"))).ToArray();
		Expect(missingTextures.Length == 0, "vfx textures missing from assets: " + string.Join(", ", missingTextures));

		string[] unused = Directory.EnumerateFiles(Path.Combine(images, "effects", "kiwi_selection"), "*.png")
			.Select(file => "effects/kiwi_selection/" + Path.GetFileNameWithoutExtension(file))
			.Except(referenced, StringComparer.Ordinal)
			.ToArray();
		Expect(unused.Length == 0, "unreferenced kiwi_selection textures: " + string.Join(", ", unused));
	}
}
