using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace HextechRunes;

/// <summary>
/// 候选卡的原版特效，接入时机对应原版 KiwiAugmentSelection/UIBase 里 Augment_Button 下的粒子节点：
/// 打开界面时同时弹出（FlashIn）并开始常驻光（Idle）；悬停或手柄焦点时播放悬停光（Hover），离开淡出；
/// 重随时卡面重建（Refresh）加上层闪光（RefreshOverlay），金色重随额外在按钮上播放点击爆发；
/// 确认选择时选中的卡播放选中特效（Selected），其余卡播放未选中特效（Static），悬停光随按钮切到选中状态立即撤掉；
/// 界面关闭时三张卡按原版场景退场一起淡出。
/// 全部是本地表现，失败只记警告；选择结果与联机同步不等任何特效。
/// </summary>
internal sealed partial class HextechRuneSelectionScreen
{
	// 原版 AugmentSelection 场景的 sceneTransitionOut：transitionTime 1.0 秒、startAlpha 255 → endAlpha 0，
	// 卡牌连同上面的特效整体淡出，没有位移和缩放。easingType 18 的曲线名称数据里没有，按缓动枚举的常见排列推断为 InOutExpo。
	private const float SelectionSceneFadeOutSeconds = 1.0f;
	private const Tween.TransitionType SelectionSceneFadeOutTransition = Tween.TransitionType.Expo;
	private const Tween.EaseType SelectionSceneFadeOutEase = Tween.EaseType.InOut;

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

	// 悬停光去掉开头的一次性引入，跳过常驻卡框光约 0.14 秒的出场延迟，再整体淡入，避免先闪一下再常亮。
	private const float HoverVfxSkipSeconds = 0.15f;
	private const float HoverVfxFadeInSeconds = 0.15f;

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

	// 渐隐期间界面所在的画布层，要高于地图等关闭后紧接着打开的界面。
	private const int SelectionFadeCanvasLayer = 100;

	private CanvasLayer? _selectionFadeLayer;

	// 退场期间有新界面入栈（连续锻造器选择、选完海克斯紧接着发锻造器、锻造器后的原版选牌界面），
	// 退场层比叠加层栈高，会盖住新界面的开头；此时把剩余淡出压到这么短。
	private const float SelectionFadeInterruptedSeconds = 0.12f;

	private Tween? _selectionFadeTween;
	private NOverlayStack? _selectionFadeStack;
	private Node? _selectionFadeRun;
	private Control? _selectionFadeCardsRow;
	private int _selectionFadeBaselineScreens = -1;
	private bool _selectionFadeFinished;

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
				if (_cardsRow.GetChild(i) is Control slot && !IsNoiseSlot(i))
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
				|| IsNoiseSlot(slotIndex)
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

			HextechKiwiVfxPlayer? player = AttachCardVfx(
				slot,
				slotIndex,
				TierVfx(_relics[slotIndex], "HoverVFX"),
				HoverVfxLayer,
				include: static emitter => !HextechKiwiVfxLibrary.IsOneShotIntro(emitter));
			if (player != null)
			{
				player.FadeInFrom(HoverVfxSkipSeconds, HoverVfxFadeInSeconds);
				_cardHoverVfx[slotIndex] = player;
			}
		});
	}

	private void StopCardHoverVfx(int slotIndex, bool immediately = false)
	{
		if (_cardHoverVfx.Remove(slotIndex, out HextechKiwiVfxPlayer? player) && GodotObject.IsInstanceValid(player))
		{
			player.FadeOutAndFree(immediately ? 0f : HoverVfxFadeOutSeconds);
		}
	}

	/// <summary>
	/// 确认选择时：选中的卡播放选中特效（盖在卡面内容上），其余卡播放未选中特效（卡面内容下方的灰卡）。
	/// 原版按钮此时切到选中状态，状态元素只剩常驻光，悬停光直接撤掉、不淡出。
	/// </summary>
	private void PlaySelectionVfx()
	{
		HextechPresentation.TryRun("Mayhem", "SelectionScreen.PlaySelectionVfx failed", () =>
		{
			foreach (int slotIndex in _cardHoverVfx.Keys.ToArray())
			{
				StopCardHoverVfx(slotIndex, immediately: true);
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
					AttachCardVfx(slot, i, TierVfx(_relics[i], "SelectedVFX"), PickedVfxLayer);
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

	/// <summary>
	/// 界面关闭时按原版场景退场淡出：界面已经移出叠加层栈，这里只是让节点多留一会儿。
	/// 三个卡槽（卡牌、卡上的特效与重随按钮）保持原大小和位置，一起在 <see cref="SelectionSceneFadeOutSeconds"/> 内淡出，
	/// 选中卡保留选中态；原版场景里没有对应物的本模组元素（面板底、标题、敌方预览、操作按钮）直接透明，
	/// 免得在地图上留半透明黑底；全部不接收鼠标，淡完释放整个界面。
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
			Control cardsRow = _cardsRow;
			// 卡槽那一行以外的本模组元素直接透明。
			for (Node node = cardsRow; node != this && node.GetParent() is Node parent; node = parent)
			{
				foreach (Node sibling in parent.GetChildren())
				{
					if (sibling != node && sibling is CanvasItem item)
					{
						item.Modulate = Colors.Transparent;
					}
				}
			}

			// 卡槽行的各级父容器自身也会画底板（半透明黑底等），只隐藏它们自己的绘制，子节点照常显示。
			for (Node? node = cardsRow; node != null; node = node.GetParent())
			{
				if (node is CanvasItem ancestor)
				{
					ancestor.SelfModulate = Colors.Transparent;
				}
				if (node == this)
				{
					break;
				}
			}

			Visible = true;
			// 关闭后紧接着可能打开地图，地图画在叠加层栈之上，会盖住还在渐隐的卡。
			// 下一帧把界面挪到根节点下单独的高层画布（叠加层栈此刻正在移除本界面，不在这里直接改父节点）。
			Callable.From(MoveToSelectionFadeLayer).CallDeferred();
			_selectionFadeTween = StartSelectionFadeTween(cardsRow, SelectionSceneFadeOutSeconds, SelectionSceneFadeOutTransition, SelectionSceneFadeOutEase);
			WatchSelectionFadeInterruptions(cardsRow);
			started = true;
		});
		return started;
	}

	private Tween StartSelectionFadeTween(Control cardsRow, float seconds, Tween.TransitionType transition, Tween.EaseType ease)
	{
		Tween tween = CreateTween().SetParallel();
		foreach (Node child in cardsRow.GetChildren())
		{
			if (child is CanvasItem cardSlot)
			{
				tween.TweenProperty(cardSlot, "modulate:a", 0f, seconds)
					.SetTrans(transition)
					.SetEase(ease);
			}
		}
		tween.Chain().TweenCallback(Callable.From(FinishSelectionFadeOut));
		return tween;
	}

	/// <summary>
	/// 退场开始时本界面还在栈的列表里（原版 Remove 先回调 AfterOverlayClosed 再删列表），所以基准是当前数量减一；
	/// 之后栈里界面多于基准就是有新界面入栈。地图不在叠加层栈里，单纯打开地图不会打断退场。
	/// 局内场景退出（回主菜单、读档）时立即释放，不在菜单上残留。
	/// </summary>
	private void WatchSelectionFadeInterruptions(Control cardsRow)
	{
		if (NOverlayStack.Instance is { } stack)
		{
			_selectionFadeStack = stack;
			_selectionFadeBaselineScreens = Math.Max(0, stack.ScreenCount - 1);
			_selectionFadeCardsRow = cardsRow;
			stack.Changed += OnOverlayStackChangedDuringFade;
		}

		if (NRun.Instance is Node run)
		{
			_selectionFadeRun = run;
			run.TreeExiting += FinishSelectionFadeOut;
		}
	}

	private void OnOverlayStackChangedDuringFade()
	{
		if (_selectionFadeFinished
			|| _selectionFadeStack == null
			|| !GodotObject.IsInstanceValid(_selectionFadeStack)
			|| _selectionFadeStack.ScreenCount <= _selectionFadeBaselineScreens
			|| _selectionFadeCardsRow is not { } cardsRow
			|| !GodotObject.IsInstanceValid(cardsRow))
		{
			return;
		}

		// 只压缩一次：基准抬到当前数量，后续入栈不再重复处理。
		_selectionFadeBaselineScreens = int.MaxValue;
		_selectionFadeTween?.Kill();
		_selectionFadeTween = StartSelectionFadeTween(cardsRow, SelectionFadeInterruptedSeconds, Tween.TransitionType.Linear, Tween.EaseType.InOut);
	}

	private void MoveToSelectionFadeLayer()
	{
		if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion() || !IsInsideTree())
		{
			return;
		}

		_selectionFadeLayer = new CanvasLayer
		{
			Name = "HextechSelectionFadeLayer",
			Layer = SelectionFadeCanvasLayer
		};
		GetTree().Root.AddChild(_selectionFadeLayer);
		Reparent(_selectionFadeLayer, keepGlobalTransform: true);
	}

	private void FinishSelectionFadeOut()
	{
		if (_selectionFadeFinished)
		{
			return;
		}

		_selectionFadeFinished = true;
		_selectionFadeTween?.Kill();
		if (_selectionFadeRun != null && GodotObject.IsInstanceValid(_selectionFadeRun))
		{
			_selectionFadeRun.TreeExiting -= FinishSelectionFadeOut;
		}
		if (_selectionFadeStack != null && GodotObject.IsInstanceValid(_selectionFadeStack))
		{
			_selectionFadeStack.Changed -= OnOverlayStackChangedDuringFade;
		}

		if (_selectionFadeLayer != null && GodotObject.IsInstanceValid(_selectionFadeLayer))
		{
			_selectionFadeLayer.QueueFree();
		}

		QueueFree();
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
