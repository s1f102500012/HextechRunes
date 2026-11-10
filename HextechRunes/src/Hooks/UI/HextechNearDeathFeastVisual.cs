using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

/// <summary>濒死狂宴:我方持有者与(敌方海克斯激活时的)敌人在濒死时身后的红色心跳辉光。</summary>
internal sealed class HextechNearDeathFeastVisual : HextechBehindCreatureVisual
{
	private const string NodeName = "HextechRunes_NearDeathFeastAura";
	private const string LogTag = "NearDeathFeast";
	private const float BodyHeightFactor = 0.64f;
	private const float GlowWidthFactor = 2.45f;
	private const float SurgeIntensityStep = 0.045f;

	private Sprite2D? _glow;
	private Sprite2D? _rays;
	private float _time;
	private float _surge;
	private bool _wasActive;
	private float _lastIntensity;

	private HextechNearDeathFeastVisual(NCreature creature)
		: base(creature, LogTag, "Near-Death Feast visual")
	{
	}

	internal static void TryAttach(NCreature? creature)
	{
		// 我方(带符文的玩家)与敌方(敌方海克斯激活时的所有敌人)都挂:轮询各自的濒死强度。
		TryAttach(
			creature,
			LogTag,
			"Near-Death Feast visual",
			static node => node.Hitbox != null && node.Entity != null,
			static node => new HextechNearDeathFeastVisual(node));
	}

	private static bool TryGetIntensity(NCreature creature, out float intensity)
	{
		intensity = 0f;
		return creature.Entity != null
			&& creature.Entity.IsAlive
			&& (NearDeathFeastRune.TryGetFeastIntensity(creature.Entity, out intensity)
				|| HextechEnemyNearDeath.TryGetFeastIntensity(creature.Entity, out intensity));
	}

	protected override bool Start()
	{
		Texture2D? glowTexture = HextechTextures.LoadUiTexture(HextechAssets.NearDeathFeastGlowPath);
		if (glowTexture == null || !TryCreateRoot(NodeName, visible: false) || Root is not { } root)
		{
			return false;
		}

		// 原版 glowie / glowie2 共用这张红光纹理，分别为普通混合和加色；只用这两层，不叠加程序生成的圆环。
		_glow = CreateSprite(root, "FeastGlow", glowTexture, additive: false);
		_rays = CreateSprite(root, "FeastRays", glowTexture, additive: true);
		UpdateTransform(1f);
		return true;
	}

	protected override bool Tick()
	{
		if (Root is not { } root)
		{
			return false;
		}

		bool active = TryGetIntensity(Creature, out float intensity);
		root.Visible = active;

		if (active && !_wasActive)
		{
			_surge = 1f;
			_lastIntensity = intensity;
		}
		else if (active && intensity > _lastIntensity + SurgeIntensityStep)
		{
			_surge = 0.55f + intensity * 0.3f;
			_lastIntensity = intensity;
		}
		else if (!active)
		{
			_lastIntensity = 0f;
			_surge = 0f;
		}

		_wasActive = active;

		if (active)
		{
			EnsureRenderOrder();
			float dt = ClampedFrameDelta(root);
			_time = Mathf.PosMod(_time + dt, 3600f);
			_surge = Mathf.MoveToward(_surge, 0f, dt * 2.8f);
			Animate(intensity);
		}

		return true;
	}

	private void Animate(float intensity)
	{
		// 心跳:越濒死跳得越快、越重。两段"咚-咚"(lub-dub)而非匀速正弦,更有生命体征感。
		float rate = Mathf.Lerp(1.05f, 1.95f, intensity);
		float beat = Heartbeat(_time * rate);

		float glowScale = 1f + beat * Mathf.Lerp(0.05f, 0.12f, intensity) + _surge * 0.12f;
		UpdateTransform(glowScale);

		if (_glow != null)
		{
			float glowAlpha = Mathf.Lerp(0.35f, 0.65f, intensity) + beat * 0.15f + _surge * 0.20f;
			_glow.Modulate = Colors.White with { A = glowAlpha };
		}

		if (_rays != null)
		{
			float rayAlpha = Mathf.Lerp(0.08f, 0.20f, intensity) + beat * 0.12f + _surge * 0.25f;
			_rays.Modulate = Colors.White with { A = rayAlpha };
			_rays.Rotation = _time * 0.12f;
		}
	}

	private void UpdateTransform(float glowScalePulse)
	{
		if (Root == null || !GodotObject.IsInstanceValid(Creature) || Creature.Hitbox == null)
		{
			return;
		}

		Vector2 top = Creature.GetTopOfHitbox();
		Vector2 bottom = Creature.GetBottomOfHitbox();
		Root.GlobalPosition = bottom.Lerp(top, BodyHeightFactor);

		float width = Mathf.Clamp(Creature.Hitbox.Size.X, 120f, 360f);
		ScaleSprite(_glow, width * GlowWidthFactor * glowScalePulse);
		ScaleSprite(_rays, width * GlowWidthFactor * glowScalePulse);
	}

	private static Sprite2D CreateSprite(Node2D parent, string name, Texture2D texture, bool additive)
	{
		Sprite2D sprite = new()
		{
			Name = name,
			Texture = texture,
			Centered = true,
			Modulate = Colors.White with { A = 0f }
		};
		if (additive)
		{
			sprite.Material = HextechAuraLayer.CreateAdditiveMaterial();
		}

		parent.AddChildSafely(sprite);
		return sprite;
	}

	private static void ScaleSprite(Sprite2D? sprite, float diameter)
	{
		if (sprite?.Texture is { } texture)
		{
			sprite.Scale = Vector2.One * (diameter / Math.Max(texture.GetWidth(), 1));
		}
	}

	private static float Heartbeat(float t)
	{
		float phase = Mathf.PosMod(t, 1f);
		float lub = Bump(phase, 0f, 0.055f);
		float dub = Bump(phase, 0.17f, 0.065f) * 0.78f;
		return Mathf.Clamp(lub + dub, 0f, 1f);
	}

	private static float Bump(float x, float center, float sigma)
	{
		float d = x - center;
		if (d > 0.5f)
		{
			d -= 1f;
		}
		else if (d < -0.5f)
		{
			d += 1f;
		}

		return Mathf.Exp(-(d * d) / (2f * sigma * sigma));
	}
}
