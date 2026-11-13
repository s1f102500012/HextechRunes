using System.Reflection;
using HextechRunes;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

// 0.113.0 战斗侧适配的回归：各条在三个维护目标上都要成立，按当前编译目标的原版程序集核对。
internal static partial class Program
{
	/// <summary>
	/// 大刀改写的君王之剑按原版同一个变量取段数：0.113.0 把 RepeatVar 换成 HitCountVar，读旧键会抛 KeyNotFoundException。
	/// </summary>
	[HextechTest]
	private static void BigKnifeSovereignBladeHitCountReadsCurrentGameVar()
	{
		SovereignBlade blade = CreateMutableTestModel<SovereignBlade>();
		Expect(HextechV113ApiCompat.GetSovereignBladeHitCount(blade) == 1,
			$"Sovereign Blade should expose its base hit count on sts2 {ModInfo.TargetGameVersion}");
	}

	/// <summary>
	/// 棱彩蛋读取的宝箱同步器字段必须存在且类型可用，否则替换静默失效（启动摘要只会列出缺失成员）。
	/// </summary>
	[HextechTest]
	private static void PrismaticEggTreasureSynchronizerFieldsHaveUsableTypes()
	{
		BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
		FieldInfo players = (FieldInfo?)typeof(HextechTreasureRuneHooks).GetField("PlayerCollectionField", flags)?.GetValue(null)
			?? throw new InvalidOperationException("TreasureRoomRelicSynchronizer players field did not resolve");
		FieldInfo relics = (FieldInfo?)typeof(HextechTreasureRuneHooks).GetField("CurrentRelicsField", flags)?.GetValue(null)
			?? throw new InvalidOperationException("TreasureRoomRelicSynchronizer._currentRelics did not resolve");
		Expect(players.DeclaringType == typeof(TreasureRoomRelicSynchronizer), "players field belongs to the treasure synchronizer");
		Expect(typeof(IPlayerCollection).IsAssignableFrom(players.FieldType),
			$"{players.Name} ({players.FieldType.Name}) should be readable as IPlayerCollection");
		Expect(relics.FieldType.IsAssignableFrom(typeof(List<RelicModel>)) || typeof(List<RelicModel>).IsAssignableFrom(relics.FieldType),
			$"_currentRelics ({relics.FieldType.Name}) should hold List<RelicModel>");
	}

	/// <summary>
	/// 横扫复制能力时排除私有数据不随模型复制的能力：0.113.0 的穿刺（箭矢在私有数据里）；旧目标没有这个类型，不排除任何能力。
	/// </summary>
	[HextechTest]
	private static void SweepingBladeReplicationExcludesImpaledPower()
	{
		Expect(!HextechV113ApiCompat.IsPowerExcludedFromReplication(CreateMutableTestModel<WeakPower>()),
			"ordinary powers stay replicable");
		Type? impaledType = typeof(PowerModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers.ImpaledPower");
		if (impaledType == null)
		{
			return;
		}

		PowerModel impaled = (PowerModel)Activator.CreateInstance(impaledType)!;
		Expect(HextechV113ApiCompat.IsPowerExcludedFromReplication(impaled),
			"ImpaledPower keeps its arrows in private data and must not be replicated");
	}
}
