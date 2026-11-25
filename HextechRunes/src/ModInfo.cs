namespace HextechRunes;

internal static class ModInfo
{
	public const string Id = "HextechRunes";

	public const string DisplayName = "海克斯符文";

	public const string Version = "0.10.3";

	// 发布变体为 0.111.0 / 0.113.0 / 0.114.0;Directory.Build.targets 的 HextechValidateTarget 拦住其它目标。
#if STS2_114_OR_NEWER
	public const string TargetGameVersion = "0.114.0";
#elif STS2_113_OR_NEWER
	public const string TargetGameVersion = "0.113.0";
#else
	public const string TargetGameVersion = "0.111.0";
#endif
}
