using Godot;

namespace HextechRunes;

/// <summary>
/// 候选卡的原版特效，接入时机对应原版 KiwiAugmentSelection/UIBase 里 Augment_Button 下的粒子节点：
/// 打开界面时同时弹出（FlashIn）并开始常驻光（Idle）；悬停或手柄焦点时播放悬停光（Hover），离开淡出；
/// 重随时卡面重建（Refresh）加上层闪光（RefreshOverlay），金色重随额外在按钮上播放点击爆发；
/// 确认选择时选中的卡播放选中特效（Selected），其余卡播放未选中特效（Static）；界面关闭时选中卡等选中特效播完再渐隐。
/// 全部是本地表现，失败只记警告；选择结果与联机同步不等任何特效。
/// </summary>
internal sealed partial class HextechRuneSelectionScreen
{
	private const float SelectedCardFadeSeconds = 0.45f;

	// 原版各粒子节点的 Layer（同一卡上按它排先后）。卡面图标是 15、名称与描述是 20：低于 15 的画在卡面内容下方。
	private const int CardContentLayer = 15;
	private const int IdleVfxLayer = 5;
	private const int HoverVfxLayer = 6;
	private const int NotPickedVfxLayer = 6;
	private const int RefreshVfxLayer = 7;
	private const int FlashInVfxLayer = 21;
	private const int RefreshOverlayVfxLayer = 21;
	private const int PickedVfxLayer = 25;
	private const int GoldenRerollClickVfxLayer = 9;

	// 常驻光整局都亮着，比一次性特效再压一档，避免三张卡同时闪。
	private const float IdleVfxIntensityScale = 0.75f;
	private const float HoverVfxFadeOutSeconds = 0.2f;

	// 未选中特效（Augment_StaticVFX）的灰卡粒子 0.4 秒从不透明淡到透明，未选中的卡跟它一起淡出。
	private const float NotPickedCardFadeSeconds = 0.4f;

	// 选中卡等选中特效播完再渐隐；数据估算的时长（银/金 0.9 秒、棱彩约 1.6 秒）超过这个值也只等这么久。
	private const float MaxSelectedVfxHoldSeconds = 1.6f;

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

	// 正在显示悬停光的卡槽。
	private readonly Dictionary<int, HextechKiwiVfxPlayer> _cardHoverVfx = new();

	// 选中特效：界面关闭时据此决定选中卡在渐隐前还要停留多久。
	private HextechKiwiVfxPlayer? _selectedCardVfx;
	private string? _selectedCardVfxSystem;

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
					AttachCardVfx(slot, i, TierVfx(_relics[i], "FlashInVFX"), FlashInVfxLayer);
					StartCardIdleVfx(slot, i, _relics[i]);
				}
			}
		});
	}

	private void PlayRerollVfx(int slotIndex, bool goldenReroll)
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlayRerollVfx failed", () =>
		{
			// 被重随的卡已经连同悬停光一起重建，旧条目作废；新卡在鼠标再次移动时重新触发悬停。
			_cardHoverVfx.Remove(slotIndex);
			if (_cardsRow == null
				|| slotIndex < 0
				|| slotIndex >= _relics.Count
				|| slotIndex >= _cardsRow.GetChildCount()
				|| _cardsRow.GetChild(slotIndex) is not Control slot)
			{
				return;
			}

			RelicModel relic = _relics[slotIndex];
			AttachCardVfx(slot, slotIndex, TierVfx(relic, "RefreshVFX"), RefreshVfxLayer);
			AttachCardVfx(slot, slotIndex, TierVfx(relic, "RefreshOverlayVFX"), RefreshOverlayVfxLayer);
			StartCardIdleVfx(slot, slotIndex, relic);
			if (goldenReroll && slotIndex < _rerollButtons.Count)
			{
				HextechKiwiVfxPlayer? click = HextechKiwiVfxPlayer.Create(
					HextechKiwiVfxLibrary.GoldenRerollClick,
					KiwiVfxCardPixelsPerUnit,
					KiwiVfxButtonPixelsPerUnit);
				if (click == null)
				{
					return;
				}

				click.UiLayer = GoldenRerollClickVfxLayer;
				if (AddToCardVfxHost(slot, slotIndex, click, CardVfxHost.RerollButton))
				{
					_cardVfx.Add(new CardVfxAttachment(click, slotIndex, CardVfxHost.RerollButton));
				}
				else
				{
					click.QueueFree();
				}
			}
		});
	}

	/// <summary>常驻光：原版 IdleVFX 去掉重画卡底与卡框的发射器（本模组卡牌自己画边框），只留卡框柔光与边缘火花。银色因此没有常驻光。</summary>
	private void StartCardIdleVfx(Control slot, int slotIndex, RelicModel relic)
	{
		AttachCardVfx(
			slot,
			slotIndex,
			TierVfx(relic, "IdleVFX"),
			IdleVfxLayer,
			IdleVfxIntensityScale,
			static emitter => !HextechKiwiVfxLibrary.DrawsCardBase(emitter));
	}

	/// <summary>卡牌的悬停光跟随鼠标悬停与手柄焦点（与重随按钮的高亮同一口径：任一成立即显示）。</summary>
	private void HookCardHoverVfx(Button card, int slotIndex)
	{
		bool hovered = false;
		bool focused = false;
		void Update()
		{
			if (hovered || focused)
			{
				StartCardHoverVfx(slotIndex);
			}
			else
			{
				StopCardHoverVfx(slotIndex);
			}
		}
		card.MouseEntered += () =>
		{
			hovered = true;
			Update();
		};
		card.MouseExited += () =>
		{
			hovered = false;
			Update();
		};
		card.FocusEntered += () =>
		{
			focused = true;
			Update();
		};
		card.FocusExited += () =>
		{
			focused = false;
			Update();
		};
	}

	private void StartCardHoverVfx(int slotIndex)
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.StartCardHoverVfx failed", () =>
		{
			if (_choiceLocked
				|| _cardsRow == null
				|| slotIndex < 0
				|| slotIndex >= _relics.Count
				|| slotIndex >= _cardsRow.GetChildCount()
				|| _cardsRow.GetChild(slotIndex) is not Control slot)
			{
				return;
			}

			if (_cardHoverVfx.TryGetValue(slotIndex, out HextechKiwiVfxPlayer? current)
				&& GodotObject.IsInstanceValid(current)
				&& !current.IsQueuedForDeletion()
				&& !current.IsFadingOut)
			{
				return;
			}

			HextechKiwiVfxPlayer? player = AttachCardVfx(slot, slotIndex, TierVfx(_relics[slotIndex], "HoverVFX"), HoverVfxLayer);
			if (player != null)
			{
				_cardHoverVfx[slotIndex] = player;
			}
		});
	}

	private void StopCardHoverVfx(int slotIndex)
	{
		if (_cardHoverVfx.Remove(slotIndex, out HextechKiwiVfxPlayer? player) && GodotObject.IsInstanceValid(player))
		{
			player.FadeOutAndFree(HoverVfxFadeOutSeconds);
		}
	}

	/// <summary>确认选择时：选中的卡播放选中特效（盖在卡面内容上），其余卡播放未选中特效（卡面内容下方的灰卡）。</summary>
	private void PlaySelectionVfx()
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlaySelectionVfx failed", () =>
		{
			foreach (int slotIndex in _cardHoverVfx.Keys.ToArray())
			{
				StopCardHoverVfx(slotIndex);
			}

			if (_selectedSlotIndex < 0 || _cardsRow == null)
			{
				return;
			}

			int count = Math.Min(_relics.Count, _cardsRow.GetChildCount());
			for (int i = 0; i < count; i++)
			{
				if (_cardsRow.GetChild(i) is not Control slot)
				{
					continue;
				}

				if (i == _selectedSlotIndex)
				{
					_selectedCardVfxSystem = TierVfx(_relics[i], "SelectedVFX");
					_selectedCardVfx = AttachCardVfx(slot, i, _selectedCardVfxSystem, PickedVfxLayer);
				}
				else
				{
					AttachCardVfx(slot, i, HextechKiwiVfxLibrary.NotPicked, NotPickedVfxLayer);
				}
			}
		});
	}

	private string TierVfx(RelicModel relic, string suffix)
	{
		return HextechKiwiVfxLibrary.TierSystemName(DetermineCardRarityKey(relic, _metadataMode), suffix);
	}

	/// <summary>在卡上挂一个特效：Layer 低于卡面内容的插在稀有度边框之后、图标和文字之前，其余盖在卡牌按钮最上层；同一区域内按 Layer 排序。</summary>
	private HextechKiwiVfxPlayer? AttachCardVfx(
		Control slot,
		int slotIndex,
		string systemName,
		int layer,
		float intensityScale = 1f,
		Func<HextechKiwiVfxEmitter, bool>? include = null)
	{
		HextechKiwiVfxPlayer? player = HextechKiwiVfxPlayer.Create(
			systemName,
			KiwiVfxCardPixelsPerUnit,
			KiwiVfxButtonPixelsPerUnit,
			intensityScale: intensityScale,
			include: include);
		if (player == null)
		{
			return null;
		}

		player.UiLayer = layer;
		CardVfxHost host = layer < CardContentLayer ? CardVfxHost.CardBelowContent : CardVfxHost.CardOverlay;
		if (!AddToCardVfxHost(slot, slotIndex, player, host))
		{
			player.QueueFree();
			return null;
		}

		_cardVfx.Add(new CardVfxAttachment(player, slotIndex, host));
		return player;
	}

	private bool AddToCardVfxHost(Control slot, int slotIndex, HextechKiwiVfxPlayer player, CardVfxHost host)
	{
		switch (host)
		{
			case CardVfxHost.CardBelowContent when slot.GetChildCount() > 0 && slot.GetChild(0) is Button card:
			{
				Node? frame = card.GetNodeOrNull("RarityFrame");
				int index = frame != null ? frame.GetIndex() + 1 : 0;
				// 跳过已经在内容下方、Layer 不高于自己的特效，插到第一个 Layer 更高的特效或卡面内容之前。
				while (index < card.GetChildCount()
					&& card.GetChild(index) is HextechKiwiVfxPlayer below
					&& below.UiLayer <= player.UiLayer)
				{
					index++;
				}
				card.AddChild(player);
				card.MoveChild(player, index);
				return true;
			}
			case CardVfxHost.CardOverlay when slot.GetChildCount() > 0 && slot.GetChild(0) is Button card:
			{
				card.AddChild(player);
				// 最上层的特效都在末尾；往前越过 Layer 更高的特效。
				int index = card.GetChildCount() - 1;
				while (index > 0
					&& card.GetChild(index - 1) is HextechKiwiVfxPlayer above
					&& above.UiLayer > player.UiLayer)
				{
					index--;
				}
				card.MoveChild(player, index);
				return true;
			}
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

	/// <summary>选中特效还要播多久（按数据估算的时长减去已播时间，封顶 <see cref="MaxSelectedVfxHoldSeconds"/>）。</summary>
	private float RemainingSelectedVfxSeconds()
	{
		if (_selectedCardVfx == null
			|| _selectedCardVfxSystem == null
			|| !GodotObject.IsInstanceValid(_selectedCardVfx)
			|| _selectedCardVfx.IsQueuedForDeletion())
		{
			return 0f;
		}

		float remaining = HextechKiwiVfxPlayer.EstimateDurationSeconds(_selectedCardVfxSystem) - _selectedCardVfx.ElapsedSeconds;
		return Math.Clamp(remaining, 0f, MaxSelectedVfxHoldSeconds);
	}

	/// <summary>
	/// 界面关闭时让被选中的卡原地渐隐：界面已经移出叠加层栈，这里只是让节点多留一会儿，
	/// 未选中的卡随未选中特效一起淡出，其余元素直接透明（保持排版不动）、全部不接收鼠标；
	/// 选中的卡保持原大小、位置和选中态，等选中特效播完再淡出，之后释放整个界面。
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
			Tween? notPickedFade = null;
			for (Node node = card; node != this && node.GetParent() is Node parent; node = parent)
			{
				foreach (Node sibling in parent.GetChildren())
				{
					if (sibling == node || sibling is not CanvasItem item)
					{
						continue;
					}

					if (parent == _cardsRow)
					{
						notPickedFade ??= CreateTween().SetParallel();
						notPickedFade.TweenProperty(item, "modulate:a", 0f, NotPickedCardFadeSeconds);
					}
					else
					{
						item.Modulate = Colors.Transparent;
					}
				}
			}

			Visible = true;
			Tween tween = CreateTween();
			float hold = RemainingSelectedVfxSeconds();
			if (hold > 0f)
			{
				tween.TweenInterval(hold);
			}
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
