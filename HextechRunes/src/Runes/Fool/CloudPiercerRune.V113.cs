#if STS2_113_OR_NEWER
using MegaCrit.Sts2.Core.Entities.FoolArrows;

namespace HextechRunes;

public sealed partial class CloudPiercerRune
{
	// ImpaledPower 改了 HitCounter 后要通知界面刷新计数；原版的刷新方法是 protected。
	private static readonly MethodInfo? PowerDisplayAmountChangedMethod = AccessTools.Method(typeof(PowerModel), "InvokeDisplayAmountChanged", Type.EmptyTypes);

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.ForArrow(null),
		HoverTipFactory.FromPower<ImpaledPower>()
	];

	// 原版计数只在两处取到初值：新施加的 ImpaledPower 用 CanonicalVars 的 3；AfterDamageReceived 数到 0 时写死重置为 3 再松脱。
	// 前者之后紧接 AfterArrowImpaled，后者在松脱里触发 AfterArrowDislodged，所以在这两个钩子里把持有者的计数压到
	// 原版初值 − HitReduction（至少 1）。只降不升：已经数过的计数、卡牌效果松脱时没被重置的计数保持原样。
	public override Task AfterArrowImpaled(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		CapHitCounter(power);
		return Task.CompletedTask;
	}

	public override Task AfterArrowDislodged(ImpaledPower power, ArrowSlot arrow, PlayerChoiceContext context, CardModel? source)
	{
		// 钩子触发时这支箭已从能力上取下；没有剩余的箭时能力随后会被移除，不必调整。
		if (power.Arrows.Count > 0)
		{
			CapHitCounter(power);
		}

		return Task.CompletedTask;
	}

	private void CapHitCounter(ImpaledPower power)
	{
		if (power.Applier != Owner.Creature || !IsFoolPlayer(Owner))
		{
			return;
		}

		int vanillaHits = ModelDb.Power<ImpaledPower>().HitCounter;
		int hits = Math.Max(1, vanillaHits - DynamicVars["HitReduction"].IntValue);
		if (power.HitCounter <= hits)
		{
			return;
		}

		power.HitCounter = hits;
		HextechPresentation.TryRun("PowerVisual", "Impaled counter refresh failed", () => PowerDisplayAmountChangedMethod?.Invoke(power, null));
		Flash();
	}
}
#endif
