namespace HextechRunes;

internal static class HextechThoughtOverwriteKeywordPersistenceHooks
{
	// 顺序即存档里标记项的追加顺序与读档恢复顺序，不能重排。
	private static readonly KeywordPersistenceTracker[] Trackers =
	[
		KeywordPersistenceTrackers.ThoughtOverwrite,
		KeywordPersistenceTrackers.CurtainCall,
		KeywordPersistenceTrackers.CosplayInnate,
		KeywordPersistenceTrackers.CorruptedBranchInnate,
		KeywordPersistenceTrackers.UndyingEthereal
	];

	/// <summary>按 <see cref="Trackers"/> 下标记录的"需要保留"位图。</summary>
	private readonly struct KeywordPersistenceSnapshot
	{
		private readonly int _persistMask;

		private KeywordPersistenceSnapshot(int persistMask)
		{
			_persistMask = persistMask;
		}

		internal static KeywordPersistenceSnapshot Capture(CardModel? card)
		{
			if (card == null)
			{
				return default;
			}

			int mask = 0;
			for (int i = 0; i < Trackers.Length; i++)
			{
				if (Trackers[i].ShouldPersist(card))
				{
					mask |= 1 << i;
				}
			}

			return new KeywordPersistenceSnapshot(mask);
		}

		internal void Restore(CardModel? card)
		{
			if (card == null)
			{
				return;
			}

			for (int i = 0; i < Trackers.Length; i++)
			{
				if ((_persistMask & (1 << i)) != 0)
				{
					Trackers[i].Restore(card);
				}
			}
		}
	}

	/// <summary>存档：按固定顺序为需要保留的关键词追加值为 1 的标记（已有同名项不重复写）。</summary>
	internal static void WriteMarkers(CardModel card, SerializableCard save)
	{
		foreach (KeywordPersistenceTracker tracker in Trackers)
		{
			if (tracker.ShouldPersist(card))
			{
				HextechCardSavedProps.AddIntIfMissing(save, tracker.MarkerSavedPropertyName, 1);
			}
		}
	}

	/// <summary>读档：带非 0 标记的关键词恢复到卡上并重新追踪。</summary>
	internal static void RestoreFromMarkers(SerializableCard save, CardModel card)
	{
		foreach (KeywordPersistenceTracker tracker in Trackers)
		{
			if (HextechCardSavedProps.HasNonZeroInt(save.Props, tracker.MarkerSavedPropertyName))
			{
				tracker.Restore(card);
			}
		}
	}

	// 原版克隆(DeepCloneFields)只按"有来源"的关键词重建 _keywords,思维覆写/谢幕/扮演/腐化枝/不死这类
	// 运行期附加的关键词与追踪标记都会丢;镜中倒影、复视等复制整副牌组的路径拿到的副本因此没有虚无词条。
	// 牌组级克隆把源牌的持久化快照原样恢复到副本上,副本自己成为被追踪的牌组版本。
	[HarmonyPatch(typeof(RunState), nameof(RunState.CloneCard), typeof(CardModel))]
	[HextechPatch("card.keyword-persistence.clone-deck", "关键词持久化")]
	private static class RunStateCloneCardPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel mutableCard, CardModel __result)
		{
			KeywordPersistenceSnapshot.Capture(mutableCard).Restore(__result);
		}
	}

	[HarmonyPatch(typeof(CombatState), nameof(CombatState.CloneCard), typeof(CardModel))]
	[HextechPatch("card.keyword-persistence.clone-combat", "关键词持久化")]
	private static class CombatStateCloneCardPatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel mutableCard, CardModel __result)
		{
			KeywordPersistenceSnapshot.Capture(mutableCard).Restore(__result);
		}
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.ToSerializable), new Type[0])]
	[HextechPatch("card.keyword-persistence.save", "关键词持久化")]
	private static class ToSerializablePatch
	{
		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, SerializableCard __result)
		{
			WriteMarkers(__instance, __result);
		}
	}

	[HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable), typeof(SerializableCard))]
	[HextechPatch("card.keyword-persistence.load", "关键词持久化")]
	private static class FromSerializablePatch
	{
		[HarmonyPostfix]
		private static void Postfix(SerializableCard save, CardModel __result)
		{
			RestoreFromMarkers(save, __result);
		}
	}

	[HarmonyPatch]
	[HextechPatch("card.keyword-persistence.rebuild", "关键词持久化")]
	private static class KeywordRebuildPatch
	{
		[HarmonyTargetMethods]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(CardModel), nameof(CardModel.DowngradeInternal), Type.EmptyTypes);
			yield return AccessTools.Method(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal), Type.EmptyTypes);
		}

		[HarmonyPrefix]
		private static void Prefix(CardModel __instance, out KeywordPersistenceSnapshot __state)
		{
			__state = KeywordPersistenceSnapshot.Capture(__instance);
		}

		[HarmonyPostfix]
		private static void Postfix(CardModel __instance, KeywordPersistenceSnapshot __state)
		{
			__state.Restore(__instance);
		}
	}
}
