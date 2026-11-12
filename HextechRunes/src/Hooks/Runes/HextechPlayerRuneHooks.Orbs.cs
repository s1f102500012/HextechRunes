using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

// 充能球容量、环形布局及电动力学溅射的补丁入口。
internal static partial class HextechPlayerRuneHooks
{
	private const int OrbLayoutRadiusSoftCapSlots = 10;
	private const float OrbLayoutRangeDegrees = 125f;
	private const float OrbLayoutAngleOffsetDegrees = -25f;
	private const float OrbLayoutMaxRadius = 300f;
	private const float OrbLayoutTweenSpeed = 0.45f;

	// NOrbManager._orbs（List<NOrb>）、_creatureNode（NCreature）、_curTween（Tween），0.107.1/0.111.0 原版私有字段。
	// 任一缺失时软上限布局不安装（Prepare 返回 false）、走原版。
	private static readonly FieldInfo OrbManagerOrbsField = TryGetField(typeof(NOrbManager), "_orbs")!;
	private static readonly FieldInfo OrbManagerCreatureField = TryGetField(typeof(NOrbManager), "_creatureNode")!;
	private static readonly FieldInfo OrbManagerCurrentTweenField = TryGetField(typeof(NOrbManager), "_curTween")!;
	private static readonly ConditionalWeakTable<NOrbManager, OrbLayoutFrameState> OrbLayoutFrameStates = new();

	private sealed class OrbLayoutFrameState
	{
		private ulong _processFrame = ulong.MaxValue;
		private int _capacity;
		private bool _isLocal;
		private int _orbCount;
		private NOrb[] _orbs = [];

		public bool Matches(ulong processFrame, int capacity, bool isLocal, List<NOrb> orbs)
		{
			if (_processFrame != processFrame
				|| _capacity != capacity
				|| _isLocal != isLocal
				|| _orbCount != orbs.Count)
			{
				return false;
			}

			// 容量与数量相同也可能发生激发后补位，节点序列必须完全一致才算重复布局。
			for (int i = 0; i < _orbCount; i++)
			{
				if (!ReferenceEquals(_orbs[i], orbs[i]))
				{
					return false;
				}
			}

			return true;
		}

		public void Capture(ulong processFrame, int capacity, bool isLocal, List<NOrb> orbs)
		{
			_processFrame = processFrame;
			_capacity = capacity;
			_isLocal = isLocal;
			if (_orbs.Length < orbs.Count)
			{
				int newLength = Math.Max(orbs.Count, Math.Max(16, _orbs.Length * 2));
				Array.Resize(ref _orbs, newLength);
			}

			int previousCount = _orbCount;
			_orbCount = orbs.Count;
			for (int i = 0; i < _orbCount; i++)
			{
				_orbs[i] = orbs[i];
			}

			if (_orbCount < previousCount)
			{
				Array.Clear(_orbs, _orbCount, previousCount - _orbCount);
			}
		}
	}

	internal static bool OrbTweenLayoutPrefixCore(NOrbManager __instance)
	{
		if (!TryGetOrbLayoutState(__instance, out List<NOrb> orbs, out Player? player, out int capacity)
			|| capacity <= OrbLayoutRadiusSoftCapSlots)
		{
			return true;
		}

		if (orbs.Count == 0)
		{
			return false;
		}

		bool optimizeMadScientistOverflow = player?.GetRelic<MadScientistRune>() != null;
		OrbLayoutFrameState? frameState = null;
		ulong processFrame = 0;
		if (optimizeMadScientistOverflow)
		{
			processFrame = Engine.GetProcessFrames();
			frameState = OrbLayoutFrameStates.GetValue(__instance, static _ => new OrbLayoutFrameState());
			if (frameState.Matches(processFrame, capacity, __instance.IsLocal, orbs))
			{
				return false;
			}
		}

		float angle = OrbLayoutRangeDegrees;
		float angleStep = OrbLayoutRangeDegrees / Math.Max(1, capacity - 1);
		float radius = OrbLayoutMaxRadius;
		if (!__instance.IsLocal)
		{
			radius *= 0.75f;
		}

		((Tween?)OrbManagerCurrentTweenField.GetValue(__instance))?.Kill();
		Tween tween = __instance.CreateTween().SetParallel();
		OrbManagerCurrentTweenField.SetValue(__instance, tween);

		int layoutCount = Math.Min(capacity, orbs.Count);
		int tweenedCount = ResolveTweenedOrbCount(optimizeMadScientistOverflow, capacity, orbs.Count);
		for (int i = 0; i < layoutCount; i++)
		{
			float radians = (OrbLayoutAngleOffsetDegrees - angle) * MathF.PI / 180f;
			Vector2 position = new(-MathF.Cos(radians) * radius, MathF.Sin(radians) * radius);
			if (i < tweenedCount)
			{
				tween.TweenProperty(orbs[i], "position", position, OrbLayoutTweenSpeed)
					.SetEase(Tween.EaseType.InOut)
					.SetTrans(Tween.TransitionType.Sine);
			}
			else
			{
				// 超量球沿用同一目标坐标，只省去会随球数线性膨胀的位置补间。
				orbs[i].Position = position;
			}

			angle -= angleStep;
		}

		frameState?.Capture(processFrame, capacity, __instance.IsLocal, orbs);

		return false;
	}

	internal static int ResolveTweenedOrbCount(bool optimizeMadScientistOverflow, int capacity, int orbCount)
	{
		int layoutCount = Math.Min(Math.Max(0, capacity), Math.Max(0, orbCount));
		return optimizeMadScientistOverflow
			? Math.Min(OrbLayoutRadiusSoftCapSlots, layoutCount)
			: layoutCount;
	}

	internal static bool TryGetOrbLayoutState(
		NOrbManager manager,
		out List<NOrb> orbs,
		out Player? player,
		out int capacity)
	{
		orbs = (List<NOrb>)OrbManagerOrbsField.GetValue(manager)!;
		NCreature? creature = (NCreature?)OrbManagerCreatureField.GetValue(manager);
		player = creature?.Entity.Player;
		capacity = player?.PlayerCombatState?.OrbQueue.Capacity ?? 0;
		return capacity > 0;
	}

	internal static async Task<IEnumerable<Creature>> ApplyElectrodynamicsLightningDamage(LightningOrb orb, decimal value, PlayerChoiceContext choiceContext)
	{
		List<Creature> targets = orb.CombatState.GetOpponentsOf(orb.Owner.Creature)
			.Where(static enemy => enemy.IsHittable)
			.ToList();
		if (targets.Count == 0)
		{
			return Array.Empty<Creature>();
		}

		foreach (Creature target in targets)
		{
			VfxCmd.PlayOnCreature(target, "vfx/vfx_attack_lightning");
		}

		await CreatureCmd.Damage(choiceContext, targets, value, ValueProp.Unpowered, orb.Owner.Creature);
		return targets;
	}

	// 跳过型前缀（已裁决保留，见 architecture.md）：原版 NOrbManager.TweenLayout 的半径是
	// Mathf.Lerp(225, 300, (槽位-3)/7)，超过 10 槽后外插继续放大，且逐槽补间所有槽位；没有布局 Hook。
	// 只在槽位数超过软上限时替换为半径封顶的同一环形布局（疯狂科学家另省去超量球的补间），纯本地表现层；
	// 替换体沿用原版的补间写法（杀旧补间、并行 TweenProperty position），目标 IL 由原版拷贝守卫冻结。
	[HarmonyPatch(typeof(NOrbManager), "TweenLayout")]
	[HextechPatch("rune.orb-layout-soft-cap", "充能球布局软上限")]
	internal static class OrbLayoutSoftCapPatch
	{
		[HarmonyPrepare]
		private static bool Prepare() =>
			OrbManagerOrbsField != null && OrbManagerCreatureField != null && OrbManagerCurrentTweenField != null;

		[HarmonyPrefix]
		[HarmonyPriority(Priority.Low)]
		private static bool Prefix(NOrbManager __instance)
		{
			try
			{
				return OrbTweenLayoutPrefixCore(__instance);
			}
			catch (Exception ex)
			{
				// 纯布局表现层,异常回退原版布局,不能向调用方外泄。
				HextechLog.Warn("Mayhem", $"Orb layout override failed; falling back to vanilla layout: {ex.Message}");
				return true;
			}
		}
	}
}
