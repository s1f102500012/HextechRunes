using Godot;

namespace HextechRunes;

/// <summary>
/// 候选卡的原版特效：打开界面时同时弹出（FlashIn），重随时卡面重建（Refresh，画在卡面内容下方）
/// 加上层闪光（RefreshOverlay），金色重随额外在按钮上播放点击爆发；选完后被选中的卡渐隐退场。
/// 全部是本地表现，失败只记警告。
/// </summary>
internal sealed partial class HextechRuneSelectionScreen
{
	private const float SelectedCardFadeSeconds = 0.45f;

	// 卡槽里卡牌下面还有重随按钮，卡槽中心比卡牌中心低，卡上的特效都挂在卡牌按钮上，以卡牌中心为原点。
	private enum CardVfxHost
	{
		CardOverlay,
		CardBelowContent,
		RerollButton,
	}

	private readonly record struct CardVfxAttachment(HextechKiwiVfxPlayer Player, int SlotIndex, CardVfxHost Host);

	// 叠加层可能重复回到栈顶（如看完地图预览），弹出特效每个界面实例只播一次。
	private bool _cardFlashInPlayed;

	// 正在播放的卡牌特效。重随会重建所有卡槽，没被重随的卡上的特效要摘下再挂回新卡槽，不能跟着旧卡槽一起被释放。
	private readonly List<CardVfxAttachment> _cardVfx = [];

	// 选中的卡在界面关闭时渐隐；-1 表示没有可渐隐的卡（自选、仅敌方等）。
	private int _selectedSlotIndex = -1;

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
					AttachCardVfx(slot, i, _relics[i], "FlashInVFX", belowContent: false);
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
			AttachCardVfx(slot, slotIndex, relic, "RefreshVFX", belowContent: true);
			AttachCardVfx(slot, slotIndex, relic, "RefreshOverlayVFX", belowContent: false);
			if (goldenReroll && slotIndex < _rerollButtons.Count)
			{
				HextechKiwiVfxPlayer? click = HextechKiwiVfxPlayer.Create(
					HextechKiwiVfxLibrary.GoldenRerollClick,
					KiwiVfxCardPixelsPerUnit,
					KiwiVfxButtonPixelsPerUnit);
				if (click != null && AddToCardVfxHost(slot, slotIndex, click, CardVfxHost.RerollButton))
				{
					_cardVfx.Add(new CardVfxAttachment(click, slotIndex, CardVfxHost.RerollButton));
				}
				else
				{
					click?.QueueFree();
				}
			}
		});
	}

	/// <summary>
	/// <paramref name="belowContent"/> 为 true 时插到卡牌按钮的稀有度边框之后、图标和文字之前，
	/// 与原版一样让卡面重建的光在内容下方；否则加在卡牌按钮最上层。
	/// </summary>
	private void AttachCardVfx(Control slot, int slotIndex, RelicModel relic, string suffix, bool belowContent)
	{
		string systemName = HextechKiwiVfxLibrary.TierSystemName(DetermineCardRarityKey(relic, _metadataMode), suffix);
		HextechKiwiVfxPlayer? player = HextechKiwiVfxPlayer.Create(
			systemName,
			KiwiVfxCardPixelsPerUnit,
			KiwiVfxButtonPixelsPerUnit);
		if (player == null)
		{
			return;
		}

		CardVfxHost host = belowContent ? CardVfxHost.CardBelowContent : CardVfxHost.CardOverlay;
		if (!AddToCardVfxHost(slot, slotIndex, player, host))
		{
			player.QueueFree();
			return;
		}

		_cardVfx.Add(new CardVfxAttachment(player, slotIndex, host));
	}

	private bool AddToCardVfxHost(Control slot, int slotIndex, HextechKiwiVfxPlayer player, CardVfxHost host)
	{
		switch (host)
		{
			case CardVfxHost.CardBelowContent when slot.GetChildCount() > 0 && slot.GetChild(0) is Button card:
			{
				Node? frame = card.GetNodeOrNull("RarityFrame");
				card.AddChild(player);
				card.MoveChild(player, frame != null ? frame.GetIndex() + 1 : 0);
				return true;
			}
			case CardVfxHost.CardOverlay when slot.GetChildCount() > 0 && slot.GetChild(0) is Button card:
				card.AddChild(player);
				return true;
			case CardVfxHost.RerollButton when slotIndex < _rerollButtons.Count:
				_rerollButtons[slotIndex].AddChild(player);
				if (slot.GetChildCount() > 0 && slot.GetChild(0) is Control rerollCard)
				{
					player.AnchorCardSpaceTo(rerollCard);
				}
				return true;
			default:
				return false;
		}
	}

	/// <summary>重建卡槽前调用：把除 <paramref name="rebuiltSlot"/> 之外还在播放的特效从旧卡槽摘下。</summary>
	private List<CardVfxAttachment> DetachCardVfx(int rebuiltSlot)
	{
		List<CardVfxAttachment> kept = [];
		foreach (CardVfxAttachment attachment in _cardVfx)
		{
			if (!GodotObject.IsInstanceValid(attachment.Player)
				|| attachment.Player.IsQueuedForDeletion()
				|| attachment.SlotIndex == rebuiltSlot)
			{
				continue;
			}

			attachment.Player.GetParent()?.RemoveChild(attachment.Player);
			kept.Add(attachment);
		}

		_cardVfx.Clear();
		return kept;
	}

	/// <summary>重建卡槽后调用：把摘下的特效挂回同位置的新卡槽，接着原来的进度播放。</summary>
	private void ReattachCardVfx(List<CardVfxAttachment> kept)
	{
		foreach (CardVfxAttachment attachment in kept)
		{
			if (_cardsRow != null
				&& attachment.SlotIndex < _cardsRow.GetChildCount()
				&& _cardsRow.GetChild(attachment.SlotIndex) is Control slot
				&& AddToCardVfxHost(slot, attachment.SlotIndex, attachment.Player, attachment.Host))
			{
				_cardVfx.Add(attachment);
			}
			else
			{
				attachment.Player.QueueFree();
			}
		}
	}

	/// <summary>
	/// 界面关闭时让被选中的卡原地渐隐：界面已经移出叠加层栈，这里只是让节点多留一会儿，
	/// 其余元素透明（保持排版不动）、全部不接收鼠标；选中的卡保持原大小、位置和选中态，淡出后再释放整个界面。
	/// 选择结果和联机同步不等这段动画。返回 false 表示没有可渐隐的卡，调用方直接释放。
	/// </summary>
	private bool TryPlaySelectedCardFadeOut()
	{
		bool started = false;
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlaySelectedCardFadeOut failed", () =>
		{
			int index = _selectedSlotIndex;
			_selectedSlotIndex = -1;
			if (index < 0
				|| !IsInsideTree()
				|| _cardsRow == null
				|| index >= _cardsRow.GetChildCount()
				|| _cardsRow.GetChild(index) is not Control slot
				|| slot.GetChildCount() == 0
				|| slot.GetChild(0) is not Control card)
			{
				return;
			}

			IgnoreMouseRecursively(this);
			for (Node node = card; node != this && node.GetParent() is Node parent; node = parent)
			{
				foreach (Node sibling in parent.GetChildren())
				{
					if (sibling != node && sibling is CanvasItem item)
					{
						item.Modulate = Colors.Transparent;
					}
				}
			}

			Visible = true;
			Tween tween = CreateTween();
			tween.TweenProperty(card, "modulate:a", 0f, SelectedCardFadeSeconds)
				.SetTrans(Tween.TransitionType.Quad)
				.SetEase(Tween.EaseType.In);
			tween.TweenCallback(Callable.From(QueueFree));
			started = true;
		});
		return started;
	}

	private static void IgnoreMouseRecursively(Node node)
	{
		if (node is Control control)
		{
			control.MouseFilter = MouseFilterEnum.Ignore;
			control.FocusMode = FocusModeEnum.None;
		}

		foreach (Node child in node.GetChildren())
		{
			IgnoreMouseRecursively(child);
		}
	}
}
