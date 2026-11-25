using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 0.10.2 的属性锻造器改动：愚者专属三个（瞄准、拉伸、箭矢）、通用四个（毒雾、冲势、黑暗、回响），以及百分比最大生命两档默认禁用。
// 需要 0.113.0 类型的战斗行为测试在 Program.Forges.Additions.V113.cs。
internal static partial class Program
{
	/// <summary>
	/// 注册内容各目标一致：愚者专属三个只对愚者可用（旧目标没有愚者，任何玩家都拿不到，界面也不列出），毒雾是黄金通用。
	/// </summary>
	[HextechTest]
	private static void ForgeAdditionsAreRegisteredWithExpectedRarityAndAvailability()
	{
		IReadOnlyDictionary<HextechRarityTier, IReadOnlyList<Type>> byRarity = HextechContentRegistry.ForgeTypesByRarity;
		Expect(byRarity[HextechRarityTier.Silver].Contains(typeof(AimForge)), "Aim forge is silver");
		Expect(byRarity[HextechRarityTier.Silver].Contains(typeof(StretchForge)), "Stretch forge is silver");
		Expect(byRarity[HextechRarityTier.Gold].Contains(typeof(ArrowForge)), "Arrow forge is gold");
		Expect(byRarity[HextechRarityTier.Gold].Contains(typeof(NoxiousFumesForge)), "Noxious Fumes forge is gold");
		Expect(byRarity[HextechRarityTier.Silver].Contains(typeof(JuggernautForge)), "Juggernaut forge is silver");
		Expect(byRarity[HextechRarityTier.Gold].Contains(typeof(DarkEmbraceForge)), "Dark Embrace forge is gold");
		Expect(byRarity[HextechRarityTier.Prismatic].Contains(typeof(EchoForge)), "Echo forge is prismatic");

		bool gameHasFool = HextechFoolCompat.IsFoolCharacterAvailable;
		Player ironclad = CreateCharacterTestPlayer(typeof(Ironclad));
		foreach (Type foolForge in new[] { typeof(AimForge), typeof(StretchForge), typeof(ArrowForge) })
		{
			Equal(gameHasFool, HextechCatalog.IsForgeTypeShownInUi(foolForge), $"{foolForge.Name} is listed only when the game has the Fool");
			HextechForgeBase forge = (HextechForgeBase)Activator.CreateInstance(foolForge)!;
			Expect(!forge.IsAvailableForPlayer(ironclad), $"{foolForge.Name} is not offered to other characters");
		}

		Expect(new NoxiousFumesForge().IsAvailableForPlayer(ironclad), "Noxious Fumes forge is generic");
		Expect(HextechCatalog.IsForgeTypeShownInUi(typeof(NoxiousFumesForge)) && HextechCatalog.IsForgeTypeShownInUi(typeof(StarsForge)), "other forges stay listed");
		Equal(2m, new NoxiousFumesForge().DynamicVars["NoxiousFumesPower"].BaseValue, "Noxious Fumes stacks per forge");
		Equal(2m, new JuggernautForge().DynamicVars["JuggernautPower"].BaseValue, "Juggernaut stacks per forge");
		Equal(1m, new DarkEmbraceForge().DynamicVars["DarkEmbracePower"].BaseValue, "Dark Embrace stacks per forge");
		Equal(1m, new EchoForge().DynamicVars["EchoFormPower"].BaseValue, "Echo Form stacks per forge");
		foreach (HextechForgeBase generic in new HextechForgeBase[] { new JuggernautForge(), new DarkEmbraceForge(), new EchoForge() })
		{
			Expect(generic.IsAvailableForPlayer(ironclad), $"{generic.GetType().Name} is generic");
		}
	}

	/// <summary>
	/// 百分比最大生命的白银、黄金两档出厂默认禁用；低于配置版本 50 的配置迁移一次并保留玩家自定义项，
	/// 已在 50 及以上的配置尊重玩家手动开启。
	/// </summary>
	[HextechTest]
	private static void PercentHpForgesAreDisabledByDefaultAndMigratedOnce()
	{
		string silver = ModelDb.GetId<SilverHpForge>().Entry;
		string gold = ModelDb.GetId<GoldHpForge>().Entry;
		SetEqual(new[] { silver, gold }, HextechRuneConfiguration.GetDefaultDisabledForgeIds(), "factory default disabled forges");

		const string custom = "custom-unknown-forge";
		(int version, IReadOnlySet<string> migrated) = HextechRuneConfiguration.MigrateDisabledForgeIdsForTests(49, [custom]);
		Equal(CurrentConfigVersion, version, "v49 config lands on the current version");
		SetEqual(new[] { custom, silver, gold }, migrated, "v49 config gains both percent max HP forges and keeps the player's own entries");

		(_, IReadOnlySet<string> again) = HextechRuneConfiguration.MigrateDisabledForgeIdsForTests(version, migrated.Where(id => id != silver));
		SetEqual(new[] { custom, gold }, again, "a config already at the current version keeps the player's manual re-enable");
	}
}
