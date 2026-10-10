namespace HextechRunes.Loader;

// 本体加载器的身份常量。共享的加载器源码只从这里读 mod 身份;拓展包 loader 有自己的一份。
public static partial class LoaderBootstrap
{
	internal const string ModId = "HextechRunes";
	internal const string VariantManifestName = "hextech-runes-variants.manifest";
	internal const string CompatTargetMetadataKey = "HextechCompatibilityTarget";
	internal static readonly string? RequiredAssemblyName = null;

	// 加载失败弹窗用的模组名,与 assets/localization/*/main_menu_ui.json 的 HEXTECH_MOD_NAME 一致。
	internal static readonly Dictionary<string, string> ModDisplayNames = new(StringComparer.Ordinal)
	{
		["zhs"] = "海克斯大乱斗",
		["eng"] = "ARAM: Mayhem",
		["jpn"] = "ランダムミッド：メイヘム",
		["kor"] = "무작위 총력전: 아수라장",
		["spa"] = "ARAM: Caos",
		["esp"] = "ARAM: Caos",
		["ptb"] = "ARAM: Desordem",
		["rus"] = "ARAM: Mayhem",
		["tha"] = "ARAM: Mayhem",
	};
}
