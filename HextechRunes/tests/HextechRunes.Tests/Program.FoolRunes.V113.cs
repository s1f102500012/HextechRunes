#if STS2_113_OR_NEWER
using HextechRunes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace HextechRunes.Tests;

// 愚者海克斯里需要 0.113.0 类型的共享测试与样板「箭袋满满」的测试。
// 后续各组的测试各放一个新文件 Program.FoolRunes.<组名>.V113.cs（整文件 #if STS2_113_OR_NEWER），不改本文件。
internal static partial class Program
{
	/// <summary>共享代码按名字找到的愚者角色就是原版 Fool；愚者玩家映射到愚者池并能拿到全部 18 个。</summary>
	[HextechTest]
	private static void FoolCompatResolvesVanillaFoolOn113()
	{
		Equal<ModelId?>(ModelDb.GetId<Fool>(), HextechFoolCompat.FoolCharacterId, "Fool character id");
		Equal<ModelId?>(ModelDb.GetId<Fool>(), HextechPlayerContextHelper.GetCharacterId(PlayerRuneCharacterPool.Fool), "Fool pool maps to the vanilla Fool");

		Player fool = CreateCharacterTestPlayer(typeof(Fool));
		SetAutoProperty(fool, nameof(Player.Deck), new CardPile(PileType.Deck));
		Expect(HextechFoolCompat.IsFoolPlayer(fool), "Fool player is detected");
		Expect(HextechPlayerContextHelper.TryGetRuneCharacterPool(fool, out PlayerRuneCharacterPool pool) && pool == PlayerRuneCharacterPool.Fool, "Fool maps to the Fool pool");
		Equal<ModelId?>(ModelDb.GetId<Fool>(), HextechRunePoolBuilder.GetRuneCharacterId(fool), "Fool takes part in character weighting");
		foreach ((Type type, _, _) in FoolRuneRegistrations)
		{
			HextechRelicBase rune = (HextechRelicBase)Activator.CreateInstance(type)!;
			Expect(rune.IsAvailableForPlayer(fool), $"{type.Name} is available to the Fool");
		}
	}

	/// <summary>
	/// 箭袋满满：战斗开始时在原版已初始化的箭矢槽之上再加 {Arrows} 个，新槽可立即发射；
	/// 非愚者持有者（控制台或其他模组直接发放）不触发。
	/// </summary>
	[HextechTest]
	private static void FullQuiverAddsArrowSlotsAtCombatStart()
	{
		Player fool = CreateFoolCombatTestPlayer();
		FullQuiverRune rune = CreateMutableTestModel<FullQuiverRune>();
		rune.Owner = fool;
		Equal(1, fool.PlayerCombatState!.Arrows.Count, "Fool starts combat with its base arrow slot");

		rune.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(1 + rune.DynamicVars["Arrows"].IntValue, fool.PlayerCombatState.Arrows.Count, "Full Quiver adds its arrows on top of the base slot");
		Expect(fool.PlayerCombatState.Arrows.All(static arrow => arrow.Status == ArrowStatus.Retrieved), "added arrows are ready to shoot");
		Equal(2, ArrowCmd.GetAvailableArrowCount(fool), "both arrows are available");

		Player ironclad = CreateCharacterTestPlayer(typeof(Ironclad), netId: 2);
		AttachArrowOnlyCombatState(ironclad, 0);
		FullQuiverRune foreign = CreateMutableTestModel<FullQuiverRune>();
		foreign.Owner = ironclad;
		foreign.BeforeCombatStart().GetAwaiter().GetResult();
		Equal(0, ironclad.PlayerCombatState!.Arrows.Count, "non-Fool holders gain no arrows");
	}
}
#endif
