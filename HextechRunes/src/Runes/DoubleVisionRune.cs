using MegaCrit.Sts2.Core.Models.Relics;
using System.Runtime.CompilerServices;

namespace HextechRunes;

public sealed partial class DoubleVisionRune : HextechRelicBase
{
	private static readonly AsyncLocal<CardRewardTracker?> CurrentCardRewardTracker = new();
	private static readonly AsyncLocal<int> CommandDuplicationSuppressionDepth = new();
	private static readonly AsyncLocal<EventRelicTransaction?> CurrentEventRelicTransaction = new();
	private static readonly AsyncLocal<int> EventRelicObtainDepth = new();
	private static readonly AsyncLocal<DustyTome?> SuppressedDustyTomeAfterObtained = new();
	private static readonly AsyncLocal<Player?> PaelsWingSacrificeOwner = new();
	private static readonly ConditionalWeakTable<EventRoom, EventRelicTransactionBatch> EventRelicTransactionBatches = new();
	// 原版 GoldReward 私有字段 _wasGoldStolenBack(0.107.1~0.111.0)；缺失时进启动摘要，复制金币按"非夺回"处理。
	private static readonly FieldInfo? GoldRewardWasStolenBackField = HextechHookReflection.TryGetField(typeof(GoldReward), "_wasGoldStolenBack");

	// 旧版本存档兼容占位：旧版本把待复制的事件遗物写进此字段；现由 EventOption.Chosen 外层事务
	// 在原选项 Task 完成后立即结算，不再写入，读到的旧值直接丢弃。名称与类型须保留。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedPendingEventRelicIdsJson
	{
		get => "";
		set { }
	}

	public override async Task AfterRewardTaken(Player player, Reward reward)
	{
		// 联机时奖励领取在各端都会触发本 hook:必须只在持有者本地端复制并广播,
		// 否则 N 人局的 N-1 个远端各复制一份(玩家实测 4 人局一瓶药水复制成三瓶,塞爆药水栏黑屏)。
		if (!ReferenceEquals(player, Owner)
			|| player.Creature.IsDead
			|| !ShouldDuplicateForPlayer(player))
		{
			return;
		}

		switch (reward)
		{
			case GoldReward goldReward:
				await DuplicateGoldReward(player, goldReward);
				break;
			case PotionReward potionReward:
				await DuplicatePotionReward(player, potionReward);
				break;
			case HextechForgeChoiceReward forgeReward:
				await DuplicateForgeReward(player, forgeReward);
				break;
		}
	}
}
