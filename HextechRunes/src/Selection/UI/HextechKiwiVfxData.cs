using System.Text.Json;

namespace HextechRunes;

/// <summary>
/// 海克斯选择界面复刻用的粒子数据：英雄联盟海克斯大乱斗（Kiwi）增强选取界面的粒子定义，
/// 由 <c>assets/images/effects/kiwi_selection/SOURCE.md</c> 记录的流程导出成 JSON。
/// 本文件只做解析与曲线采样，不碰 Godot API，测试可以直接校验数据完整性。
/// </summary>
internal sealed class HextechKiwiVfxLibrary
{
	public const string GoldenRerollIdle = "Augment_GoldReroll";
	public const string GoldenRerollClick = "Augment_GoldReroll_Click";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public Dictionary<string, List<HextechKiwiVfxEmitter>> Systems { get; set; } = new(StringComparer.Ordinal);

	public Dictionary<string, HextechKiwiVfxTexture> Textures { get; set; } = new(StringComparer.Ordinal);

	internal static HextechKiwiVfxLibrary Parse(string json)
	{
		return JsonSerializer.Deserialize<HextechKiwiVfxLibrary>(json, JsonOptions)
			?? throw new InvalidDataException("kiwi selection vfx data is empty");
	}

	/// <summary>界面用 "SILVER" / "GOLD" / "PRISMATIC" 表示稀有度，原版系统名用 Silver / Gold / Prismatic。</summary>
	internal static string TierSystemName(string rarityKey, string suffix)
	{
		string tier = rarityKey switch
		{
			"SILVER" => "Silver",
			"PRISMATIC" => "Prismatic",
			_ => "Gold"
		};
		return $"Augment_{tier}_{suffix}";
	}

	internal IEnumerable<string> ReferencedTextures()
	{
		foreach (HextechKiwiVfxEmitter emitter in Systems.Values.SelectMany(static emitters => emitters))
		{
			yield return emitter.Texture;
			if (emitter.ColorTexture != null)
			{
				yield return emitter.ColorTexture;
			}
			if (emitter.Mult != null)
			{
				yield return emitter.Mult.Texture;
			}
			if (emitter.Erosion != null)
			{
				yield return emitter.Erosion.Texture;
			}
		}
	}
}

internal sealed class HextechKiwiVfxTexture
{
	public string Source { get; set; } = string.Empty;

	public int[] Size { get; set; } = [];
}

/// <summary>一个原版发射器；字段名与原版 VfxEmitterDefinitionData 对应，单位为原版粒子单位。</summary>
internal sealed class HextechKiwiVfxEmitter
{
	public string Name { get; set; } = string.Empty;

	/// <summary>原版绘制顺序，数值小的先画。</summary>
	public int Pass { get; set; }

	/// <summary>blendMode 4 为加法叠加，其余按 Alpha 混合。</summary>
	public bool Additive { get; set; }

	/// <summary>isSingleParticle：开始时一次性发射 rate 个粒子。</summary>
	public bool Single { get; set; }

	public float Delay { get; set; }

	/// <summary>发射器持续时间；为空表示一直发射。</summary>
	public float? Lifetime { get; set; }

	public HextechKiwiVfxValue Rate { get; set; } = HextechKiwiVfxValue.Zero;

	public HextechKiwiVfxValue ParticleLifetime { get; set; } = HextechKiwiVfxValue.One;

	public HextechKiwiVfxValue Position { get; set; } = HextechKiwiVfxValue.Zero;

	public HextechKiwiVfxShape? Shape { get; set; }

	public HextechKiwiVfxValue BirthVelocity { get; set; } = HextechKiwiVfxValue.Zero;

	public HextechKiwiVfxValue? Velocity { get; set; }

	public HextechKiwiVfxValue? Acceleration { get; set; }

	public HextechKiwiVfxValue? Drag { get; set; }

	public HextechKiwiVfxValue BirthScale { get; set; } = HextechKiwiVfxValue.One;

	public bool UniformScale { get; set; }

	public HextechKiwiVfxValue? Scale { get; set; }

	public HextechKiwiVfxValue? BirthRotation { get; set; }

	public HextechKiwiVfxValue BirthColor { get; set; } = HextechKiwiVfxValue.One;

	public HextechKiwiVfxValue? Color { get; set; }

	public string Texture { get; set; } = string.Empty;

	/// <summary>贴图寻址：0 重复、1 夹紧、2 镜像（按原版用法推断）。</summary>
	public int AddressBase { get; set; }

	public float[] UvScale { get; set; } = [1f, 1f];

	public HextechKiwiVfxValue? UvOffset { get; set; }

	public HextechKiwiVfxValue? UvScroll { get; set; }

	public string? ColorTexture { get; set; }

	public HextechKiwiVfxMult? Mult { get; set; }

	public HextechKiwiVfxErosion? Erosion { get; set; }

	/// <summary>"button" 按重随按钮缩放，"card" 按卡牌缩放。本模组按钮相对卡牌比原版大，两者不能共用一个比例。</summary>
	public string Space { get; set; } = "card";
}

internal sealed class HextechKiwiVfxShape
{
	public string Type { get; set; } = string.Empty;

	public float[] Size { get; set; } = [];

	public float Radius { get; set; }

	public HextechKiwiVfxValue? Offset { get; set; }
}

internal sealed class HextechKiwiVfxMult
{
	public string Texture { get; set; } = string.Empty;

	public int Address { get; set; }

	public HextechKiwiVfxValue Scale { get; set; } = HextechKiwiVfxValue.One;

	public HextechKiwiVfxValue Offset { get; set; } = HextechKiwiVfxValue.Zero;

	public HextechKiwiVfxValue Scroll { get; set; } = HextechKiwiVfxValue.Zero;

	/// <summary>角度。</summary>
	public HextechKiwiVfxValue Rotation { get; set; } = HextechKiwiVfxValue.Zero;

	/// <summary>角度/秒。</summary>
	public HextechKiwiVfxValue RotateRate { get; set; } = HextechKiwiVfxValue.Zero;
}

internal sealed class HextechKiwiVfxErosion
{
	public string Texture { get; set; } = string.Empty;

	/// <summary>溶解阈值随粒子寿命的曲线；为空时阈值等于寿命进度。</summary>
	public HextechKiwiVfxValue? Drive { get; set; }

	public float Slice { get; set; } = 0.1f;

	public float[] Mixer { get; set; } = [1f, 0f, 0f, 0f];
}

/// <summary>
/// 原版 Value* 的归一化形式：T 为关键帧时间（0~1），V 为各帧取值，P 为每个分量的概率表
/// （[关键时间, 关键值]，出生时按均匀随机数查表后乘到对应分量上）。
/// </summary>
internal sealed class HextechKiwiVfxValue
{
	public static readonly HextechKiwiVfxValue Zero = new() { T = [0f], V = [[0f, 0f, 0f, 0f]] };

	public static readonly HextechKiwiVfxValue One = new() { T = [0f], V = [[1f, 1f, 1f, 1f]] };

	public float[] T { get; set; } = [0f];

	public float[][] V { get; set; } = [[0f]];

	public float[][]?[]? P { get; set; }

	/// <summary>按曲线取 t 处的值；缺少的分量补 0。</summary>
	internal void Sample(float t, Span<float> destination)
	{
		if (V.Length == 0)
		{
			destination.Clear();
			return;
		}

		if (V.Length == 1 || T.Length <= 1 || t <= T[0])
		{
			Copy(V[0], destination);
			return;
		}

		int last = Math.Min(T.Length, V.Length) - 1;
		for (int i = 1; i <= last; i++)
		{
			if (t > T[i])
			{
				continue;
			}

			float span = T[i] - T[i - 1];
			float f = span > 0f ? (t - T[i - 1]) / span : 0f;
			float[] a = V[i - 1];
			float[] b = V[i];
			for (int k = 0; k < destination.Length; k++)
			{
				float va = k < a.Length ? a[k] : 0f;
				float vb = k < b.Length ? b[k] : 0f;
				destination[k] = va + (vb - va) * f;
			}
			return;
		}

		Copy(V[last], destination);
	}

	/// <summary>出生取值：先按发射器进度取曲线，再乘上概率表的随机倍率。</summary>
	internal void SampleBirth(float emitterProgress, ref HextechKiwiVfxRng rng, Span<float> destination)
	{
		Sample(emitterProgress, destination);
		if (P == null)
		{
			return;
		}

		for (int k = 0; k < destination.Length && k < P.Length; k++)
		{
			if (P[k] is { Length: 2 } table && table[0].Length > 0)
			{
				destination[k] *= Interpolate(table[0], table[1], rng.NextUnit());
			}
		}
	}

	internal float SampleScalar(float t)
	{
		Span<float> value = stackalloc float[1];
		Sample(t, value);
		return value[0];
	}

	internal float SampleBirthScalar(float emitterProgress, ref HextechKiwiVfxRng rng)
	{
		Span<float> value = stackalloc float[1];
		SampleBirth(emitterProgress, ref rng, value);
		return value[0];
	}

	/// <summary>概率表倍率的取值范围，供着色器按粒子种子插值（如 UV 随机偏移）。</summary>
	internal (float Min, float Max) BirthRange(int component)
	{
		Span<float> value = stackalloc float[4];
		Sample(0f, value);
		float baseValue = component < 4 ? value[component] : 0f;
		if (P == null || component >= P.Length || P[component] is not { Length: 2 } table || table[1].Length == 0)
		{
			return (baseValue, baseValue);
		}

		float a = baseValue * table[1].Min();
		float b = baseValue * table[1].Max();
		return (Math.Min(a, b), Math.Max(a, b));
	}

	private static float Interpolate(float[] times, float[] values, float t)
	{
		if (values.Length == 0)
		{
			return 1f;
		}
		if (t <= times[0])
		{
			return values[0];
		}
		int last = Math.Min(times.Length, values.Length) - 1;
		for (int i = 1; i <= last; i++)
		{
			if (t <= times[i])
			{
				float span = times[i] - times[i - 1];
				float f = span > 0f ? (t - times[i - 1]) / span : 0f;
				return values[i - 1] + (values[i] - values[i - 1]) * f;
			}
		}
		return values[last];
	}

	private static void Copy(float[] source, Span<float> destination)
	{
		for (int k = 0; k < destination.Length; k++)
		{
			destination[k] = k < source.Length ? source[k] : 0f;
		}
	}
}

/// <summary>
/// 粒子外观用的本地 xorshift 序列：种子取自节点实例，只决定火花位置等纯表现细节，
/// 不读写任何共享 RNG，也不影响抽选结果。
/// </summary>
internal struct HextechKiwiVfxRng
{
	private uint _state;

	public HextechKiwiVfxRng(ulong seed)
	{
		_state = (uint)(seed ^ (seed >> 32)) | 1u;
	}

	internal float NextUnit()
	{
		_state ^= _state << 13;
		_state ^= _state >> 17;
		_state ^= _state << 5;
		return (_state & 0xFFFFFF) / 16777216f;
	}
}
