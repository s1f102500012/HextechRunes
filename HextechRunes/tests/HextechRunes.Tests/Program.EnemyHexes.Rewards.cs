using System.Runtime.CompilerServices;
using HextechRunes;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

// 改战后奖励的敌方海克斯：破碎金冠（卡牌奖励少 1 张）、红包（不再自然掉落金币）。
internal static partial class Program
{
	/// <summary>
	/// 敌方破碎金冠：遭遇战的卡牌奖励从末尾去掉 1 张，至少保留 1 张；其他来源的卡牌奖励、别的对局的玩家不受影响。
	/// 与我方同名符文共用展示遗物，品级同为白银。
	/// </summary>
	[HextechTest]
	private static void EnemyBrokenGoldenCrownRemovesOneEncounterCardRewardOption()
	{
		Equal(HextechRarityTier.Silver, MonsterHexCatalog.GetMonsterHexRarity(MonsterHexKind.BrokenGoldenCrown), "enemy Broken Golden Crown rarity");
		Equal(typeof(BrokenGoldenCrownRune), HextechContentRegistry.MonsterHexMetadata.IconRelicTypes[MonsterHexKind.BrokenGoldenCrown], "shares the player rune as display relic");

		(HextechEnemyHexContext context, Player player, _) = CreatePrismaticEnemyFixture();
		BrokenGoldenCrownEnemyHex effect = new();
		CardCreationOptions encounter = CreateRewardTestOptions(CardCreationSource.Encounter);
		List<CardCreationResult> options = CreateRewardTestOptionList(3);
		CardCreationResult[] original = options.ToArray();

		Expect(effect.TryModifyCardRewardOptionsLate(context, player, options, encounter), "three options are trimmed");
		SequenceEqual(original.Take(2), options, "the last option is removed");

		options = CreateRewardTestOptionList(1);
		Expect(!effect.TryModifyCardRewardOptionsLate(context, player, options, encounter), "a single option is left alone");
		Equal(1, options.Count, "at least one option remains");

		CardCreationSource otherSource = Enum.GetValues<CardCreationSource>().First(static source => source != CardCreationSource.Encounter);
		options = CreateRewardTestOptionList(3);
		Expect(!effect.TryModifyCardRewardOptionsLate(context, player, options, CreateRewardTestOptions(otherSource)), "non-encounter card rewards are untouched");
		Equal(3, options.Count, "non-encounter options are all kept");

		Player outsider = CreateOrdinalTestPlayer(9);
		options = CreateRewardTestOptionList(3);
		Expect(!effect.TryModifyCardRewardOptionsLate(context, outsider, options, encounter), "players outside this run are untouched");
	}

	/// <summary>
	/// 敌方红包：只去掉房间自己生成的金币奖励；经 AddExtraReward 追加的金币（遗物、海克斯、锻造器、夺回的金币）按引用识别并保留，
	/// 其他类型的奖励不动。与我方同名符文共用展示遗物，品级同为黄金。
	/// </summary>
	[HextechTest]
	private static void EnemyRedEnvelopeRemovesOnlyNaturalGoldReward()
	{
		Equal(HextechRarityTier.Gold, MonsterHexCatalog.GetMonsterHexRarity(MonsterHexKind.RedEnvelope), "enemy Red Envelope rarity");
		Equal(typeof(RedEnvelopeRune), HextechContentRegistry.MonsterHexMetadata.IconRelicTypes[MonsterHexKind.RedEnvelope], "shares the player rune as display relic");

		Player player = CreateOrdinalTestPlayer(1);
		Reward natural = new GoldReward(12, player, false);
		Reward extraFromRelic = new GoldReward(100, player, false);
		Reward stolenBack = new GoldReward(30, player, true);
		Reward other = (Reward)RuntimeHelpers.GetUninitializedObject(typeof(RelicReward));
		List<Reward> rewards = [natural, other, extraFromRelic, stolenBack];

		Equal(1, RedEnvelopeEnemyHex.RemoveNaturalGoldRewards(rewards, [extraFromRelic, stolenBack]), "only the room's own gold is removed");
		SequenceEqual(new[] { other, extraFromRelic, stolenBack }, rewards, "extra gold and other rewards stay in order");
		Equal(0, RedEnvelopeEnemyHex.RemoveNaturalGoldRewards(rewards, [extraFromRelic, stolenBack]), "nothing left to remove");

		rewards = [natural, other];
		Equal(1, RedEnvelopeEnemyHex.RemoveNaturalGoldRewards(rewards, null), "works when the player has no extra rewards");
		SequenceEqual(new[] { other }, rewards, "non-gold rewards stay");
	}

	private static CardCreationOptions CreateRewardTestOptions(CardCreationSource source)
	{
		CardCreationOptions options = (CardCreationOptions)RuntimeHelpers.GetUninitializedObject(typeof(CardCreationOptions));
		SetAutoProperty(options, nameof(CardCreationOptions.Source), source);
		return options;
	}

	private static List<CardCreationResult> CreateRewardTestOptionList(int count)
	{
		return Enumerable.Range(0, count)
			.Select(static _ => (CardCreationResult)RuntimeHelpers.GetUninitializedObject(typeof(CardCreationResult)))
			.ToList();
	}
}
