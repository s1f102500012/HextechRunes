using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace HextechRunes;

internal static class HextechMonsterInteractionPolicy
{
	public static bool IsTrueCombatDeath(Creature creature)
	{
		return IsTrueCombatDeath(creature, out _);
	}

	public static bool IsTrueCombatDeath(Creature creature, [NotNullWhen(true)] out HextechCombatState? combatState)
	{
		combatState = creature.CombatState;
		return combatState != null
			&& !IsBossPhaseTransitionDeath(creature)
			&& Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature);
	}

	public static bool IsBossPhaseTransitionDeath(Creature creature)
	{
		HextechCombatState? combatState = creature.CombatState;
		return combatState != null
			&& creature.Monster is TestSubject
			&& !Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature);
	}

	public static bool ShouldPreserveFromBuffRemoval(PowerModel power)
	{
		return IsStructuralMonsterBuff(power)
			|| IsEnemyHostedPlayerRelationBuff(power)
			// 这两个 power 保存被偷金币/卡牌，并在怪物死亡时负责归还；提前剥除会永久丢失战利品。
			|| power is HeistPower or SwipePower;
	}

	// 结构性怪物 buff = 正常挂在敌方身上，且剥除会让遭遇脚本、战斗结束或死亡流程断裂的 power。
	// 玩家牌、玩家协作、Osty 和怪物施加给玩家的 power 不属于敌方剥除策略，不能为图省事混进此处。
	// 曾把全部"动画/形态状态机"类也列入(防幽灵鳗 SkittishPower 卡死),现已逐个核验收窄:
	//  - Skittish 的卡死根因是 Block 出场动画悬空,已由 RemoveMonsterBuffSafely 补出场后安全剥除;
	//  - Smoggy/Burrowed/Hibernate/CurlUp/HardenedShell/SentryMode/Shadowmeld/Shroud/Sneaky/
	//    Covered/Surprise/Soar/Flutter/SpectrumShift 反编译核验为一次性动画或自带 AfterRemoved 清理,可正常剥除;
	//  - Asleep/Slumber 与怪物唤醒脚本硬耦合((LagavulinMatriarch/SlumberingBeetle)强转+IsAwake/WakeUpMove),
	//    直接剥除会绕过唤醒流程破坏行动 AI,保留跳过。
	// 数值成长类(力量/CreativeAi 等)不在此列,允许正常剥除。
	public static bool IsStructuralMonsterBuff(PowerModel power)
	{
		return power is AdaptablePower
			// 怪物唤醒脚本耦合
			or AsleepPower
			or SlumberPower
			// 沙坑同时管理沙虫倒计时、玩家站位与倒计时结束处决，复制或提前剥除都会破坏遭遇流程。
			or SandpitPower
			// 遭遇脚本/时限
			or BattlewornDummyTimeLimitPower
			// 战斗结束/死亡流程
			or MinionPower
			or InfestedPower;
	}

	// BackAttack 标记正常挂在敌人身上，但保存的是玩家 Surrounded 的朝向关系。
	// 剥除不会伤害敌人，反而会让玩家的背刺加成丢失，因此按玩家关系状态保护。
	private static bool IsEnemyHostedPlayerRelationBuff(PowerModel power)
	{
		return power is BackAttackLeftPower or BackAttackRightPower;
	}

	/// <summary>
	/// 安全剥除怪物增益:对有"进场动画等待出场"状态机的 power(幽灵鳗 Skittish),先补出场动画再移除,
	/// 否则 Spine 状态机停在 Block 态、下一次动画触发永远等不到(玩家实报"感受燃烧打四鳗卡死"的根因)。
	/// 感受燃烧/升级:暴露的剥除一律走这里。
	/// </summary>
	public static async Task RemoveMonsterBuffSafely(PowerModel power)
	{
		if (power is SkittishPower { HasGainedBlockThisTurn: true } && power.Owner != null)
		{
			SfxCmd.Play("event:/sfx/enemy/enemy_attacks/phantasmal_gardeners/phantasmal_gardeners_extend");
			await CreatureCmd.TriggerAnim(power.Owner, "BlockEnd", 0.15f);
		}

		// 记下"被本模组剥过机制类增益"的怪物:原版动画机条件可能无空判硬取该 power,
		// 动画触发安全护栏只对这些怪物吞 NRE,其它来源的空引用照常抛出。
		if (power.Owner != null)
		{
			HextechAnimTriggerSafetyHooks.MarkBuffStripped(power.Owner);
		}

		await PowerCmd.Remove(power);
	}
}
