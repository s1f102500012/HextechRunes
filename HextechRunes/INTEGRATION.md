# 外部模组对接指南

本文面向想把自己的内容接入“海克斯大乱斗”（manifest id `HextechRunes`）的模组作者。目前对外开放的是：玩家海克斯（符文）、锻造器、事件遗物、附魔图标、额外阶段提示，以及几个运行期辅助方法。

对接有两条路，按你的模组能不能硬依赖海克斯来选：

| | 软依赖：`HextechRunesInterop` | 硬依赖：`HextechRunesApi` |
| --- | --- | --- |
| 引用 `HextechRunes.dll` | 不需要，靠反射或 RitsuLib 的 `[ModInterop]` 调用 | 需要，编译期引用 |
| 没装海克斯时 | 你的模组照常运行 | 你的模组加载失败 |
| 符文基类 | 任意 `RelicModel` | 必须继承 `HextechRelicBase` |
| 能用的内容 | 玩家海克斯、来源标签、配置菜单分组标题、额外阶段提示 | 全部（海克斯、锻造器、事件遗物、附魔图标、SavedProperty 载体、发放/选择辅助）；来源标签和分组标题同样调用 `HextechRunesInterop` |
| 签名稳定性 | 已发布的签名不再改动，新能力走新方法并递增 `ApiVersion` | 跟随海克斯版本，升级时可能需要重新编译 |

`HextechCatalog` 等 `internal` 类不属于对外契约，随时可能重构，请不要对它们打 Harmony 补丁。缺少什么能力，请直接开 issue。

## 一、软依赖接入（HextechRunesInterop）

### 版本检查

`HextechRunesInterop.ApiVersion`（`public static int`）从 0.9.7 开始提供。反射取不到这个属性，就说明对方的海克斯版本太旧，此时直接跳过对接即可。

| ApiVersion | 内容 |
| --- | --- |
| 1 | `RegisterPlayerRune`、`SetPlayerRunePoolLabel`、`RegisterConfigSectionTitle`、`RegisterExtraActProvider` |
| 2 | `RegisterPlayerRune` 的 `flags` 新增 `GuaranteedFirstPick`；新增 `SetPlayerRuneCharacter` |

旧版本不认识 `GuaranteedFirstPick`，传进去会抛 `ArgumentException`。用到它之前先确认 `ApiVersion >= 2`，否则不带这个标志注册。

### 注册时机

- 海克斯本体由加载器按游戏版本加载其中一个变体 DLL，程序集名固定为 `HextechRunes`（完整二创版通常也用这个程序集名）。按程序集名检测，不要按 manifest id 检测。
- 模组之间的初始化顺序不固定。先查一次已加载的程序集，没找到就订阅 `AppDomain.CurrentDomain.AssemblyLoad`，等 `HextechRunes` 载入后立即注册。
- 注册必须在模组初始化阶段完成：模型池要在共享遗物池首次枚举前登记，带 `[SavedProperty]` 的符文还要赶在存档序列化缓存初始化之前。窗口关闭后再调用，会抛 `InvalidOperationException`，并且不会留下半登记的状态。
- manifest 里不要写 `dependencies: ["HextechRunes"]`，否则没装海克斯的玩家加载不了你的模组。

### 符文类的要求

```csharp
public sealed class MyRune : RelicModel
{
	// 必须是 Starter，原因见下方说明。
	public override RelicRarity Rarity => RelicRarity.Starter;

	// 图标由你自己提供，不依赖海克斯的资源路径。
	public override string PackedIconPath => "res://MyMod/images/relics/my_rune.png";
	protected override string PackedIconOutlinePath => "res://MyMod/images/relics/my_rune_outline.png";
	protected override string BigIconPath => "res://MyMod/images/relics/my_rune.png";

	// 效果照常覆写原版 RelicModel 的钩子即可。
}
```

- 必须是具体、非泛型的 `RelicModel` 子类。
- **`Rarity` 必须是 `RelicRarity.Starter`。** 海克斯只把注册表里的符文从原版的自然遗物池中过滤掉，而原版还有别的路径会按稀有度抽取遗物，只有 Starter 能保证它们抽不到你的符文。稀有度不是 Starter 的外部符文，海克斯一律不发放，并在日志中给出警告。
- 标题、描述、风味文本按原版遗物的规则，写在你模组的 `relics.json` 里。
- `HextechRelicBase` 提供的扩展（如阵营回合钩子、`*Compat` 伤害修正、生成卡牌辅助）不对外部符文开放。外部符文请直接使用原版 `RelicModel` 的钩子。
- 使用原版回合钩子（`BeforeSideTurnStart`、`AfterSideTurnEnd` 等）时，先检查 `participants` 里有没有持有者。联机中，队友的额外回合（例如佩尔之眼）只会带那名玩家重新进入这些钩子，此时阵营仍是玩家侧，回合号也不会增加，只看阵营的判断会在别人的回合里多触发一次。原版玩家侧回合结束的 `participants` 只包含玩家本人，不包含宠物。"战斗第一回合"类效果请看持有者的 `PlayerCombatState.TurnNumber`，不要看 `RoundNumber`。

### RegisterPlayerRune

```csharp
public static void RegisterPlayerRune(
	Type runeType,
	string rarity,
	string? flags,
	string? characterPool,
	int characterOrder,
	string? tagKey,
	string? assetModId,
	Func<Player, bool>? isAvailableForPlayer);
```

| 参数 | 取值 |
| --- | --- |
| `runeType` | 你的符文类型 |
| `rarity` | 海克斯品级：`Silver` / `Gold` / `Prismatic`（不区分大小写，不接受数字） |
| `flags` | 逗号分隔的标志名，可以为 `null`：`Disabled`（默认在配置里关闭）、`FirstActExcluded`（第一幕不出现）、`ThirdActExcluded`（第三幕不出现，装了无尽模组时不生效）、`SelectionExcluded`（不进三选一和配置菜单，只能通过你自己的途径发放）、`AttributeConversionExclusive`（属性转换互斥组）、`GuaranteedFirstPick`（每幕首次三选一保底，见下文，需要 `ApiVersion >= 2`） |
| `characterPool` | 角色专属池：`Ironclad` / `Silent` / `Regent` / `Defect` / `Necrobinder`；`null` 为通用 |
| `characterOrder` | 角色池内的排序 |
| `tagKey` | 海克斯标签，`null` 为 `COMPREHENSIVE`，可选值见“标签”一节 |
| `assetModId` | 你的模组 id，用于配置菜单的来源显示；建议填写 |
| `isAvailableForPlayer` | 可选的发放过滤，见下文 |

参数无效时抛 `ArgumentException`（通过反射调用时包在 `TargetInvocationException` 里）。所有校验都在产生任何副作用之前完成。

同一个类型重复注册时，以第一次登记的元数据、资源归属和过滤委托为准，之后的登记只在日志里警告。

### isAvailableForPlayer

海克斯的所有发放路径（三选一、奖励、锻造器、宝箱替换）都会经过这个委托。可以用它实现“只在某个角色在场时出现”“只在联机时出现”之类的限制。

- 联机时两端都会执行这个委托，且必须得出同样的结果。只能读同步状态，例如 `Player` 的角色、遗物、牌组和 RunState；不要读本地设置、UI 或随机数。
- 委托抛出异常时，该符文按不可用处理，并记一条警告，两端的候选池因此仍然一致。

### GuaranteedFirstPick（每幕首次三选一保底）

适合角色专属联动符文：玩家用对应角色时，每幕第一次海克斯三选一里必定出现其中一个。

- 只作用于每一幕（包括额外阶段）的第一次三选一。同一幕的后续选择、锻造器选择不触发。
- 从带这个标志、且当前玩家能拿到的符文里挑一个，不受这一幕品级的限制。“能拿到”与普通候选池的判断相同：`isAvailableForPlayer` 返回 true、这一幕允许出现、玩家还没有、不与已有海克斯互斥、没有在配置里被关掉。一个都拿不到时，这一幕不保底。
- 优先挑本局还没出现过的；全部出现过时，从全部里挑。挑选用种子稳定随机，联机各端结果一致，也不消耗游戏的随机数。
- 保底符文放在最后一格。正常抽出的候选里已经有带这个标志的符文时，不再替换。
- 刷新保底那一格时，按这一幕原本的品级从普通候选池里重新抽，之后照常刷新。
- 限制“哪个角色能拿到”仍然靠 `isAvailableForPlayer`，这个标志本身不看角色。

### SetPlayerRuneCharacter

```csharp
public static void SetPlayerRuneCharacter(Type runeType, Type characterType);
```

需要 `ApiVersion >= 2`。指定符文所属的角色，给原版五个角色池以外的角色（例如你的模组角色）使用。三选一里，玩家当前角色的专属符文权重是通用符文的 150%（之后每抽到一个专属符文降 10 个百分点，每抽到一个通用符文升 10 个百分点）；原版角色靠 `characterPool` 标记专属符文，模组角色用这个方法标记。

- `characterType` 是你的 `CharacterModel` 子类，`runeType` 是已注册（或即将注册）的符文类型，调用顺序不限。
- 只影响权重，不限制谁能拿到。限制发放请用 `isAvailableForPlayer`。
- 以第一次登记为准，之后的登记只在日志里警告。类型不对时抛 `ArgumentException`。
- 联机两端要有同样的登记，所以只在模组初始化时调用。

### SetPlayerRunePoolLabel 与 RegisterConfigSectionTitle

```csharp
public static void SetPlayerRunePoolLabel(Type runeType, string poolKey);
public static void RegisterConfigSectionTitle(string assetModId, string titleKey);
```

这两个方法只改界面上的文字，不影响发放，也不影响联机，调用顺序和时机没有限制，建议与 `RegisterPlayerRune` 放在一起调用。

- `SetPlayerRunePoolLabel`：指定符文在三选一界面上显示的来源标签（配置菜单里同一分组内也按它排序）。文字取 `relic_collection` 表中的 `HEXTECH_POOL.<poolKey>`。
- `RegisterConfigSectionTitle`：指定配置菜单里分组显示时，属于 `assetModId` 的内容（符文和锻造器）使用的小标题。`titleKey` 是 `relic_collection` 表里的完整键。

两者都以第一次登记为准，之后的登记只在日志里警告。空白参数、以及不是 `RelicModel` 的类型会抛 `ArgumentException`。文字和默认值见下方“界面文字”一节。

### 反射调用示例

```csharp
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;

internal static class HextechBridge
{
	private const string HextechAssemblyName = "HextechRunes";

	// 在你的模组入口调用。
	public static void Initialize()
	{
		Assembly? hextech = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(assembly => assembly.GetName().Name == HextechAssemblyName);
		if (hextech != null)
		{
			TryRegister(hextech);
			return;
		}

		AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
	}

	private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
	{
		if (args.LoadedAssembly.GetName().Name != HextechAssemblyName)
		{
			return;
		}

		AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
		TryRegister(args.LoadedAssembly);
	}

	private static void TryRegister(Assembly hextech)
	{
		Type? interop = hextech.GetType("HextechRunes.HextechRunesInterop");
		object? version = interop?.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
		if (interop == null || version is not int apiVersion || apiVersion < 1)
		{
			Log.Info("[MyMod] HextechRunes is too old for interop registration; skipped.");
			return;
		}

		MethodInfo? register = interop.GetMethod(
			"RegisterPlayerRune",
			BindingFlags.Public | BindingFlags.Static,
			[typeof(Type), typeof(string), typeof(string), typeof(string), typeof(int), typeof(string), typeof(string), typeof(Func<Player, bool>)]);
		if (register == null)
		{
			Log.Warn("[MyMod] HextechRunesInterop.RegisterPlayerRune not found; skipped.");
			return;
		}

		try
		{
			register.Invoke(null,
			[
				typeof(MyRune),
				"Gold",
				"FirstActExcluded",
				null,
				0,
				"OUTPUT",
				"MyMod",
				(Func<Player, bool>)(player => player.Character is MyCharacter)
			]);
		}
		catch (TargetInvocationException ex)
		{
			Log.Warn($"[MyMod] Hextech rune registration failed: {ex.InnerException?.Message}");
			return;
		}

		// 界面文字：MY_MOD 对应 relic_collection.json 里的 HEXTECH_POOL.MY_MOD。
		interop.GetMethod("SetPlayerRunePoolLabel", [typeof(Type), typeof(string)])
			?.Invoke(null, [typeof(MyRune), "MY_MOD"]);
		interop.GetMethod("RegisterConfigSectionTitle", [typeof(string), typeof(string)])
			?.Invoke(null, ["MyMod", "MY_MOD_HEXTECH_SECTION"]);
	}
}
```

用 RitsuLib `[ModInterop]` 时，代理方法的参数列表要与上面的签名完全一致。

### RegisterExtraActProvider

```csharp
public static void RegisterExtraActProvider(Func<IRunState, string?> provider);
```

适用于在原版幕之外插入额外阶段的模组。委托返回非空的阶段 id 时，海克斯把当前视为这个额外阶段，并为该 id 单独发放一次海克斯选择；返回 `null` 或空串，就按原版幕处理。这个委托同样要求两端结果一致。

## 二、注册之后会发生什么

- 符文会加入原版 `SharedRelicPool`，以便获得模型身份、在图鉴中显示，但原版的自然遗物生成（宝箱、精英、商店等）会把它过滤掉。只有海克斯自己的流程会发放它。
- 符文会按品级、角色池、标志和标签进入海克斯的三选一、奖励和宝箱替换。
- 配置菜单里可以单独开关（带 `SelectionExcluded` 的除外）；同一品级里有多个来源时按来源分组，并显示分组小标题。
- 在图鉴中，符文列在“海克斯”分类下。
- 海克斯“复视”不会复制它。
- 需要持久数据时，照常在符文上写 `[SavedProperty]`。注册时海克斯会一并登记载体，前提是在上面说的注册窗口内。

## 三、界面文字

海克斯三选一界面上，每张卡片下方有三枚小标签：品级、来源标签、海克斯标签。配置菜单的海克斯池页和锻造器池页里，如果同一品级下有多个来源，还会按来源分组并显示小标题（例如“本体”“额外拓展包”）。以下三项由外部模组决定：

| 界面位置 | 设置方法 | 本地化键（`relic_collection` 表） | 未设置时 |
| --- | --- | --- | --- |
| 来源标签 | `SetPlayerRunePoolLabel(runeType, poolKey)` | `HEXTECH_POOL.<poolKey>` | 注册时带角色池的显示角色名，其余显示“通用” |
| 海克斯标签 | `RegisterPlayerRune` 的 `tagKey` | `HEXTECH_TAG.<tagKey>` | `COMPREHENSIVE`（综合） |
| 配置菜单分组小标题 | `RegisterConfigSectionTitle(assetModId, titleKey)` | `titleKey` 本身 | “外部模组：<assetModId>” |

可以直接用海克斯已有的键，不必自己写文字：

- 来源标签：`GENERIC` `IRONCLAD` `SILENT` `REGENT` `DEFECT` `NECROBINDER`
- 海克斯标签：`COMPREHENSIVE` `OUTPUT` `SURVIVAL` `RESOURCE` `ORB` `SUMMON` `RANDOM` `SWORDCRAFT` `DOOM` `POISON` `ECONOMY` `STATUS` `STARLIGHT` `STACKING` `MULTIPLAYER` `BLOODLETTING` `SHIV` `EXHAUST` `VOID` `TRICK` `DRAW` `COLORLESS`

海克斯标签还决定三选一的加权：玩家已拥有的同标签海克斯越多，同标签候选的权重越高。

使用新键时，在你模组每个语言目录下的 `relic_collection.json` 里补上文字。游戏只合并与原版同名的本地化表，文件名必须是 `relic_collection.json`；语言目录用游戏的语言代码（如 `zhs`、`eng`），其余语言缺失时回退到 `eng`。

```json
{
  "HEXTECH_POOL.MY_MOD": "我的模组",
  "HEXTECH_TAG.MY_TAG": "我的标签",
  "MY_MOD_HEXTECH_SECTION": "我的模组"
}
```

键不存在时不会报错，界面直接显示键名本身（如 `MY_MOD`），看到键名就说明本地化文件没生效。

额外拓展包（`HextechRunesSponsorPack`）不调用这两个方法，也会保留“拓展包”来源标签和“额外拓展包”分组标题。

## 四、联机须知

- 两端的模组列表必须一致，包括你的模组和海克斯的版本。
- 新增、改名或删除 `[SavedProperty]` 会改变存档同步的字段布局，新旧版本之间无法联机。请随版本号发布，并在更新日志里写明。
- 过滤委托和额外阶段委托只能依赖同步状态。

## 五、硬依赖接入（HextechRunesApi）

适合与海克斯一起发布、本来就要求玩家安装海克斯的模组，例如海克斯的额外拓展包。以下方法都要在初始化窗口内调用：

| 方法 | 用途 |
| --- | --- |
| `RegisterPlayerRune<T>(rarity, flags, characterPool, characterOrder, tagKey, assetModId)` | 注册继承 `HextechRelicBase` 的符文。若把图标放在 `res://<assetModId>/images/relics/<id 的小驼峰>.png`（例如 `MY_RUNE` 对应 `myRune.png`），会自动使用它 |
| `RegisterForge<T>(rarity, assetModId)` | 注册继承 `HextechForgeBase` 的锻造器 |
| `RegisterEventRelic<T>(assetModId)` | 注册事件遗物，加入原版事件遗物池 |
| `RegisterSavedPropertyCarrier<T>()` | 为不经上述方法注册、却带 `[SavedProperty]` 的模型登记载体 |
| `RegisterEnchantmentIcon<T>(iconPath)` | 为附魔登记图标 |

运行期辅助方法：`ObtainRandomForges`（按条件随机发放锻造器）、`SelectRelicOption`（联机同步的遗物选择）、`TrackPersistentInnate` / `RestorePersistentInnate`（持久固有标记），以及 `RelicBundleGrantHelper.GrantRelics`。

| 方法 | 用途 |
| --- | --- |
| `RegisterForgeShopPriceModifier(Func<RunState, int, int>)` | 初始化期登记"随机锻造器"商店售价修正。算价时按登记顺序调用 `modifier(runState, currentPrice)`，返回值作为新价格；只读同步状态、自行决定下限。某个修正器抛异常时跳过它并记 Warn |
| `IsHextechRelic(RelicModel?)` | 判断遗物是否由海克斯注册表管理（玩家海克斯、锻造器、商店锻造器、敌方海克斯展示遗物，含其他模组登记的符文）。不要用 `is HextechRelicBase` 代替 |
| `StableIndex(RunState, count, params string?[] salt)` | 运行种子稳定随机：按本局种子、当前幕、总层数与盐取 `[0, count)` 的下标，不消耗共享 RNG，两端输入一致结果就一致；`count <= 0` 抛 `ArgumentOutOfRangeException`。同层重复抽取要在盐里放区分量。算法与结果保持不变 |
| `StartWorkshopPlaytimeTracking(ulong publishedFileId)` | 让 Steam 为该创意工坊物品统计游玩时长与会话数（工坊统计页）。在模组入口调用一次即可，重复调用忽略；Steam 未初始化或失败时只记日志 |

另有两个可实现的接口：`IHextechHealingMultiplierProvider`（治疗乘区）和 `IHextechGeneratedRune`（生成式符文的实例数据）。

候选生成扩展点 `HextechRuneGeneration`：`RegisterChaosTransform(CandidateTransform)` 登记一个混沌变换（候选生成完成后调用；已有别的变换时抛 `InvalidOperationException`），`UnregisterChaosTransform` 注销同一个委托。变换不得消耗共享 RNG、不得修改已有候选实例；返回结果必须与输入同条数且全部是已登记的玩家海克斯，否则（包括抛异常、返回 null）本次回退原候选并记 Warn。结果会进入联机选择协议，两端必须算出相同结果。

通过上述任何入口登记本体内置的符文或锻造器都会被拒绝（只告警，不抛异常，也不产生池登记等副作用）。

硬依赖同样建议按程序集名检测、延迟注册，而不是在 manifest 里按 id 声明依赖，这样也能兼容程序集同名的二创版。

### 选择界面表现层（IHextechSelectionPresenter，实验性）

> 实验性接口：签名可能随版本调整，届时会在更新日志里写明。

想用自己的界面呈现海克斯三选一（例如换一套场景和动画）、但玩法完全交给本体的模组，可以登记一个表现层：

```csharp
HextechRunesApi.RegisterSelectionPresenter(new MyPresenter());

sealed class MyPresenter : IHextechSelectionPresenter
{
	public bool KeepsMapOpen => false;                                  // true：开选前不关地图；只对接管的界面生效，本体界面显示前仍会关地图
	public bool ShouldPresent(HextechSelectionSession session) => true; // 返回 false 时这一次仍用本体界面
	public void Present(HextechSelectionSession session) { /* 搭建自己的界面 */ }
}
```

- 只接管普通的玩家海克斯选择；“仅继续”、自选模式和锻造器选择仍用本体界面。最多登记一个表现层，之后的登记只记 Warn。
- 接管后本体界面不构建 UI、不用共享暗色背景，但仍在覆盖层栈里保存选择状态。`session.Screen` 离开场景树（`Closed`）即本次选择结束。
- 所有操作都走本体按钮原有的逻辑：`RerollOption`、`Choose`、`ConfirmEnemyHexes`、`RerollEnemyHex`、`ToggleEnemyHexRemoved`。重随次数、敌方海克斯权限（`CanAdjustEnemyHexes`）、联机同步和存档与本体界面一致。
- 显示用的数据：`Options`、`Rarity`、`GetOptionFooter`（本体卡面上的附加说明）、`GetEnemyHex` 返回的 `HextechEnemyHexInfo`（标题、已格式化描述、图标、品级、附加悬停提示、是否已移除）。
- 联机：`EnemyHexesChangedRemotely` 在权威玩家的敌方调整同步过来时触发；`HextechSelectionProgress.HasCompleted(netId)` 与 `Changed` 可用于显示“谁已经选好”。
- 金色重随：`IsGoldenRerollActive` 表示下一次重随会升级，`GoldenRerollRarity` 是升级后的品级；重随后用 `GetOptionRarity(slot)` 取该位置的实际品级（`Rarity` 保持开选时的品级）。
- 手柄：接管时本体界面没有可聚焦的节点。用 `SetControllerFocus(root, defaultFocus)` 告诉本体哪些节点属于表现层（焦点在其中时不会被抢回）以及默认焦点；不设置时本体不移动焦点。原版只按声明的焦点邻居移动焦点，表现层需要自己设置 `FocusNeighbor*`，并把原版的确认键（`MegaInput.select`）当作点击处理。
- 表现层模组要硬依赖海克斯。用到这些类型的代码最好放在单独的程序集里，等 `HextechRunes` 程序集载入后再加载，这样游戏扫描你的主程序集时不会因为找不到海克斯的类型而失败。

## 六、排障

- 日志前缀 `[HextechRunes][ExternalContent]`：包括重复注册冲突、非 Starter 外部符文被拒、过滤委托抛出异常。
- 注册窗口已关闭时抛出的异常信息中，会带上类型名和 SavedProperty 名称。
- 符文没有出现时，先确认：配置菜单里是否已开启，品级和标志是否把它排除在当前幕之外，过滤委托是否返回了 `true`。

---

## English quick reference

- Soft dependency (no reference to `HextechRunes.dll`): reflect on `HextechRunes.HextechRunesInterop` in the assembly named `HextechRunes`, check the `ApiVersion` property (≥ 1; available since 0.9.7), then call `RegisterPlayerRune(Type runeType, string rarity, string? flags, string? characterPool, int characterOrder, string? tagKey, string? assetModId, Func<Player, bool>? isAvailableForPlayer)` during mod initialization.
- The rune only needs to derive from `RelicModel`, but its `Rarity` **must** be `RelicRarity.Starter`; non-Starter external runes are never granted. Provide your own icons via `PackedIconPath` / `PackedIconOutlinePath` / `BigIconPath`.
- Rarity, flag and character pool arguments are enum *names* (case-insensitive): `Silver|Gold|Prismatic`; comma-separated `Disabled, FirstActExcluded, ThirdActExcluded, SelectionExcluded, AttributeConversionExclusive, GuaranteedFirstPick` (the last one needs `ApiVersion >= 2`); `Ironclad|Silent|Regent|Defect|Necrobinder` or `null`.
- Registered runes are kept out of vanilla natural relic generation, are skipped by Double Vision, and appear under the Hextech category in the compendium and in the config menu.
- UI labels: `SetPlayerRunePoolLabel(Type runeType, string poolKey)` picks the source pill on the selection screen (`HEXTECH_POOL.<poolKey>`; defaults to the character pool or `GENERIC`), and `RegisterConfigSectionTitle(string assetModId, string titleKey)` picks the config-menu group heading for everything registered with that `assetModId`. Put the texts in your mod's `relic_collection.json` (only vanilla table names are merged); a missing key is shown as the raw key instead of throwing. Tag pills read `HEXTECH_TAG.<tagKey>` the same way.
- `GuaranteedFirstPick` (ApiVersion 2): the first rune selection of every act (extra acts included) always offers one available flagged rune in the last slot, ignoring the act's rarity. Availability uses the normal filters (your predicate, act exclusions, owned/mutually exclusive, config-disabled); unseen runes are preferred, the pick is seed-stable and identical on every client. Rerolling that slot draws from the act's normal pool.
- `SetPlayerRuneCharacter(Type runeType, Type characterType)` (ApiVersion 2) marks a rune as belonging to a non-vanilla character so it gets the same 150% character weight that `characterPool` gives vanilla characters. Weight only; restrict who can get it with `isAvailableForPlayer`.
- `isAvailableForPlayer` runs on every client and must be deterministic over synchronized state; a throwing predicate excludes the rune.
- Hard-dependency helpers on `HextechRunesApi` also include `RegisterForgeShopPriceModifier(Func<RunState, int, int>)` (random-forge shop price modifiers, applied in registration order), `IsHextechRelic(RelicModel?)` (registry-based ownership check), `StableIndex(RunState, int count, params string?[] salt)` (seed-stable index that never consumes shared RNG) and `StartWorkshopPlaytimeTracking(ulong publishedFileId)` (asks Steam to count Workshop playtime and sessions for that item; call once from your mod initializer).
- Selection presentation (hard dependency): `HextechRunesApi.RegisterSelectionPresenter(IHextechSelectionPresenter)` lets another mod draw the player-rune selection itself while Hextech Mayhem keeps the gameplay (pools, rerolls, enemy hexes, saves, multiplayer sync). The presenter gets a `HextechSelectionSession` with the options, enemy hexes (`HextechEnemyHexInfo`), reroll/choose/remove actions that go through the same logic as the built-in buttons, `EnemyHexesChangedRemotely` for the authority's synced adjustments, `HextechSelectionProgress` for who has already chosen, golden reroll state (`IsGoldenRerollActive`, `GoldenRerollRarity`, `GetOptionRarity`) and controller focus (`SetControllerFocus`). Experimental: signatures may still change.
- Published interop signatures never change; new capabilities come as new methods with a higher `ApiVersion`. Internal classes such as `HextechCatalog` are not part of the contract; please do not patch them.
