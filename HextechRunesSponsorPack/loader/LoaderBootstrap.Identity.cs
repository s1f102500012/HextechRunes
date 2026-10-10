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
	internal const string ModDisplayNameZh = "海克斯大乱斗：额外拓展包";
	internal const string ModDisplayNameEn = "ARAM: Mayhem: Extra Expansion Pack";
}
