namespace HextechRunes;

public sealed partial class DoubleVisionRune
{
	private sealed class CardRewardTracker
	{
		public CardRewardTracker(Player player)
		{
			Player = player;
		}

		public Player Player { get; }

		public List<CardModel> AddedCards { get; } = [];
	}

	private class RewardDuplicationScope
	{
		public RewardDuplicationScope(IReadOnlyList<DoubleVisionRune> runes)
		{
			Runes = runes;
		}

		public IReadOnlyList<DoubleVisionRune> Runes { get; }
	}

	private sealed class DirectCommandRewardScope : RewardDuplicationScope
	{
		public DirectCommandRewardScope(Player player, IReadOnlyList<DoubleVisionRune> runes, int previousSuppressionDepth)
			: base(runes)
		{
			Player = player;
			PreviousSuppressionDepth = previousSuppressionDepth;
		}

		public Player Player { get; }

		public int PreviousSuppressionDepth { get; }

		public decimal GoldAmount { get; set; }

		public bool WasGoldStolenBack { get; set; }
	}

	// 佩尔之翼献祭换来的遗物：卡牌奖励在所有端按同步选择执行，复制也在所有端各自进行，不走广播。
	private sealed class ReplicatedRelicRewardScope : RewardDuplicationScope
	{
		public ReplicatedRelicRewardScope(Player player, IReadOnlyList<DoubleVisionRune> runes)
			: base(runes)
		{
			Player = player;
		}

		public Player Player { get; }
	}

	private sealed record PaelsWingSacrificeState(Player? PreviousOwner);

	// 一次事件选项内获得的遗物先只记账，等原选项 Task 完成后再按获得顺序逐个复制：
	// 原版在选项内可能连续获得多件，复制必须串行，且收尾后迟到的异步获得不再并入本批。
	internal sealed class EventRelicTransaction
	{
		private readonly List<EventRelicIntent> _items = [];
		private bool _commitStarted;

		public EventRelicTransaction(
			RunState runState,
			EventRoom eventRoom,
			EventRelicTransactionBatch batch)
		{
			RunState = runState;
			EventRoom = eventRoom;
			Batch = batch;
		}

		public RunState RunState { get; }

		public EventRoom EventRoom { get; }

		public EventRelicTransactionBatch Batch { get; }

		public int Count => _items.Count;

		public bool IsCommitting { get; private set; }

		public bool IsAcceptingRecords { get; private set; } = true;

		public bool TryRecord(EventRelicIntent intent)
		{
			if (!IsAcceptingRecords)
			{
				return false;
			}

			_items.Add(intent);
			return true;
		}

		public void CloseForRecording()
		{
			IsAcceptingRecords = false;
		}

		public async Task CommitSequentially(Func<EventRelicIntent, Task> commit)
		{
			if (_commitStarted)
			{
				throw new InvalidOperationException("The event transaction has already been committed.");
			}

			CloseForRecording();
			_commitStarted = true;
			IsCommitting = true;
			try
			{
				foreach (EventRelicIntent intent in _items)
				{
					await commit(intent);
				}
			}
			finally
			{
				IsCommitting = false;
			}
		}
	}

	internal sealed class EventRelicTransactionBatch
	{
		private int _activeTransactions;
		private bool _hasCommittedRewardsSinceLastSave;

		public void Begin()
		{
			_activeTransactions++;
		}

		public bool Complete(bool committedRewards, bool canSaveFinishedAncientEvent)
		{
			if (_activeTransactions <= 0)
			{
				throw new InvalidOperationException("Event relic transaction batch completed without a matching begin.");
			}

			_activeTransactions--;
			_hasCommittedRewardsSinceLastSave |= committedRewards;
			if (_activeTransactions != 0
				|| !_hasCommittedRewardsSinceLastSave
				|| !canSaveFinishedAncientEvent)
			{
				return false;
			}

			_hasCommittedRewardsSinceLastSave = false;
			return true;
		}
	}

	internal sealed record EventRelicIntent(
		Player Player,
		RelicModel ObtainedRelic,
		IReadOnlyList<DoubleVisionRune> Runes);

	private sealed record EventRelicTransactionScope(
		EventRelicTransaction Transaction,
		EventRelicTransaction? Previous);

	private sealed record EventRelicRecordScope(
		EventRelicTransaction Transaction,
		Player Player,
		RelicModel AttemptedRelic,
		IReadOnlyList<DoubleVisionRune> Runes,
		int PreviousObtainDepth,
		bool IsOutermostObtain);

	private sealed class CardRewardTrackingScope : RewardDuplicationScope
	{
		public CardRewardTrackingScope(Player player, IReadOnlyList<DoubleVisionRune> runes, CardRewardTracker? previousTracker)
			: base(runes)
		{
			Tracker = new CardRewardTracker(player);
			PreviousTracker = previousTracker;
		}

		public CardRewardTracker Tracker { get; }

		public CardRewardTracker? PreviousTracker { get; }
	}
}
