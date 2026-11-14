namespace HextechRunes;

/// <summary>
/// 前奏（愚者专属）：每场战斗的第一回合开始时，不再抽牌，改为从你的抽牌堆中选择等量的牌加入手牌。
/// 本文件只放与版本无关的声明（数值、存档属性、可用性），三个变体都编译；
/// 钩子与战斗逻辑在 <c>Fool/PreludeRune.V113.cs</c>，只在 0.113.0 编译。
/// </summary>
public sealed partial class PreludeRune : HextechRelicBase
{
	// 旧变体没有愚者，恒为 false；触发时仍要再判角色（设计裁决：角色限定符文在触发时仍判断角色）。
	public override bool IsAvailableForPlayer(Player player)
	{
		return IsFoolPlayer(player);
	}

	/// <summary>
	/// 本次要加入手牌的张数与是否需要弹选择：张数取本应抽的张数与手牌剩余空间的较小者；
	/// 抽牌堆不多于这个张数时全部加入、不弹选择。
	/// </summary>
	internal static (int Count, bool NeedsSelection) GetCardsToAdd(int requestedDraws, int handCount, int maxHandSize, int drawPileCount)
	{
		int count = Math.Min(Math.Max(0, requestedDraws), Math.Max(0, maxHandSize - handCount));
		if (count <= 0 || drawPileCount <= 0)
		{
			return (0, false);
		}

		return drawPileCount <= count ? (drawPileCount, false) : (count, true);
	}
}
