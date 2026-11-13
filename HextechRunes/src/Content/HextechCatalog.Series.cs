using MegaCrit.Sts2.Core.Models.Exceptions;
using static HextechRunes.HextechContentRegistry;

namespace HextechRunes;

internal static partial class HextechCatalog
{
	private readonly record struct IndexedRuneType(Type Type, int Index);
	private static readonly HashSet<Type> MissingVisibleCustomRelicLogs = [];

	public static IReadOnlyList<RelicModel> GetCanonicalRunes()
	{
		return PlayerRuneMetadata.AllTypes
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)))
			.ToArray();
	}

	public static IReadOnlyList<RelicModel> GetCanonicalGenericVisibleRunes()
	{
		return GetGenericVisibleRuneTypes()
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)))
			.ToArray();
	}

	public static IReadOnlyList<RuneSeriesGroup> GetCharacterRuneGroups()
	{
		return CharacterRunePools
			.Where(static pool => HextechPlayerContextHelper.IsCharacterPoolPresentInGame(pool.Pool))
			.Select(static pool => new RuneSeriesGroup(
				$"CHARACTER.{pool.LocalizationKey}",
				pool.RuneTypes
					.Select(static (type, index) => new IndexedRuneType(type, index))
					.Where(static rune => IsPlayerRuneTypeVisibleInCollection(rune.Type))
					.OrderBy(static rune => PlayerRuneMetadata.GetRaritySortOrder(rune.Type))
					.ThenBy(static rune => rune.Index)
					.Select(static rune => ModelDb.GetById<RelicModel>(ModelDb.GetId(rune.Type)))
					.ToArray()))
			.Where(static group => group.Relics.Count > 0)
			.ToArray();
	}

	public static IReadOnlyList<RelicModel> GetCanonicalForges()
	{
		return AllForgeTypes
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)))
			.ToArray();
	}

	public static IReadOnlyList<RelicModel> GetCanonicalVisibleCustomRelics()
	{
		return AllCustomRelicTypes
			.Where(static type => !PlayerRuneMetadata.AllTypes.Contains(type) || IsPlayerRuneTypeVisibleInCollection(type))
			.Select(static type => TryGetCanonicalVisibleCustomRelic(type, out RelicModel? relic) ? relic : null)
			.OfType<RelicModel>()
			.ToArray();
	}

	private static bool TryGetCanonicalVisibleCustomRelic(Type type, out RelicModel? relic)
	{
		ModelId id = ModelDb.GetId(type);
		try
		{
			relic = ModelDb.GetById<RelicModel>(id);
			return true;
		}
		catch (ModelNotFoundException ex)
		{
			relic = null;
			lock (MissingVisibleCustomRelicLogs)
			{
				if (MissingVisibleCustomRelicLogs.Add(type))
				{
					HextechLog.Warn("Inspect", $"Skipping missing visible custom relic during inspect list build: type={type.FullName} id={id.Entry}: {ex.Message}");
				}
			}

			return false;
		}
	}
}
