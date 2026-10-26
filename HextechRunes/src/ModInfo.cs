namespace HextechRunes;

internal static class ModInfo
{
	public const string Id = "HextechRunes";

	public const string DisplayName = "海克斯符文";

	public const string Version = "0.9.9";

	// 发布变体只有两个;Directory.Build.targets 的 HextechValidateTarget 拦住其它目标。
#if STS2_111_OR_NEWER
	public const string TargetGameVersion = "0.111.0";
#else
	public const string TargetGameVersion = "0.107.1";
#endif
}
