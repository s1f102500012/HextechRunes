namespace HextechRunesSponsorPack.Loader;

// 拓展包加载器的身份常量。加载逻辑直接编译本体的 HextechRunes/loader/LoaderBootstrap.cs 与
// LinuxNativeDependencyBootstrap.cs(见 csproj),两边只在这些常量上不同。
public static partial class LoaderBootstrap
{
	internal const string ModId = "HextechRunesSponsorPack";
	internal const string VariantManifestName = "hextech-runes-sponsor-pack-variants.manifest";
	internal const string CompatTargetMetadataKey = "HextechSponsorCompatibilityTarget";
	// 变体引用本体程序集,必须等它加载后才能交给游戏。按程序集名而非 mod id 判断,与 ModEntry 的前置检测一致。
	internal static readonly string? RequiredAssemblyName = "HextechRunes";

	// 加载失败弹窗用的模组名,与 assets/localization/*/main_menu_ui.json 的 HEXTECH_SPONSOR_MOD_NAME 一致。
	internal static readonly Dictionary<string, string> ModDisplayNames = new(StringComparer.Ordinal)
	{
		["zhs"] = "海克斯大乱斗：额外拓展包",
		["eng"] = "ARAM: Mayhem: Extra Expansion Pack",
		["jpn"] = "ランダムミッド：メイヘム：追加拡張パック",
		["kor"] = "무작위 총력전: 아수라장: 추가 확장팩",
		["spa"] = "ARAM: Caos: Paquete de patrocinadores",
		["esp"] = "ARAM: Caos: Paquete de patrocinadores",
		["ptb"] = "ARAM: Desordem: Pacote de Expansão Extra",
		["rus"] = "ARAM: Mayhem: дополнительное расширение",
		["tha"] = "ARAM: Mayhem: แพ็กเสริมเพิ่มเติม",
	};
}
