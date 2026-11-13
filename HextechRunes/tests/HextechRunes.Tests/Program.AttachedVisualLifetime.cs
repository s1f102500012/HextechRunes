using Godot;

namespace HextechRunes.Tests;

internal static partial class Program
{
	[HextechTest]
	private static void AttachedVisualLifetimeEndsOnCombatEndRoomSwitchAndTreeExit()
	{
		// 挂载早于开战:战斗尚未进行、房间指针尚未切到本房间时都应继续。
		HextechAttachmentLifetime persistent = default;
		Expect(persistent.Observe(creatureInTree: true, combatInProgress: false, roomIsCurrent: false, endsWithCombat: true),
			"attachment created before combat start and room switch should keep running");
		Expect(persistent.Observe(creatureInTree: true, combatInProgress: true, roomIsCurrent: true, endsWithCombat: true),
			"attachment should run during combat in the current room");
		Expect(!persistent.Observe(creatureInTree: true, combatInProgress: false, roomIsCurrent: true, endsWithCombat: true),
			"persistent attachment should stop once the combat it saw in progress has ended (victory or defeat overlay)");

		HextechAttachmentLifetime oneShot = default;
		Expect(oneShot.Observe(creatureInTree: true, combatInProgress: true, roomIsCurrent: true, endsWithCombat: false), "one-shot effect runs");
		Expect(oneShot.Observe(creatureInTree: true, combatInProgress: false, roomIsCurrent: true, endsWithCombat: false),
			"one-shot effects and attachments that opt out keep running past combat end");
		Expect(!oneShot.Observe(creatureInTree: true, combatInProgress: false, roomIsCurrent: false, endsWithCombat: false),
			"every attachment stops after its room stops being the current combat room");

		HextechAttachmentLifetime outsideRoom = default;
		Expect(outsideRoom.Observe(creatureInTree: true, combatInProgress: false, roomIsCurrent: null, endsWithCombat: true),
			"creature without an owning combat room skips the room rule");
		Expect(!outsideRoom.Observe(creatureInTree: false, combatInProgress: false, roomIsCurrent: null, endsWithCombat: true),
			"attachment stops when its creature leaves the scene tree");
	}

	[HextechTest]
	private static void BurnVisualBoneFollowReacquiresRebuiltSkeletonAndCapsRetries()
	{
		const int max = 8;
		Equal(HextechBoneFollowAction.Read, HextechBoneFollow.Decide(false, true, true, true, true, 0, max), "same sprite and skeleton: read bones");
		Equal(HextechBoneFollowAction.Reacquire, HextechBoneFollow.Decide(false, true, true, true, false, 0, max),
			"skeleton rebuilt on the same sprite instance must not read the cached bones");
		Equal(HextechBoneFollowAction.Reacquire, HextechBoneFollow.Decide(false, true, true, false, false, 0, max), "sprite replaced: reacquire");
		Equal(HextechBoneFollowAction.Reacquire, HextechBoneFollow.Decide(false, true, false, false, false, 0, max), "sprite freed: reacquire");
		Equal(HextechBoneFollowAction.Disable, HextechBoneFollow.Decide(false, true, true, true, false, max, max),
			"repeated rebuilds should permanently disable bone follow");
		Equal(HextechBoneFollowAction.Skip, HextechBoneFollow.Decide(true, true, true, true, true, 0, max), "disabled follow never reads bones");
		Equal(HextechBoneFollowAction.Skip, HextechBoneFollow.Decide(false, false, false, false, false, 0, max), "no cached bones: nothing to read");

		Expect(HextechBoneFollow.IsUnsetReading(Vector2.Zero), "unset SpineBone wrappers read back as exactly (0,0)");
		Expect(!HextechBoneFollow.IsUnsetReading(new Vector2(0f, -12.5f)), "a live bone on the vertical axis is not unset");
		Expect(!HextechBoneFollow.IsUnsetReading(new Vector2(3f, 0f)), "a live bone on the ground line is not unset");
	}
}
