using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes.Tests;

internal static partial class Program
{
	/// <summary>
	/// 写入只读/私有 set 自动属性的编译器支持字段（<c>&lt;Name&gt;k__BackingField</c>）。
	/// 从实例的运行时类型沿继承链查找；链上出现多个同名支持字段（派生类覆写了自动属性）时直接报错，
	/// 避免悄悄写到另一层。
	/// </summary>
	private static void SetAutoProperty(object target, string propertyName, object? value)
	{
		string fieldName = $"<{propertyName}>k__BackingField";
		FieldInfo? found = null;
		for (Type? type = target.GetType(); type != null; type = type.BaseType)
		{
			FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
			if (field == null)
			{
				continue;
			}

			if (found != null)
			{
				throw new InvalidOperationException($"{target.GetType().FullName}.{propertyName} has backing fields on both {found.DeclaringType?.FullName} and {type.FullName}");
			}

			found = field;
		}

		if (found == null)
		{
			throw new InvalidOperationException($"{target.GetType().FullName}.{propertyName} has no auto-property backing field");
		}

		found.SetValue(target, value);
	}

	private static (HextechEnemyHexContext Context, Player First, Player Second) CreatePrismaticEnemyFixture()
	{
		RunState run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
		Player first = CreateOrdinalTestPlayer(1), second = CreateOrdinalTestPlayer(2);
		AccessTools.Field(typeof(RunState), "_players").SetValue(run, new List<Player> { first, second });
		FieldInfo history = AccessTools.Field(typeof(RunState), "_mapPointHistory");
		history.SetValue(run, Activator.CreateInstance(history.FieldType));
		AccessTools.Property(typeof(RunState), "Rng").SetValue(run, new RunRngSet("FOUR-ENEMY-HEXES"));
		CombatState combat = new(runState: run);
		foreach (Player player in new[] { first, second })
		{
			AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
			SetAutoProperty(player, nameof(Player.Creature), CreatePrismaticTestCreature(CombatSide.Player, combat));
			PlayerCombatState state = (PlayerCombatState)RuntimeHelpers.GetUninitializedObject(typeof(PlayerCombatState));
			SetAutoProperty(state, nameof(PlayerCombatState.Hand), new CardPile(PileType.Hand));
			SetAutoProperty(state, nameof(PlayerCombatState.DrawPile), new CardPile(PileType.Draw));
			SetAutoProperty(state, nameof(PlayerCombatState.DiscardPile), new CardPile(PileType.Discard));
			SetAutoProperty(state, nameof(PlayerCombatState.ExhaustPile), new CardPile(PileType.Exhaust));
			SetAutoProperty(state, nameof(PlayerCombatState.PlayPile), new CardPile(PileType.Play));
			SetAutoProperty(player, nameof(Player.Deck), new CardPile(PileType.Deck));
			AccessTools.Property(typeof(Player), "PlayerCombatState").SetValue(player, state);
		}
		HextechMayhemModifier modifier = CreateMutableTestModel<HextechMayhemModifier>();
		AccessTools.Field(typeof(ModifierModel), "_runState").SetValue(modifier, run);
		return (new HextechEnemyHexContext(modifier), first, second);
	}

	/// <summary>
	/// 只带 NetId 与角色的玩家壳（不跑构造函数，见 <see cref="CreateOrdinalTestPlayer"/>）。角色模型同样不构造，
	/// 只写入按类型推导的 ModelId，足够让 <c>player.Character.Id</c> 的角色判定走真实路径；没有 RunState、遗物与战斗状态。
	/// </summary>
	private static Player CreateCharacterTestPlayer(Type characterType, ulong netId = 1)
	{
		CharacterModel character = (CharacterModel)RuntimeHelpers.GetUninitializedObject(characterType);
		SetAutoProperty(character, nameof(AbstractModel.Id), ModelDb.GetId(characterType));
		Player player = CreateOrdinalTestPlayer(netId);
		SetAutoProperty(player, nameof(Player.Character), character);
		return player;
	}

	private static Creature CreatePrismaticTestCreature(CombatSide side, CombatState combat)
	{
		Creature creature = (Creature)RuntimeHelpers.GetUninitializedObject(typeof(Creature));
		SetAutoProperty(creature, nameof(Creature.Side), side);
		AccessTools.Field(typeof(Creature), "_currentHp").SetValue(creature, 50);
		AccessTools.Property(typeof(Creature), "CombatState").SetValue(creature, combat);
		return creature;
	}

	/// <summary>
	/// 测试进程不跑原版 ModelDb 初始化:临时注入本测试用到、尚未登记的模型;释放时只移除本次注入的,
	/// 别的测试已登记的类型不受影响。
	/// </summary>
	private static IDisposable InjectMissingModels(params Type[] types)
	{
		Type[] added = types.Where(static type => !ModelDb.Contains(type)).ToArray();
		foreach (Type type in added)
		{
			ModelDb.Inject(type);
		}

		return new InjectedModels(added);
	}

	private sealed class InjectedModels(Type[] types) : IDisposable
	{
		public void Dispose()
		{
			foreach (Type type in types)
			{
				ModelDb.Remove(type);
			}
		}
	}

	/// <summary>
	/// 让 <see cref="HextechRuneConfiguration"/> 视为配置已加载,并可在测试里切换总开关,使启用判断走真实路径。
	/// 只改测试进程内存;释放时还原 <c>_loaded</c> 与 <c>ModEnabled</c>。
	/// </summary>
	private sealed class ModEnabledOverride : IDisposable
	{
		private readonly FieldInfo _loadedField = AccessTools.Field(typeof(HextechRuneConfiguration), "_loaded");
		private readonly object _config = AccessTools.Field(typeof(HextechRuneConfiguration), "_config").GetValue(null)!;
		private readonly PropertyInfo _enabledProperty;
		private readonly object? _wasLoaded;
		private readonly object? _wasEnabled;

		public ModEnabledOverride()
		{
			_enabledProperty = _config.GetType().GetProperty("ModEnabled")!;
			_wasLoaded = _loadedField.GetValue(null);
			_wasEnabled = _enabledProperty.GetValue(_config);
			_loadedField.SetValue(null, true);
		}

		public bool Enabled
		{
			set => _enabledProperty.SetValue(_config, value);
		}

		public void Dispose()
		{
			_enabledProperty.SetValue(_config, _wasEnabled);
			_loadedField.SetValue(null, _wasLoaded);
		}
	}

	// PowerModel 的 Owner 与层数没有公开 setter;测试直接写字段,不走需要战斗与 Godot 节点的 PowerCmd。
	private static T CreateTestPower<T>(int? amount = null, Creature? owner = null)
		where T : PowerModel, new()
	{
		T power = CreateMutableTestModel<T>();
		if (owner != null)
		{
			AccessTools.Property(typeof(PowerModel), nameof(PowerModel.Owner)).SetValue(power, owner);
		}

		if (amount != null)
		{
			AccessTools.Field(typeof(PowerModel), "_amount").SetValue(power, amount.Value);
		}

		return power;
	}

	private sealed class GeneratedTestRelic : RelicModel, IHextechGeneratedRune
	{
		public override RelicRarity Rarity => RelicRarity.Event;
		public string Data { get; set; } = "";
		public string ExportSelectionData() => Data;
		public bool TryImportSelectionData(string data) { Data = data; return data.StartsWith("recipe:", StringComparison.Ordinal); }
	}
}
