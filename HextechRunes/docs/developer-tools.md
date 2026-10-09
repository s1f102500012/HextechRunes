# 开发工具与共享代码

先读 [开发规范](development.md)。下方 Python 命令从仓库根目录执行；也可以将脚本路径改为自己克隆目录下的绝对路径。表格中的 `tools/`、`src/` 路径相对于本体目录 `HextechRunes/`。工具从脚本自身位置找项目，不依赖旧合集工作区。

## 构建环境与外部工具

- 安装 .NET 9 SDK 和 Python 3。Godot 资源导入与 PCK 打包需要支持 .NET 的 Godot 编辑器；可通过 `GODOT_EDITOR` 指定其可执行文件。
- 按 `.csproj` 和构建脚本声明的目标版本准备游戏程序集。本体与拓展包共用版本化引用目录 `HextechRunes/versioned-dll-backups/<游戏版本>/game-refs/`；目录缺失时构建和 `run_tests.sh` 直接报错，不回退到本机游戏安装。程序集必须来自对应版本的本机游戏安装，不提交到 Git。
- `.csproj` 支持通过 `-p:GameDataDir=<程序集目录>` 指定引用目录。直接构建时，本体、拓展包与测试都用 `HextechSts2Target` 指定目标（拓展包经 ProjectReference 传给本体）；完整打包脚本会逐个构建它们声明支持的版本。版本符号、目标校验（未知目标直接报错）与游戏引用集中在仓库根 `Directory.Build.targets`，只对声明 `HextechUsesVariantTargets=true` 的工程生效，loader 与 mplab 不受影响。
- 游戏安装位置默认是维护者本机路径，可用环境变量 `STS2_GAME_APP` 覆盖（两个构建脚本与 mplab 一致）。两个 `build_and_deploy.sh` 是面向 macOS 的 Zsh 脚本，共用步骤在 `tools/lib_build.sh`；其它系统应使用适合本机的构建、资源导入与部署方式，不直接照搬 macOS 路径。
- 本体用 `HEXTECH_DEPLOY=0` 关闭部署，拓展包用 `HEXTECH_SPONSOR_DEPLOY=0`。只设置其中一个不会改变另一个脚本的行为。`HEXTECH_UPDATE_LATEST` 默认 0，本地构建不改写已跟踪的 `server/hextech-telemetry/public/latest-version.json`；发布时显式设 1。
- 两个包的加载器只有一份源码 `HextechRunes/loader/`：拓展包 loader 工程以链接方式编译它，身份常量在各自的 `LoaderBootstrap.Identity.cs`。拓展包没有自己的 `multi_version` 脚本，直接调用本体的。
- 原工作区的 `tools/sts2-inspect` 未包含在此仓库。需要原版 API 证据时，使用本机另行配置的反编译工具读取对应版本的 `sts2.dll` 与同目录依赖；不把旧工具路径当作本仓库提供的命令。

## 内容定位

```bash
python3 HextechRunes/tools/hextech_dev.py find '电球头'
python3 HextechRunes/tools/hextech_dev.py find SingularityAI --limit 3
```

输出中文名、品级、注册元数据、模型/敌方两种描述、源码和测试引用、已有图标路径。复用内容校验器的注册解析与键名规则，支持连续大写缩写；只索引本体的注册符文/敌方/锻造器，不扫描发行副本或拓展包。它是导航工具，不是 C# 语义分析器；修改后仍需阅读目标文件。卡牌/Power 等用 `rg` 在对应源码目录找。

## 同步重复文案

以下命令将每种语言的源键复制到同语言的目标键，默认仅展示 diff：

```bash
python3 HextechRunes/tools/hextech_dev.py loc-copy GLOBE_HEAD_HEX.description globeHeadHex.enemyDescription
# 明确两条文本语义相同后写入；--check 则只比较，有差异时退出 1
python3 HextechRunes/tools/hextech_dev.py loc-copy GLOBE_HEAD_HEX.description globeHeadHex.enemyDescription --apply
```

`--locale zhs` 可限制语言，可重复；默认覆盖磁盘上的全部语言。`--table` 默认 `relics`，也支持 `relic_collection/cards/powers`。先检查所有所选语言的键存在且为字符串，再改写；保留其它行格式和内容。此工具不翻译、不更新 TXT、不重打 PCK。退出 2 表示缺键、格式或调用错误。

**不能对所有我方/敌方描述批量互抄。** 比如借用我方图标的敌方海克斯有不同效果；Inklet 的敌方文本有多人缩放变量。源键写错也会被工具忠实复制，所以先看 diff 和实际代码。无需维护“所有别名必须相等”的测试白名单。

TXT 沿用现有工具：

```bash
python3 HextechRunes/tools/sync_content_txt.py
python3 HextechRunes/tools/sync_content_txt.py --help
```

不传参数为只读预览（不是 `--check` 参数）。工具会读取拓展包，报告可能超出本次修改；`--apply` 会更新三个 TXT 的生成部分，不自动覆盖人工描述。只对已批准条目用 `--accept-json '锚'`，删除旧条目需显式 `--prune`。不要为消除预览差异整表覆盖。

TXT 描述从对应模型的 `CanonicalVars` 和数值常量取得未升级基础值，支持同文件多个模型、标准变量及 `PowerVar`，并剥离 BBCode。敌方人数缩放读取 `MonsterHexCatalog` 的参数表，以 `N` 表示玩家人数；需要具体对局状态的值使用明确公式。遇到无法静态解析的变量会报错，不能让裸占位符进入说明，也不能猜一个数值。已有人工描述仍须通过 `--accept-json` 才会更新。

`tools/validate_hextech_content.py` 还会比较固定九语逐键占位符集合与 BBCode 配平（各语言标签数量不要求一致）；键集、格式检查同时覆盖 `HextechRunesSponsorPack/assets/localization`（报错带 `HextechRunesSponsorPack:` 前缀）。非 eng 的值与 eng 逐字相同、去掉占位符和标签后仍有 ≥4 个拉丁字母时按疑似漏译处理，两个包都逐条报 error；品牌名、目标语言同形词、原版该语言缺译而回退英文的原版名写进 `tools/localization_untranslated_allowlist.json` 并注明理由，过期条目同样报出。原版中文引用读取 `tools/official_zhs_titles.json`，运行时不依赖本机 PCK。名称检查覆盖 `CardUpgradeRuneBase<T>` 的源码绑定和 `[gold]` 名称引用，区分自创标题与普通强调词；没有高亮的普通句子里的原版名称不在检查范围内，需人工核对。补充官方名称时从原版本地化取证更新快照；不得把错写的原版名称加到普通强调词豁免中。术语依据见 [自创术语](custom-terms.md)；原版中文名以 `tools/official_zhs_titles.json` 为准，不要在文档里另维护一份。

## 定向验证

玩家侧活力火花的施加入口与清理规则见 [设计裁决 · 玩家符文](design-decisions.md#玩家符文)；使用 `PowerCmd.Apply<HextechVitalSparkPower>`，不要直接将原版敌方增益施加到玩家。

```bash
python3 HextechRunes/tools/hextech_dev.py tests --list --match Hopper
python3 HextechRunes/tools/hextech_dev.py tests --target 0.111.0 --name HopperEscapeSurvivesTheNextNativeMoveRoll
# 加 --run 才构建该目标并运行所选测试，可重复 --name
python3 HextechRunes/tools/hextech_dev.py tests --target 0.111.0 --name HopperEscapeSurvivesTheNextNativeMoveRoll --run
```

执行前校验维护目标和精确测试名，避免名称拼错变成“运行 0 项也成功”；构建失败后不运行残留 DLL。`--run` 实际调用 `bash tools/run_tests.sh --target <T> <名称…>`。目标从仓库根 `Directory.Build.targets` 读取，名称从测试目录里标了 `[HextechTest]` 的方法读取（与测试程序的收集规则一致）。测试项目仍会编译其工程依赖，但只执行指定案例，不构建 loader、不部署、不跑内容/发行检查。命令列出的是静态注册候选，最终是否可用由对应版本的测试程序判定。

新增测试：在 `tests/HextechRunes.Tests/` 任一 `partial class Program` 文件里写无参 `private static void 名称()` 并标 `[HextechTest]`，不需要在 Program.cs 注册。测试程序用反射收集这些方法、按名称 Ordinal 排序执行，所以测试之间不得依赖执行顺序（依赖全局登记状态时只比较集合，或自己准备前置状态）；特性标在非 Program 类型上或签名不对时启动直接报错。夹具写只读自动属性统一用 `Program.Fixtures.cs` 的 `SetAutoProperty`。

不同目标共用 bin/obj，**串行执行**。测试中不要调用依赖 Godot 原生层的 API；确实需要时按已有隔离 fixture/`TestMode` 方法处理，真实界面/战斗验证仍交用户。

其它已有工具按任务使用：

| 工具 | 用途/副作用 |
| --- | --- |
| `tools/validate_hextech_content.py` | 全量内容/注册/本地化检查；不是每次小改必跑 |
| `tools/run_tests.sh` | Bash 脚本，默认全套、多目标，并构建两个包的 loader、做已有 bundle 检查；`HEXTECH_STS2_TARGET` 可限目标；`--target <T> <名称…>` 为定向模式，只构建该目标并运行所列测试 |
| 本机反编译工具（外部依赖） | 原版 API 取证；目标 `sts2.dll` 与依赖必须来自同一游戏版本，见上方环境说明 |
| `tools/multi_version/validate_variant_bundle.py` | loader/manifest/变体路径、目标、DLL 哈希校验 |
| `tools/build_and_deploy.sh` | Zsh 脚本，重建 `.build` 和 `dist`、导入、构建、打包；默认替换本机模组目录，设 `HEXTECH_DEPLOY=0` 才不部署；`HEXTECH_UPDATE_LATEST=1` 才改写 latest-version |
| `tools/package_release.py [输出绝对路径] --dist <目录>` | 只打包现有 dist，不构建、不部署；校验 bundle，再按变体清单打 ZIP；包含 loader、PCK、manifest、各变体 DLL 和必要 `compat-target.txt`，不含更新日志 TXT；成功后才替换原 ZIP |
| `tools/extract_near_death_feast_glow.gd -- <原版PCK> <输出PNG>` | 用 Godot `--headless --path tools -s <脚本绝对路径>` 运行，提取 SOUL_NEXUS 红光并写入指定 PNG；区域与来源见 [设计裁决 · 视觉](design-decisions.md#视觉) |
| `tools/mplab/run_mplab.sh` | 本机双客户端联机确定性实验：构建 `HextechMpLab` 驱动并临时放进游戏 mods 目录，主机与客户端自动跑同一局，结束后比对两边日志里的校验和分叉并移除驱动；排查联机分叉时按需使用 |
| `tools/update_latest_version.py` / 工坊上传器 | 涉及版本发布或外部写入；按用户指定范围使用，不是代码修改后的自动步骤。源码直接提交到当前仓库，不再做镜像同步 |

## 运行时共享能力

以下是已有实现的入口，使用前看具体签名和相邻调用。不要再造一组同义封装。

| 需求 | 入口 | 使用边界 |
| --- | --- | --- |
| 归属、攻击/技能判定、伤害预览 | `src/Relics/Base/HextechRelicBase.CombatHelpers.cs`、`src/Helpers/HextechCardEffectTypes.cs` | `IsOwnedAttack/IsOwnedSkill` 判定持有者的攻击/技能牌，技能按规范实例类型（`HextechCardEffectTypes`）；`IsAttackDamageForRuneEffects` 把持有者技能牌造成的伤害也算作攻击伤害；预览不得产生副作用 |
| 符文共享基类 | `src/Relics/Base/TurnScopedRelicBase.cs`、`DrawThresholdRuneBase.cs`、`src/Runes/HextechSharedCombatVictoryRune.cs` | 每回合状态只实现 `ResetTurnScopedState()`，由基类在开战/战后/持有者回合开始统一清零；每 N 张阈值与跨阈值计数（`HextechRelicBase.CountThresholdCrossings`）；单机战后共享结算继承 `HextechSharedCombatVictoryRuneBase`。SavedProperty 仍声明在各子类上 |
| 按回合号防重、每 N 回合 | `src/Combat/HextechRoundInterval.cs` | `IsDue` 按文案里的 N 判定；`TryClaimRound` 替代手写 `_lastProcRound`，仍按 RoundNumber（见设计裁决） |
| 弹幕与选敌 | `src/Runes/HextechMissileVolley.cs`、`HextechRuneTargeting.cs` | 飞弹类伤害在出牌动作内同步结算；`FirstHittableEnemy` 按 CombatId 取稳定目标 |
| 治疗系数 | `src/Api/IHextechHealingMultiplierProvider.cs`、`HextechRelicBase.IsFirstOwnedInstance` | 符文自报系数，同类只乘一次；`HextechPlayerCoefficientHelper.GetHealingMultiplier` 不再写单个符文的分支（全队效果除外） |
| 最大生命基值、数值上限、体型下限 | `src/Combat/HextechMaxHpScaling.cs`、`HextechCreatureStatLimits.cs`、`HextechPlayerBodyScaleHelper.cs` | `EnsureScaledBaseInitialized` 替代手写初始化；`StatHardCap` 替代 `999999999`；`MinCreatureBodyScale` 敌我共用 |
| 濒死狂宴失血推演 | `src/Combat/HextechNearDeathHpLoss.cs` | 玩家与敌方共用；同步前缀只记债务，力量补差在 `AfterCurrentHpChanged` 里等待执行 |
| 角色/联网上下文 | `src/Helpers/HextechPlayerContextHelper.cs` | 本地玩家判断不能控制共享战斗结算；“已连接的联机”用 `IsMultiplayerConnected()`，只判断联机类型用 `IsNetworkMultiplayerRun()` |
| 出牌/抽牌历史和宠物来源 | `src/Helpers/HextechCombatHistoryHelper.cs` | 历史计数含重放与自动打出（同原版苦无口径）；历史读取不等于自动保证跨端一致 |
| 小刀识别 | `src/Helpers/HextechKnifeHelper.cs` | 用当前项目的小刀规则，不到处另写 `card is Shiv` |
| 敌方三档数值/存活目标 | `src/EnemyHexes/HextechEnemyHexContext.cs` | `TierValue` 按该海克斯强度算；`IsManualPlayerCardPlay`、`FractionOfMaxHp`、`GetAlivePlayersByNetId`、`TryConsumeRoundInterval`/`TryConsumeOncePerRound` 是统一口径；别以幕号替代强度 |
| 敌方海克斯共享基类 | `src/EnemyHexes/DrawProgressEnemyHexBase.cs`、`AttributeBoostEnemyHexBase.cs`、`EnemyMaxHpStepMultiplier.cs` | 抽牌进度联机补记、属性增益三档、按最大生命阶梯加成；`MonsterHexCatalog` 的阈值保留字面量供 TXT 脚本读取，由测试守一致 |
| 次数与战斗追踪 | `src/Mayhem/HextechCombatProcTracker.cs`、`HextechMayhemCombatTrackingState.cs` | 挑选玩家/敌人/全局及本回合/整场范围，键一律经计次器拼接；`TryConsumeOwnerTurnProc` 与遗物基类同口径；新增字段还要接序列化和重置 |
| 确定性抽选 | `src/HextechStableRandom.cs`、`src/Helpers/HextechStableCombatSpawns.cs` | 稳定身份、排序、盐值和触发序号是调用者契约；不擅自替换原流程 RNG。随从牌/充能球的稳定候选表在 `HextechStableCombatSpawns` |
| 随机获得符文 | `src/Helpers/HextechRuneGrantHelper.cs` | 包含联机 ID 同步及奖励生命周期限制；不要自行抽一个类型再只在本机发奖 |
| 防递归执行范围 | `src/Helpers/HextechScopedDepthGuard.cs` | `RunAsync` 管理进入/退出；只约束执行流，不是保存数据或联机协议 |
| 等待下一帧 | `src/Helpers/HextechGodotAsync.cs` | 用于已有时序需求；返回 false 表示生命周期已结束；帧数不能决定伤害或随机结果 |
| 私有 API/跨版本签名 | `src/Helpers/HextechHookReflection.cs`、`src/Compat` | 优先公开 API；新反射集中用 `TryGetField/TryGetMethod/TryGetProperty/TryGetPropertySetter/TryGetNestedType`，缺失时降级并进启动摘要，不在静态字段初始化里抛异常；版本差异放 `Compat`（如 `HextechRuneApiCompat`、`HextechGameApiCompat`） |
| 软依赖探测 | `src/Compat/HextechLoadedAssemblyLookup.cs`、`HextechCatalog.IsEndlessModeLoaded` | 连“没找到”也缓存，新程序集加载时失效；不要每次扫描全部程序集 |
| 日志与模型 ID | `src/HextechLog.cs`、`src/Helpers/HextechModelIdExtensions.cs` | `HextechLog.Info/Warn/Error(tag, message)` 统一 `[HextechRunes][Tag]` 前缀，Info 默认关闭；规范 ID 用 `x.CanonicalId()` |
| 表现层兜底 | `src/Helpers/HextechPresentation.cs` | `TryRun` 包住特效、音效、节点查找；放在共享状态写入之后 |
| 卡牌持久标记 | `src/Hooks/Runes/HextechCardSavedProps.cs` | 卡牌 Props 上的整数/标记读写；键名是存档契约 |
| 图片资源/界面样式 | `src/Assets/HextechAssets.cs`、`HextechTextures.cs`、`src/UI/HextechUiTheme.cs` | 资源路径从 `HextechAssets` 的根常量拼接（校验器会解析这些常量）；复用加载与主题颜色；不硬编码另一套路径和字号 |
| 战斗视觉附件 | `src/Hooks/UI/HextechCreatureAttachedVisual.cs`（含 `HextechBehindCreatureVisual`、`HextechAuraLayer`） | 挂在生物节点上的逐帧特效共用生命周期与图层工具；挂载顺序在 `HextechCreatureVisualHost` |
| UI 偏好、手柄确认、悬浮提示 | `src/UI/HextechUiPreferences.cs`、`HextechSelectAcceptButton.cs`、`HextechHoverTipAccess.cs` | 本机偏好只从 `HextechUiPreferences` 读写并批量保存；自定义按钮用 `HextechSelectAcceptButton` 接 `ui_select` |
| 对外扩展 | `src/Api` | 保持现有公共契约；只有调用方确实需要时才增加 API。拓展包只经 `HextechRunesApi` 使用本体能力（售价修正登记、`IsHextechRelic`、`StableIndex`），不反射 internal 类型 |

## 维护这些工具

```bash
python3 HextechRunes/tools/tests/test_developer_tools.py
```

这些测试只覆盖开发工具的文件写入边界和发行包遗漏问题，不启动游戏或执行全部 C# 回归。修改只读查询时也可直接使用上面的查询示例核对。新增命令保持默认只读；有写入/构建动作需在帮助中明确，并更新本手册。
