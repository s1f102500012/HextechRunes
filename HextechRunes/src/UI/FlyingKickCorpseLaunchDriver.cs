using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

internal sealed class FlyingKickCorpseLaunchDriver
{
	private const float Lifetime = 1.25f;
	private const float InitialVelocityX = 1080f;
	private const float DampingPerFrame = 0.92f;

	private static readonly HashSet<uint> PendingCombatIds = [];
	private static readonly HashSet<Creature> PendingCreatureRefs = new(ReferenceEqualityComparer.Instance);
	private static readonly HashSet<ulong> ActiveCreatureNodes = [];

	private readonly NCreature _creature;
	private NCreatureVisuals? _visuals;
	private Vector2 _velocity = new(InitialVelocityX, 0f);
	private float _elapsed;

	private FlyingKickCorpseLaunchDriver(NCreature creature)
	{
		_creature = creature;
	}

	internal static void MarkPending(Creature creature)
	{
		if (creature.CombatId is uint combatId)
		{
			PendingCombatIds.Add(combatId);
			return;
		}

		PendingCreatureRefs.Add(creature);
	}

	internal static void MarkPendingUntilConsumed(Creature creature)
	{
		MarkPending(creature);
		TaskHelper.RunSafely(ClearPendingAfterDelay(creature));
	}

	internal static bool TryConsumePending(Creature? creature)
	{
		if (creature == null)
		{
			return false;
		}

		if (creature.CombatId is uint combatId)
		{
			return PendingCombatIds.Remove(combatId);
		}

		return PendingCreatureRefs.Remove(creature);
	}

	internal static void ClearPending(Creature creature)
	{
		if (creature.CombatId is uint combatId)
		{
			PendingCombatIds.Remove(combatId);
		}

		PendingCreatureRefs.Remove(creature);
	}

	private static async Task ClearPendingAfterDelay(Creature creature)
	{
		await Task.Delay(TimeSpan.FromSeconds(3));
		ClearPending(creature);
	}

	internal static void TryAttach(NCreature creature)
	{
		// 只由 FlyingKickCorpseLaunchPatch 调用;Android 上该补丁的 Prepare 返回 false,不会走到这里。
		try
		{
			if (!GodotObject.IsInstanceValid(creature) || creature.Entity?.IsMonster != true)
			{
				return;
			}

			ulong creatureInstanceId = creature.GetInstanceId();
			if (!ActiveCreatureNodes.Add(creatureInstanceId))
			{
				return;
			}

			FlyingKickCorpseLaunchDriver driver = new(creature);
			if (!driver.Start())
			{
				ActiveCreatureNodes.Remove(creatureInstanceId);
				return;
			}

			TaskHelper.RunSafely(driver.RunAsync(creatureInstanceId));
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Could not attach Flying Kick corpse launch driver: {ex.Message}");
		}
	}

	private bool Start()
	{
		_visuals = _creature.Visuals;
		return GodotObject.IsInstanceValid(_visuals);
	}

	private async Task RunAsync(ulong creatureInstanceId)
	{
		try
		{
			while (_elapsed < Lifetime)
			{
				if (!GodotObject.IsInstanceValid(_creature) || !GodotObject.IsInstanceValid(_visuals))
				{
					return;
				}

				float dt = Mathf.Min(Mathf.Max((float)_visuals.GetProcessDeltaTime(), 1f / 120f), 0.05f);
				_elapsed += dt;

				_creature.Position += _velocity * dt;
				_velocity *= MathF.Pow(DampingPerFrame, dt * 60f);

				if (!await HextechGodotAsync.AwaitProcessFrameAsync(_visuals))
				{
					return;
				}
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("Mayhem", $"Flying Kick corpse launch driver stopped after runtime error: {ex.Message}");
		}
		finally
		{
			ActiveCreatureNodes.Remove(creatureInstanceId);
		}
	}
}
