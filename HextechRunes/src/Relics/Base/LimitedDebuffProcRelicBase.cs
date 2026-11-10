using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace HextechRunes;

public abstract class LimitedDebuffProcRelicBase : TurnScopedRelicBase
{
	private int _procsThisTurn;

	// 无上限的子类不再写入这个计数，但属性本身保留：它在 SavedProperty 清单里，删掉会改变保存与联机布局。
	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedProcsThisTurn
	{
		get
		{
			// 序列化可能发生在回合钩子之外（例如读档后的首个回合钩子之前）：先按持有者回合号懒清零，
			// 保证写出的是本回合的计数而不是上一回合的残值。
			EnsureTurnScopedStateCurrent();
			return _procsThisTurn;
		}
		set
		{
			_procsThisTurn = Math.Max(0, value);
			UpdateDisplay();
			UpdateTurnScopedStateIdentity();
		}
	}

	protected virtual int MaxProcsPerTurn => 3;

	/// <summary>false = 不限每回合次数，也不显示剩余次数。</summary>
	protected virtual bool HasTurnLimit => true;

	/// <summary>true = 监听持有者自己收到的负面效果（来源不限）；false = 监听持有者给敌人施加的负面效果。</summary>
	protected virtual bool ListensToOwnerDebuffs => false;

	public override bool ShowCounter => HasTurnLimit && IsInLiveCombat;

	public override int DisplayAmount => HasTurnLimit && !IsCanonical ? Math.Max(0, MaxProcsPerTurn - _procsThisTurn) : 0;

	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (HasTurnLimit)
		{
			EnsureTurnScopedStateCurrent();
		}

		if (!TryMatchProc(power, amount, applier, out Creature? target))
		{
			return;
		}

		if (HasTurnLimit)
		{
			if (!TryConsumeTurnProc(ref _procsThisTurn, MaxProcsPerTurn))
			{
				return;
			}

			UpdateDisplay();
		}

		Flash([target]);
		await OnDebuffProc(Owner, target);
	}

	/// <summary>触发回调。监听敌方时 target 是收到负面效果的敌人；监听自身时 target 是持有者自己。</summary>
	protected abstract Task OnDebuffProc(Player owner, Creature target);

	/// <summary>判定这次能力变化是否触发。默认按 <see cref="ListensToOwnerDebuffs"/> 匹配负面效果；子类可改为其他口径。</summary>
	protected virtual bool TryMatchProc(PowerModel power, decimal amount, Creature? applier, [NotNullWhen(true)] out Creature? target)
	{
		return ListensToOwnerDebuffs
			? TryGetOwnerReceivedDebuff(power, amount, out target)
			: TryGetOwnedEnemyDebuffTarget(power, amount, applier, out target);
	}

	protected override void ResetTurnScopedState()
	{
		_procsThisTurn = 0;
		UpdateDisplay();
	}

	private void UpdateDisplay()
	{
		Status = HasTurnLimit && _procsThisTurn == MaxProcsPerTurn - 1 ? RelicStatus.Active : RelicStatus.Normal;
		InvokeDisplayAmountChanged();
	}
}
