using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

/// <summary>
/// 敌方翱翔（HextechSoarPower）的纯表现：持有期间立绘离地漂浮并轻微摇晃，失去翱翔或死亡后落回原位。
/// 位置与角度都按增量叠加到立绘上，不覆盖原版受击抖动等对同一节点的动画；不读写任何对局状态。
/// </summary>
internal sealed class HextechSoarFloatDriver
{
	private const float LiftPixels = 36f;
	private const float BobPixels = 7f;
	private const float SwayRadians = 0.035f;
	private const float BobPeriodSeconds = 2.4f;
	private const float SwayPeriodSeconds = 3.1f;
	private const float RiseSeconds = 0.6f;
	private const float LandSeconds = 0.4f;

	private static readonly HashSet<ulong> ActiveCreatureNodes = [];

	private readonly Creature _creature;
	private readonly NCreature _node;
	private float _appliedOffset;
	private float _appliedRotation;

	private HextechSoarFloatDriver(Creature creature, NCreature node)
	{
		_creature = creature;
		_node = node;
	}

	internal static void EnsureFloating(Creature creature)
	{
		try
		{
			if (NCombatRoom.Instance?.GetCreatureNode(creature) is not NCreature node
				|| !GodotObject.IsInstanceValid(node)
				|| !ActiveCreatureNodes.Add(node.GetInstanceId()))
			{
				return;
			}

			ulong nodeId = node.GetInstanceId();
			TaskHelper.RunSafely(new HextechSoarFloatDriver(creature, node).RunAsync(nodeId));
		}
		catch (Exception ex)
		{
			HextechLog.Warn("SoarFloat", $"Could not start soar float: {ex.Message}");
		}
	}

	private bool IsSoaring => _creature.IsAlive && _creature.GetPowerAmount<HextechSoarPower>() > 0;

	private async Task RunAsync(ulong nodeId)
	{
		try
		{
			float elapsed = 0f;
			float lift = 0f;
			while (true)
			{
				if (!GodotObject.IsInstanceValid(_node) || _node.Visuals is not NCreatureVisuals visuals || !GodotObject.IsInstanceValid(visuals))
				{
					return;
				}

				float dt = Mathf.Clamp((float)visuals.GetProcessDeltaTime(), 1f / 240f, 0.05f);
				elapsed += dt;
				bool soaring = IsSoaring;
				lift = soaring
					? Mathf.Min(1f, lift + dt / RiseSeconds)
					: Mathf.Max(0f, lift - dt / LandSeconds);
				float eased = lift * lift * (3f - 2f * lift);
				float offset = -eased * (LiftPixels + BobPixels * Mathf.Sin(elapsed * Mathf.Tau / BobPeriodSeconds));
				float rotation = eased * SwayRadians * Mathf.Sin(elapsed * Mathf.Tau / SwayPeriodSeconds);
				Apply(visuals, offset, rotation);
				if (!soaring && lift <= 0f)
				{
					Apply(visuals, 0f, 0f);
					return;
				}

				SceneTree tree = visuals.GetTree();
				if (!GodotObject.IsInstanceValid(tree))
				{
					return;
				}

				await visuals.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			}
		}
		catch (Exception ex)
		{
			HextechLog.Warn("SoarFloat", $"Soar float stopped after runtime error: {ex.Message}");
		}
		finally
		{
			ActiveCreatureNodes.Remove(nodeId);
		}
	}

	private void Apply(NCreatureVisuals visuals, float offset, float rotation)
	{
		visuals.Position += new Vector2(0f, offset - _appliedOffset);
		visuals.Rotation += rotation - _appliedRotation;
		_appliedOffset = offset;
		_appliedRotation = rotation;
	}
}
