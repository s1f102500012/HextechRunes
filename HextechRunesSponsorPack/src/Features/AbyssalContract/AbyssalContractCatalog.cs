using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;

namespace HextechRunesSponsorPack;

// 深渊契约的两张静态表。选择界面、未签约提示、契约识别、事件遗物注册与依赖声明都从这里取,不各写一份。
internal static class AbyssalContractCatalog
{
	// 顺序即选择界面顺序、未签约时的提示顺序,也是 SponsorCatalog 事件遗物表里的注册顺序(append-only,不要重排)。
	internal static IReadOnlyList<ContractChoice> Choices { get; } =
	[
		new(AbyssalContractKind.Warrior, typeof(WarriorContractChoiceRelic)),
		new(AbyssalContractKind.Hunter, typeof(HunterContractChoiceRelic)),
		new(AbyssalContractKind.Regent, typeof(RegentContractChoiceRelic)),
		new(AbyssalContractKind.Necrobinder, typeof(NecrobinderContractChoiceRelic)),
		new(AbyssalContractKind.Automaton, typeof(AutomatonContractChoiceRelic))
	];

	// 原版五名角色的起始遗物与其升级版:战士/自动机契约把起始遗物换成升级版,
	// 摄政契约把起始遗物(或已升级的版本)换成击剑手册。
	internal static IReadOnlyList<StarterRelicUpgrade> StarterRelics { get; } =
	[
		new(typeof(Ironclad), typeof(BurningBlood), typeof(BlackBlood)),
		new(typeof(Silent), typeof(RingOfTheSnake), typeof(RingOfTheDrake)),
		new(typeof(Regent), typeof(DivineRight), typeof(DivineDestiny)),
		new(typeof(Necrobinder), typeof(BoundPhylactery), typeof(PhylacteryUnbound)),
		new(typeof(Defect), typeof(CrackedCore), typeof(InfusedCore))
	];

	internal static IEnumerable<Type> ChoiceRelicTypes => Choices.Select(static choice => choice.ChoiceRelicType);

	internal static AbyssalContractKind GetKindForChoice(RelicModel? selected)
	{
		if (selected == null)
		{
			return AbyssalContractKind.None;
		}

		foreach (ContractChoice choice in Choices)
		{
			if (choice.ChoiceRelicType.IsInstanceOfType(selected))
			{
				return choice.Kind;
			}
		}

		return AbyssalContractKind.None;
	}

	internal static bool TryGetStarterRelics(CharacterModel character, out StarterRelicUpgrade starter)
	{
		foreach (StarterRelicUpgrade candidate in StarterRelics)
		{
			if (candidate.Character.IsInstanceOfType(character))
			{
				starter = candidate;
				return true;
			}
		}

		starter = default;
		return false;
	}

	internal static RelicModel GetCanonicalRelic(Type relicType)
	{
		return ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType));
	}

	// 与 Player.GetRelic<T>() 同语义:第一个类型匹配的遗物。
	internal static RelicModel? FindOwnedRelic(Player owner, Type relicType)
	{
		return owner.Relics.FirstOrDefault(relicType.IsInstanceOfType);
	}

	internal readonly record struct ContractChoice(AbyssalContractKind Kind, Type ChoiceRelicType);

	internal readonly record struct StarterRelicUpgrade(Type Character, Type Starter, Type Upgraded);
}
