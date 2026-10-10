using Godot;

namespace HextechRunes;

/// <summary>
/// 候选卡的原版特效：打开界面时依次弹出（FlashIn），重随时卡面重建（Refresh，画在卡面内容下方）
/// 加上层闪光（RefreshOverlay），金色重随额外在按钮上播放点击爆发。全部是本地表现，失败只记警告。
/// </summary>
internal sealed partial class HextechRuneSelectionScreen
{
	private const float CardFlashInStaggerSeconds = 0.12f;

	// 叠加层可能重复回到栈顶（如看完地图预览），弹出特效每个界面实例只播一次。
	private bool _cardFlashInPlayed;

	private void PlayCardFlashInVfx()
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlayCardFlashInVfx failed", () =>
		{
			if (_cardFlashInPlayed || _cardsRow == null)
			{
				return;
			}

			_cardFlashInPlayed = true;

			int count = Math.Min(_relics.Count, _cardsRow.GetChildCount());
			for (int i = 0; i < count; i++)
			{
				if (_cardsRow.GetChild(i) is Control slot)
				{
					AttachCardVfx(slot, _relics[i], "FlashInVFX", i * CardFlashInStaggerSeconds, belowContent: false);
				}
			}
		});
	}

	private void PlayRerollVfx(int slotIndex, bool goldenReroll)
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlayRerollVfx failed", () =>
		{
			if (_cardsRow == null
				|| slotIndex < 0
				|| slotIndex >= _relics.Count
				|| slotIndex >= _cardsRow.GetChildCount()
				|| _cardsRow.GetChild(slotIndex) is not Control slot)
			{
				return;
			}

			RelicModel relic = _relics[slotIndex];
			AttachCardVfx(slot, relic, "RefreshVFX", 0f, belowContent: true);
			AttachCardVfx(slot, relic, "RefreshOverlayVFX", 0f, belowContent: false);
			if (goldenReroll && slotIndex < _rerollButtons.Count)
			{
				HextechKiwiVfxPlayer? click = HextechKiwiVfxPlayer.Create(
					HextechKiwiVfxLibrary.GoldenRerollClick,
					KiwiVfxCardPixelsPerUnit,
					KiwiVfxButtonPixelsPerUnit);
				if (click != null)
				{
					_rerollButtons[slotIndex].AddChild(click);
				}
			}
		});
	}

	/// <summary>
	/// <paramref name="belowContent"/> 为 true 时插到卡牌按钮的稀有度边框之后、图标和文字之前，
	/// 与原版一样让卡面重建的光在内容下方；否则盖在整个卡槽上。
	/// </summary>
	private void AttachCardVfx(Control slot, RelicModel relic, string suffix, float delay, bool belowContent)
	{
		string systemName = HextechKiwiVfxLibrary.TierSystemName(DetermineCardRarityKey(relic, _metadataMode), suffix);
		HextechKiwiVfxPlayer? player = HextechKiwiVfxPlayer.Create(
			systemName,
			KiwiVfxCardPixelsPerUnit,
			KiwiVfxButtonPixelsPerUnit,
			delay: delay);
		if (player == null)
		{
			return;
		}

		if (belowContent && slot.GetChildCount() > 0 && slot.GetChild(0) is Button card)
		{
			Node? frame = card.GetNodeOrNull("RarityFrame");
			card.AddChild(player);
			card.MoveChild(player, frame != null ? frame.GetIndex() + 1 : 0);
			return;
		}

		slot.AddChild(player);
	}
}
