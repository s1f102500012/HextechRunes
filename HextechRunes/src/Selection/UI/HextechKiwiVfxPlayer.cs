using Godot;

namespace HextechRunes;

/// <summary>
/// 按原版海克斯大乱斗增强选取界面的粒子定义播放一个特效系统（按钮常驻光、金色重随点击、卡牌弹出与重随）。
/// 每个原版发射器对应一个 <see cref="MultiMeshInstance2D"/>：粒子的位置、尺寸、旋转、颜色由 CPU 按曲线推进，
/// 主贴图 × 乘法贴图、溶解和颜色贴图在着色器里合成。挂在父节点的 FullRect 内，以父节点中心为原点。
/// 纯表现层：随机数取本地序列，不读写共享状态；非循环特效播完后自行释放。
/// </summary>
internal sealed partial class HextechKiwiVfxPlayer : Control
{
	private const int MaxParticlesPerEmitter = 256;

	private const string ShaderTemplate = """
		shader_type canvas_item;
		render_mode BLEND_MODE, unshaded;

		uniform sampler2D base_texture : filter_linear, repeat_enable;
		uniform sampler2D mult_texture : filter_linear, repeat_enable;
		uniform sampler2D erosion_texture : filter_linear, repeat_enable;
		uniform sampler2D color_texture : filter_linear;
		uniform bool use_mult = false;
		uniform bool use_erosion = false;
		uniform bool use_color_texture = false;
		uniform bool color_texture_vertical = false;
		uniform int base_address = 0;
		uniform int mult_address = 0;
		uniform vec2 uv_scale = vec2(1.0);
		uniform vec2 uv_scroll = vec2(0.0);
		uniform vec2 uv_offset_min = vec2(0.0);
		uniform vec2 uv_offset_max = vec2(0.0);
		uniform vec2 mult_scale = vec2(1.0);
		uniform vec2 mult_scroll = vec2(0.0);
		uniform vec2 mult_offset_min = vec2(0.0);
		uniform vec2 mult_offset_max = vec2(0.0);
		uniform float mult_rotation = 0.0;
		uniform float mult_rotate_rate = 0.0;
		uniform float erosion_slice = 0.1;
		uniform vec4 erosion_mixer = vec4(1.0, 0.0, 0.0, 0.0);

		// x 寿命进度, y 已存活秒数, z 溶解阈值, w 粒子种子
		varying vec4 particle;

		void vertex() {
			particle = INSTANCE_CUSTOM;
		}

		vec2 address(vec2 uv, int mode) {
			if (mode == 1) {
				return clamp(uv, vec2(0.002), vec2(0.998));
			}
			if (mode == 2) {
				return vec2(1.0) - abs(mod(uv, vec2(2.0)) - vec2(1.0));
			}
			return fract(uv);
		}

		float seeded(float seed, float salt) {
			return fract(sin(seed * 91.3458 + salt * 47.853) * 43758.5453);
		}

		void fragment() {
			vec2 base_seed = vec2(seeded(particle.w, 1.0), seeded(particle.w, 2.0));
			vec2 uv = UV * uv_scale + mix(uv_offset_min, uv_offset_max, base_seed) + uv_scroll * particle.y;
			vec4 result = texture(base_texture, address(uv, base_address));
			if (use_mult) {
				vec2 m = UV - vec2(0.5);
				float angle = mult_rotation + mult_rotate_rate * particle.y;
				m = vec2(m.x * cos(angle) - m.y * sin(angle), m.x * sin(angle) + m.y * cos(angle));
				vec2 mult_seed = vec2(seeded(particle.w, 3.0), seeded(particle.w, 4.0));
				m = m * mult_scale + vec2(0.5) + mix(mult_offset_min, mult_offset_max, mult_seed) + mult_scroll * particle.y;
				result *= texture(mult_texture, address(m, mult_address));
			}
			vec4 tint = COLOR;
			if (use_color_texture) {
				tint *= texture(color_texture, color_texture_vertical ? vec2(0.5, particle.x) : vec2(particle.x, 0.5));
			}
			result *= tint;
			if (use_erosion) {
				float erosion = dot(texture(erosion_texture, UV), erosion_mixer);
				result.a *= smoothstep(particle.z, particle.z + erosion_slice, erosion);
			}
			COLOR = vec4(max(result.rgb, vec3(0.0)), clamp(result.a, 0.0, 1.0));
		}
		""";

	private static readonly Lazy<HextechKiwiVfxLibrary?> Library = new(LoadLibrary);
	private static readonly Lazy<Shader> AdditiveShader = new(() => new Shader { Code = ShaderTemplate.Replace("BLEND_MODE", "blend_add") });
	private static readonly Lazy<Shader> MixShader = new(() => new Shader { Code = ShaderTemplate.Replace("BLEND_MODE", "blend_mix") });
	private static readonly Lazy<ArrayMesh> UnitQuad = new(BuildUnitQuad);

	private readonly List<EmitterState> _emitters = [];
	private HextechKiwiVfxRng _rng;
	private float _time;
	private float _startDelay;
	private bool _loop;

	private HextechKiwiVfxPlayer()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ProcessMode = ProcessModeEnum.Always;
		ClipContents = false;
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
	}

	/// <summary>
	/// 建立一个特效。<paramref name="cardPixelsPerUnit"/> 与 <paramref name="buttonPixelsPerUnit"/> 把原版粒子单位
	/// 换算成本界面像素；数据缺失或贴图加载失败时返回 null（已记录日志）。
	/// </summary>
	internal static HextechKiwiVfxPlayer? Create(
		string systemName,
		float cardPixelsPerUnit,
		float buttonPixelsPerUnit,
		bool loop = false,
		float delay = 0f)
	{
		HextechKiwiVfxLibrary? library = Library.Value;
		if (library == null || !library.Systems.TryGetValue(systemName, out List<HextechKiwiVfxEmitter>? definitions))
		{
			HextechLog.Warn("UI", $"Kiwi vfx system missing: {systemName}");
			return null;
		}

		HextechKiwiVfxPlayer player = new()
		{
			Name = systemName,
			_loop = loop,
			_startDelay = delay
		};
		player._rng = new HextechKiwiVfxRng(player.GetInstanceId());
		player._time = -delay;
		foreach (HextechKiwiVfxEmitter definition in definitions.OrderBy(static emitter => emitter.Pass))
		{
			float pixelsPerUnit = definition.Space == "button" ? buttonPixelsPerUnit : cardPixelsPerUnit;
			EmitterState? state = player.CreateEmitter(definition, pixelsPerUnit);
			if (state != null)
			{
				player._emitters.Add(state);
				player.AddChild(state.Node);
			}
		}

		if (player._emitters.Count == 0)
		{
			player.QueueFree();
			return null;
		}

		return player;
	}

	/// <summary>从头重播（按钮常驻光重新激活时用）。</summary>
	internal void Restart()
	{
		_time = -_startDelay;
		foreach (EmitterState state in _emitters)
		{
			state.Reset();
		}
	}

	public override void _Process(double delta)
	{
		Advance((float)Math.Min(delta, 0.1));
	}

	private void Advance(float delta)
	{
		_time += delta;
		if (_time < 0f)
		{
			return;
		}

		Vector2 origin = Size * 0.5f;
		bool finished = true;
		foreach (EmitterState state in _emitters)
		{
			if (!state.Advance(this, _time, delta))
			{
				finished = false;
			}
			state.Upload(origin);
		}

		if (!finished)
		{
			return;
		}

		if (_loop)
		{
			Restart();
		}
		else
		{
			QueueFree();
		}
	}

	private EmitterState? CreateEmitter(HextechKiwiVfxEmitter definition, float pixelsPerUnit)
	{
		Texture2D? baseTexture = LoadTexture(definition.Texture);
		if (baseTexture == null)
		{
			return null;
		}

		ShaderMaterial material = new()
		{
			Shader = definition.Additive ? AdditiveShader.Value : MixShader.Value
		};
		material.SetShaderParameter("base_texture", baseTexture);
		material.SetShaderParameter("base_address", definition.AddressBase);
		material.SetShaderParameter("uv_scale", ToVector2(definition.UvScale, 1f));
		if (definition.UvScroll != null)
		{
			material.SetShaderParameter("uv_scroll", RangeMin(definition.UvScroll));
		}
		if (definition.UvOffset != null)
		{
			material.SetShaderParameter("uv_offset_min", RangeMin(definition.UvOffset));
			material.SetShaderParameter("uv_offset_max", RangeMax(definition.UvOffset));
		}

		if (definition.Mult is { } mult && LoadTexture(mult.Texture) is { } multTexture)
		{
			material.SetShaderParameter("use_mult", true);
			material.SetShaderParameter("mult_texture", multTexture);
			material.SetShaderParameter("mult_address", mult.Address);
			material.SetShaderParameter("mult_scale", RangeMin(mult.Scale));
			material.SetShaderParameter("mult_scroll", RangeMin(mult.Scroll));
			material.SetShaderParameter("mult_offset_min", RangeMin(mult.Offset));
			material.SetShaderParameter("mult_offset_max", RangeMax(mult.Offset));
			material.SetShaderParameter("mult_rotation", Mathf.DegToRad(mult.Rotation.SampleScalar(0f)));
			material.SetShaderParameter("mult_rotate_rate", Mathf.DegToRad(mult.RotateRate.SampleScalar(0f)));
		}

		if (definition.Erosion is { } erosion && LoadTexture(erosion.Texture) is { } erosionTexture)
		{
			material.SetShaderParameter("use_erosion", true);
			material.SetShaderParameter("erosion_texture", erosionTexture);
			material.SetShaderParameter("erosion_slice", Math.Max(0.02f, erosion.Slice));
			material.SetShaderParameter("erosion_mixer", ToColor(erosion.Mixer));
		}

		if (definition.ColorTexture != null && LoadTexture(definition.ColorTexture) is { } colorTexture)
		{
			material.SetShaderParameter("use_color_texture", true);
			material.SetShaderParameter("color_texture", colorTexture);
			material.SetShaderParameter("color_texture_vertical", colorTexture.GetHeight() > colorTexture.GetWidth());
		}

		MultiMesh multiMesh = new()
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
			UseColors = true,
			UseCustomData = true,
			Mesh = UnitQuad.Value
		};
		multiMesh.InstanceCount = 16;
		multiMesh.VisibleInstanceCount = 0;
		MultiMeshInstance2D node = new()
		{
			Name = definition.Name,
			Multimesh = multiMesh,
			Texture = baseTexture,
			Material = material
		};
		return new EmitterState(definition, node, multiMesh, pixelsPerUnit);
	}

	private static Texture2D? LoadTexture(string relativePath)
	{
		return HextechTextures.LoadUiTexture(HextechAssets.ImageRoot + relativePath + ".png");
	}

	private static HextechKiwiVfxLibrary? LoadLibrary()
	{
		try
		{
			string json = Godot.FileAccess.GetFileAsString(HextechAssets.KiwiSelectionVfxDataPath);
			if (string.IsNullOrEmpty(json))
			{
				HextechLog.Warn("UI", $"Kiwi vfx data missing: {HextechAssets.KiwiSelectionVfxDataPath}");
				return null;
			}

			return HextechKiwiVfxLibrary.Parse(json);
		}
		catch (Exception ex)
		{
			HextechLog.Warn("UI", $"Kiwi vfx data failed to load: {ex.Message}");
			return null;
		}
	}

	private static ArrayMesh BuildUnitQuad()
	{
		Godot.Collections.Array arrays = [];
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = new Vector2[]
		{
			new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f)
		};
		arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
		{
			new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
		};
		arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
		ArrayMesh mesh = new();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private static Vector2 ToVector2(float[] value, float fallback)
	{
		return new Vector2(value.Length > 0 ? value[0] : fallback, value.Length > 1 ? value[1] : fallback);
	}

	private static Color ToColor(float[] value)
	{
		return new Color(
			value.Length > 0 ? value[0] : 0f,
			value.Length > 1 ? value[1] : 0f,
			value.Length > 2 ? value[2] : 0f,
			value.Length > 3 ? value[3] : 0f);
	}

	private static Vector2 RangeMin(HextechKiwiVfxValue value)
	{
		return new Vector2(value.BirthRange(0).Min, value.BirthRange(1).Min);
	}

	private static Vector2 RangeMax(HextechKiwiVfxValue value)
	{
		return new Vector2(value.BirthRange(0).Max, value.BirthRange(1).Max);
	}

	private struct Particle
	{
		public Vector2 Position;
		public Vector2 Velocity;
		public Vector2 Drag;
		public Vector2 Scale;
		public Color BirthColor;
		public float Rotation;
		public float Age;
		public float Lifetime;
		public float Seed;
	}

	private sealed class EmitterState
	{
		private readonly HextechKiwiVfxEmitter _definition;
		private readonly MultiMesh _multiMesh;
		private readonly float _pixelsPerUnit;
		private readonly List<Particle> _particles = [];
		private float _accumulator;
		private bool _fired;

		public EmitterState(HextechKiwiVfxEmitter definition, MultiMeshInstance2D node, MultiMesh multiMesh, float pixelsPerUnit)
		{
			_definition = definition;
			Node = node;
			_multiMesh = multiMesh;
			_pixelsPerUnit = pixelsPerUnit;
			Reset();
		}

		public MultiMeshInstance2D Node { get; }

		public void Reset()
		{
			_particles.Clear();
			_accumulator = 0.999f;
			_fired = false;
		}

		/// <summary>推进发射与粒子；返回该发射器是否已经发射完毕且没有存活粒子。</summary>
		public bool Advance(HextechKiwiVfxPlayer player, float systemTime, float delta)
		{
			HextechKiwiVfxEmitter definition = _definition;
			float emitterTime = systemTime - definition.Delay;
			bool emitting = false;
			if (emitterTime >= 0f)
			{
				if (definition.Single)
				{
					if (!_fired)
					{
						_fired = true;
						int count = Math.Max(1, (int)MathF.Round(definition.Rate.SampleBirthScalar(0f, ref player._rng)));
						for (int i = 0; i < count; i++)
						{
							Spawn(player, 0f);
						}
					}
				}
				else if (definition.Lifetime is not float lifetime || emitterTime <= lifetime)
				{
					emitting = true;
					float progress = definition.Lifetime is float life && life > 0f ? Math.Clamp(emitterTime / life, 0f, 1f) : 0f;
					_accumulator += definition.Rate.SampleScalar(progress) * delta;
					while (_accumulator >= 1f)
					{
						_accumulator -= 1f;
						Spawn(player, progress);
					}
				}
			}
			else
			{
				emitting = true;
			}

			Span<float> value = stackalloc float[4];
			for (int i = _particles.Count - 1; i >= 0; i--)
			{
				Particle particle = _particles[i];
				particle.Age += delta;
				float lifeProgress = particle.Age / particle.Lifetime;
				if (lifeProgress >= 1f)
				{
					_particles.RemoveAt(i);
					continue;
				}

				if (definition.Acceleration != null)
				{
					definition.Acceleration.Sample(lifeProgress, value);
					particle.Velocity += new Vector2(value[0], value[1]) * delta;
				}
				particle.Velocity -= particle.Velocity * new Vector2(
					Math.Min(1f, particle.Drag.X * delta),
					Math.Min(1f, particle.Drag.Y * delta));
				Vector2 velocity = particle.Velocity;
				if (definition.Velocity != null)
				{
					definition.Velocity.Sample(lifeProgress, value);
					velocity += new Vector2(value[0], value[1]);
				}
				particle.Position += velocity * delta;
				_particles[i] = particle;
			}

			return !emitting && _particles.Count == 0;
		}

		public void Upload(Vector2 origin)
		{
			if (_particles.Count > _multiMesh.InstanceCount)
			{
				_multiMesh.InstanceCount = Math.Min(MaxParticlesPerEmitter, Math.Max(_particles.Count, _multiMesh.InstanceCount * 2));
			}

			HextechKiwiVfxEmitter definition = _definition;
			Span<float> value = stackalloc float[4];
			int count = Math.Min(_particles.Count, _multiMesh.InstanceCount);
			for (int i = 0; i < count; i++)
			{
				Particle particle = _particles[i];
				float lifeProgress = Math.Clamp(particle.Age / particle.Lifetime, 0f, 1f);
				Vector2 scale = particle.Scale;
				if (definition.Scale != null)
				{
					definition.Scale.Sample(lifeProgress, value);
					scale *= definition.UniformScale ? new Vector2(value[0], value[0]) : new Vector2(value[0], value[1]);
				}

				Color color = particle.BirthColor;
				if (definition.Color != null)
				{
					definition.Color.Sample(lifeProgress, value);
					color *= new Color(value[0], value[1], value[2], value[3]);
				}

				float erosionThreshold = definition.Erosion?.Drive?.SampleScalar(lifeProgress) ?? lifeProgress;
				// 原版粒子 Y 轴朝上，界面 Y 轴朝下；四边形尺寸是出生缩放的两倍（半宽）。
				Vector2 position = origin + new Vector2(particle.Position.X, -particle.Position.Y) * _pixelsPerUnit;
				Vector2 size = new(
					Math.Max(0f, scale.X) * 2f * _pixelsPerUnit,
					Math.Max(0f, scale.Y) * 2f * _pixelsPerUnit);
				_multiMesh.SetInstanceTransform2D(i, new Transform2D(-particle.Rotation, size, 0f, position));
				_multiMesh.SetInstanceColor(i, color);
				_multiMesh.SetInstanceCustomData(i, new Color(lifeProgress, particle.Age, erosionThreshold, particle.Seed));
			}

			_multiMesh.VisibleInstanceCount = count;
		}

		private void Spawn(HextechKiwiVfxPlayer player, float progress)
		{
			if (_particles.Count >= MaxParticlesPerEmitter)
			{
				return;
			}

			HextechKiwiVfxEmitter definition = _definition;
			ref HextechKiwiVfxRng rng = ref player._rng;
			Span<float> value = stackalloc float[4];
			Particle particle = new();

			definition.Position.SampleBirth(progress, ref rng, value);
			particle.Position = new Vector2(value[0], value[1]) + SpawnOffset(definition.Shape, ref rng);

			definition.BirthVelocity.SampleBirth(progress, ref rng, value);
			particle.Velocity = new Vector2(value[0], value[1]);

			if (definition.Drag != null)
			{
				definition.Drag.SampleBirth(progress, ref rng, value);
				particle.Drag = new Vector2(value[0], value[1]);
			}

			definition.BirthScale.SampleBirth(progress, ref rng, value);
			particle.Scale = definition.UniformScale ? new Vector2(value[0], value[0]) : new Vector2(value[0], value[1]);

			if (definition.BirthRotation != null)
			{
				definition.BirthRotation.SampleBirth(progress, ref rng, value);
				particle.Rotation = Mathf.DegToRad(value[0]);
			}

			definition.BirthColor.SampleBirth(progress, ref rng, value);
			particle.BirthColor = new Color(value[0], value[1], value[2], value[3]);
			particle.Lifetime = Math.Max(0.01f, definition.ParticleLifetime.SampleBirthScalar(progress, ref rng));
			particle.Seed = rng.NextUnit();
			_particles.Add(particle);
		}

		private static Vector2 SpawnOffset(HextechKiwiVfxShape? shape, ref HextechKiwiVfxRng rng)
		{
			if (shape == null)
			{
				return Vector2.Zero;
			}

			switch (shape.Type)
			{
				case "box":
				{
					float x = shape.Size.Length > 0 ? shape.Size[0] : 0f;
					float y = shape.Size.Length > 1 ? shape.Size[1] : 0f;
					return new Vector2((rng.NextUnit() * 2f - 1f) * x, (rng.NextUnit() * 2f - 1f) * y);
				}
				case "sphere":
				{
					float angle = rng.NextUnit() * Mathf.Tau;
					float distance = MathF.Sqrt(rng.NextUnit()) * shape.Radius;
					return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
				}
				case "offset" when shape.Offset != null:
				{
					Span<float> value = stackalloc float[4];
					shape.Offset.SampleBirth(0f, ref rng, value);
					return new Vector2(value[0], value[1]);
				}
				default:
					return Vector2.Zero;
			}
		}
	}
}
