using Godot;

namespace HextechRunes;

/// <summary>
/// 选择界面特效的预热结果：解析好的粒子数据，加上在后台线程解码好、还没上传显卡的贴图。
/// 贴图在第一次用到时由 <see cref="HextechKiwiVfxPlayer"/> 在主线程建成纹理并缓存到 <see cref="Textures"/>。
/// 只在主线程读写（后台线程只负责构造）。
/// </summary>
internal sealed class HextechKiwiVfxWarmData
{
	private HextechKiwiVfxWarmData(HextechKiwiVfxLibrary? library, Dictionary<string, Image> images, string? error)
	{
		Library = library;
		Images = images;
		Error = error;
	}

	public HextechKiwiVfxLibrary? Library { get; }

	public Dictionary<string, Image> Images { get; }

	public Dictionary<string, ImageTexture> Textures { get; } = new(StringComparer.Ordinal);

	public string? Error { get; }

	internal static HextechKiwiVfxWarmData Empty(string error)
	{
		return new HextechKiwiVfxWarmData(null, new Dictionary<string, Image>(StringComparer.Ordinal), error);
	}

	/// <summary>在后台线程运行：解析 JSON，解码所有引用到的贴图。单张贴图失败只是不预热，用到时再走常规加载。</summary>
	internal static HextechKiwiVfxWarmData Decode(string json, Func<string, byte[]> readTexture)
	{
		if (string.IsNullOrEmpty(json))
		{
			return Empty($"data missing: {HextechAssets.KiwiSelectionVfxDataPath}");
		}

		HextechKiwiVfxLibrary library;
		try
		{
			library = HextechKiwiVfxLibrary.Parse(json);
		}
		catch (Exception ex)
		{
			return Empty(ex.Message);
		}

		Dictionary<string, Image> images = new(StringComparer.Ordinal);
		foreach (string texture in library.ReferencedTextures().Distinct(StringComparer.Ordinal))
		{
			try
			{
				byte[] bytes = readTexture(texture);
				Image image = new();
				if (bytes.Length > 0 && image.LoadPngFromBuffer(bytes) == Godot.Error.Ok)
				{
					images[texture] = image;
				}
			}
			catch (Exception)
			{
				// 用到时 HextechTextures 会再加载一次并按路径告警。
			}
		}

		return new HextechKiwiVfxWarmData(library, images, null);
	}
}
