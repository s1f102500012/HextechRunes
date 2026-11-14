namespace HextechRunes;

internal static class ModInfo
{
	public const string Id = "HextechRunes";

	public const string DisplayName = "海克斯符文";

	public const string Version = "0.10.1";

	// 发布变体为 0.107.1 / 0.111.0 / 0.113.0;Directory.Build.targets 的 HextechValidateTarget 拦住其它目标。
#if STS2_113_OR_NEWER
	public const string TargetGameVersion = "0.113.0";
#elif STS2_111_OR_NEWER
	public const string TargetGameVersion = "0.111.0";
#else
	public const string TargetGameVersion = "0.107.1";
#endif
}
