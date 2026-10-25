using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes.Tests;

internal static partial class Program
{
	// 溢流的 +1 视同加在基础费用上（常规阶段），规则型免费都在 Late 阶段定为 0，所以"免费"一定晚于溢流生效。
	[HextechTest]
	private static void RuleBasedFreeRunesZeroCostsInLatePhase()
	{
		const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
		Type[] freeRunes =
		[
			typeof(CerberusRune),
			typeof(TrickLicenseRune),
			typeof(TriPrismRune),
			typeof(LubricantRune),
			typeof(SwordIntentRune),
			typeof(BlankCheckRune),
			typeof(BigKnifeRune)
		];
		foreach (Type rune in freeRunes)
		{
			Expect(rune.GetMethod(nameof(RelicModel.TryModifyEnergyCostInCombatLate), declared) != null
				&& rune.GetMethod(nameof(RelicModel.TryModifyEnergyCostInCombat), declared) == null,
				$"{rune.Name} makes cards free in the Late phase so Overflow cannot add a cost back");
		}

		Expect(typeof(OverflowRune).GetMethod(nameof(RelicModel.TryModifyEnergyCostInCombatLate), declared) == null,
			"Overflow's increase stays in the regular phase");
	}

	// 薄暮法衣只镜像已核对、可由玩家持有的增益，外部模组或怪物机制类 Power 不镜像。
	[HextechTest]
	private static void TwilightVeilMirrorsOnlyWhitelistedBuffs()
	{
		// 只按类型判定，不需要注册到 ModelDb 的实例。
		static PowerModel Power<T>() where T : PowerModel => (PowerModel)RuntimeHelpers.GetUninitializedObject(typeof(T));
		Expect(TwilightVeilRune.IsMirrorablePower(Power<StrengthPower>()), "Strength is mirrored");
		Expect(TwilightVeilRune.IsMirrorablePower(Power<ArtifactPower>()), "Artifact is mirrored");
		Expect(TwilightVeilRune.IsMirrorablePower(Power<PlatingPower>()), "Plating is mirrored");
		Expect(!TwilightVeilRune.IsMirrorablePower(Power<HardenedShellPower>()), "monster mechanism buffs are not mirrored");
		Expect(!TwilightVeilRune.IsMirrorablePower(Power<BarricadePower>()), "Barricade has monster-side behaviour and is not mirrored");
		Expect(!TwilightVeilRune.IsMirrorablePower(Power<ShrinkPower>()), "Shrink is not mirrored");
	}
}
