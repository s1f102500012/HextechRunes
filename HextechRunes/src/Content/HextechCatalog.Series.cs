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

	// 本类只负责“有哪些、怎么分组排序”。某个符文此刻该不该列出取决于配置开关与本机进度，
	// 由调用方（HextechCollectionVisibility）以 isRuneVisible 传入，注册表不查配置与进度。
	public static IReadOnlyList<RelicModel> GetCanonicalGenericRunes(Func<Type, bool> isRuneVisible)
	{
		PlayerRuneMetadataCatalog metadata = PlayerRuneMetadata;
		return metadata.AllTypes
			.Where(isRuneVisible)
			.Where(type => !metadata.CharacterSpecificTypes.Contains(type))
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)))
			.ToArray();
	}

	public static IReadOnlyList<RuneSeriesGroup> GetCharacterRuneGroups(Func<Type, bool> isRuneVisible)
	{
		return CharacterRunePools
			.Where(static pool => HextechPlayerContextHelper.IsCharacterPoolPresentInGame(pool.Pool))
			.Select(pool => new RuneSeriesGroup(
				$"CHARACTER.{pool.LocalizationKey}",
				pool.RuneTypes
					.Select(static (type, index) => new IndexedRuneType(type, index))
					.Where(rune => isRuneVisible(rune.Type))
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
			.Where(IsForgeTypeShownInUi)
			.Select(static type => ModelDb.GetById<RelicModel>(ModelDb.GetId(type)))
			.ToArray();
	}

	public static IReadOnlyList<RelicModel> GetCanonicalCustomRelics(Func<Type, bool> isRuneVisible)
	{
		return AllCustomRelicTypes
			.Where(type => !PlayerRuneMetadata.AllTypes.Contains(type) || isRuneVisible(type))
			.Where(IsForgeTypeShownInUi)
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
