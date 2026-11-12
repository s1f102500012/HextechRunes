# 遗留代码审计（2026-09-29）

范围：`HextechRunes/src` 全部（约 8 万行），分四片只读审查（内容层 / 补丁与界面 / 选择与状态 / 敌方海克斯与基础设施）。基线为私有 dev `b45913a4`。已在架构说明、设计裁决、开发规范、设计哲学里裁决过的保留项不重复列出。

标记：**[核实]** = 主会话已对照源码复核；其余为子代理结论，动手前需再读一遍对应代码。「需反编译」= 结论依赖原版方法是否为虚方法/是否被其他 Hook 调用，未核对。

## 一、行为与联机风险（建议优先处理）

| # | 问题 | 位置 | 说明与建议 |
| --- | --- | --- | --- |
| A1 | **缩小补丁不分场合生效** [核实] | `Hooks/Combat/HextechCombatHooks.ShrinkPower.cs:9-71` | `PowerCmd.ModifyAmount` 跳过型前缀，凡是敌人施加给玩家的缩小都改成永久，不看本局是否启用海克斯、也不看是哪个敌方海克斯。装着模组打普通局也会改原版敌人的缩小，违反设计哲学第 6 节。门控到本局启用 + 对应敌方海克斯，或在施加点换成自有 Power；保留则写进架构说明。同文件 59-60 行有无意义的中转变量。 |
| A2 | **另外两处原版修正也没门控** | `HextechCombatHooks.SlipperyFix.cs:46-131`（滑溜/奥斯提替身消耗）、`HextechArtifactCompatibilityHooks.cs:15-35`（人工制品对包围/夹击的跳过前缀） | 同上，所有对局生效且未登记为保留补丁。 |
| A3 | **遥测没有游戏内开关，默认开启** [核实] | `Telemetry/HextechTelemetry.Config.cs:24,36`、`HextechServerEndpoints.cs:5-6` | 只能手改 `telemetry_config.json`；首次运行和配置解析失败都回到"开启"；端点是明文 HTTP。与设计哲学第 7 节（默认关闭或首次明示、游戏内开关、HTTPS）冲突。属于产品决策。 |
| A4 | **社区配置接口只凭 SteamId 鉴权** | `Services/HextechFeaturedConfigs.cs:138-190`、`server/hextech-telemetry/community.js:283,341` | 上传/删除/点赞/举报只带明文 steamId，服务端直接比对。知道别人公开 SteamId 就能删他的上传、绕过限流。建议改用 Steam 会话票据服务端校验，或去掉按用户的修改操作。 |
| A5 | **"已见海克斯"记录双端不一致** [核实] | `Selection/Reroll/...Reroll.cs:105-110` vs `Sync/...Multiplayer.cs:86-87`、`Coordinator/...Selection.cs:414-415` | 同一槽位连续重随 A→B→C 时，B 只在本人客户端记为已见；其他端只记本地生成的初始候选和最终候选。该 JSON 是 SavedProperty，注释写明"参与双端比对"，而且下一幕稳定生成用已见集合做盐，分歧会逐幕扩大。重随回调来自界面。建议重随结果随载荷同步、各端按载荷记录，或各端只记最终候选。游戏在什么时点比对这份数据未实机确认。 |
| A6 | **本机运行时过滤进入联机共享候选池** | `Pool/HextechRunePoolBuilder.cs:24,325`、`Compat/HextechRuntimeRuneCompatibility.cs` | 某台机器（如安卓）钩子装失败的符文只在那台被滤掉，而"是否无候选"和影子生成要求各端一致。只在本地玩家自己的界面过滤，或把结果同步。`HextechRuneGrantHelper.BuildObtainableRunePool` 反而漏了这个过滤。 |
| A7 | **界面刷新写共享状态** | `Hooks/RunLifecycle/HextechRunLifecycleHooks.LoadedUi.cs:200-229`、`...Core.cs:73-76` | 顶栏初始化和逐帧刷新会调 `ActivateExtraStage`（持久化写入）和 `TryRecoverResolvedActsFromPlayerRelics`，时机取决于各端界面就绪。界面刷新改只读，恢复逻辑留在对称钩子里。 |
| A8 | **读档后补发锻造器是后台任务、等界面就绪** | `HextechRunLifecycleHooks.LoadedUi.cs:22,40-110` | `_ = TaskHelper.RunSafely(...)` 遍历所有玩家的待发锻造器并写 SavedProperty，时机按帧。确认 `TryObtainRandomForges` 是同步选择，否则只由本人走同步路径。 |
| A9 | **三个飞弹符文单人模式伤害不等待** [核实] | `Runes/TwinFlamesRune.cs:47-72`、`MagicMissileRune.cs:63-75`、`LightEmUpRune.cs:85-98` | 联机在出牌动作内等待结算，单人则 `_ = RunSafely(...)` 延后结算、另开选择上下文，双胞胎火焰连选目标时机都不同。统一走等待路径，只让特效异步；三份相同代码可提取。`NearDeathFeastRune.cs:142,181` 同类（力量同步脱离伤害链）。 |
| A10 | **卡卡第 2 回合的仪式会重复给** [核实] | `Runes/KakaRune.cs:32` | 只看 `RoundNumber == 2`，持有者第 2 回合拿到额外回合会再给一次。加本场标记或 `_lastProcRound`。 |
| A11 | **女王、神圣干预的回合间隔与口径不一致** [核实] | `EnemyHexes/QueenEnemyHex.cs:17`、`DivineInterventionEnemyHex.cs:9` | 已定口径"每过 N 回合 = RoundNumber % (N+1)"。这两个描述写"每过 2/4 回合"，代码传 1/3，实际每 2/4 回合触发。改代码（变弱）还是改文案需要拍板。黏液史莱姆、拉加维林女族长符合口径，不是问题。冰霜幽魂、史莱姆狂战士自己算间隔、没走统一入口。 |
| A12 | **大号匕首补丁整体吞异常，还捎带敌方逻辑** | `Runes/BigKnifeRune.cs:128-180` | `AddGeneratedCardsToCombat` 前缀整段 `catch (Exception)`，一端失败就只有那端生成的牌不同。里面还跑敌方"操纵现实"的状态牌翻倍；补丁挂在大号匕首名下，失败时只禁用大号匕首，敌方效果静默失效。拆成独立补丁，catch 收窄。 |
| A13 | **缺少 Modifier 时读本地菜单配置** | `Forges/HextechForgeGrantHelper.cs:400-450`、`HextechForgeShopPriceHelper.cs:28-50`、`Coordinator/HextechForgeSelectionCoordinator.cs:228-244` | 与"联机缺本局快照时不用本地菜单值"冲突；五处各自决定缺失时怎么办。统一成一个解析入口，联机时拒绝而不是读本地。 |
| A14 | **`Hook.*` 分发方法上挂了前缀** [核实] | `Hooks/Shop/HextechShopForgeHooks.cs:340-376` | `CoreHook.ModifyMerchantPrice` 前缀改参数、`ShouldRefillMerchantEntry` 跳过型前缀，违反设计哲学第 2 节，且不在保留清单里。改为 Modifier 或商店遗物覆写对应虚方法（需反编译）。同文件界面前缀还往商店模型里再加一次条目（第二个写入者）。 |
| A15 | **原版拷贝守卫对异步目标基本失明** | `Patching/HextechVanillaCopyGuard.cs:27-44,81-94` | 只哈希被补丁方法本身；异步方法只是启动桩，状态机 `MoveNext` 变了查不出来。冻结表里 8 行 `MoveNext` 运行时从不校验。保留跳过型前缀的依据因此很弱。 |
| A16 | **全局兜底吞第三方异常** | `Hooks/Compat/HextechGameOverCompatibilityHooks.cs:72-100`（吞任意 `InvalidCastException` 伪造分数行）、`HextechCombatHooks.AttackCommand.cs:16-26`、`Hooks/UI/HextechUiSafetyHooks.cs:241-290`（所有对局跳过原版意图/出牌队列方法并改私有委托） | 违反设计哲学第 2 节"兜底只能限定到本模组能证明是自己造成的对象"。 |
| A17 | 联机"每回合 N 次"上限单人/联机不同 | `Relics/Base/HextechRelicBase.TurnProc.cs:49-66` vs `Mayhem/HextechMayhem.TurnStart.cs` | 单人按回合号重置（额外回合不重置），联机计数在队友额外回合开始时整体清空。需要决定口径。 |
| A18 | 其他小的确定性问题 | 速度恶魔/感受燃烧/精灵魔法按 HashSet 顺序逐个 await；化学科技龙魂药水盐只用回合号；多人缩放兼容只在一端改敌人血量并触发钩子（`HextechMultiplayerScalingCompat.cs:50-98`，需核实）；玩家属性悬浮固定取 `Players[0]`（联机客户端显示房主数据）；Replay 模式在不同调用点被当成单人或联机（后者会抛异常）。 |

## 二、死代码（删除成本低）

- Doormaker 延迟开局海克斯整条链：`Mayhem/HextechBossPhaseHexes.cs:46-89` 两个判定恒为 false，连同包装和 5 个调用点（每个阵营回合开始都调）都是空转；`DoormakerRealStartApplied` 字段留着保存形状。
- `RunGroupedPlayerDebuffBurst` [核实]：唯一读取方 `ShouldSuppressMonsterDebuffDuplicate` 没有调用方，9 个敌方海克斯套着的包装不起作用。先查历史确认去重是否本该生效，再决定接回还是删除。
- 战斗追踪里不再读写的字段仍被序列化：`EscapePlanPending`、`RepulsorPending`、`GetExcitedPending`、`EnemyProtectiveVeilTurnCounter`（只自增不读）。
- 从未实现却仍分发的敌方钩子：`ShouldEtherealTrigger`、`ShouldAllowSelectingMoreCardRewards`。
- 选择协调器：`SelectRune` 的联机半段（只在单人分支被调）、`SelectRuneWithLocalScreen`、`WaitForRunChangeOrMultiplayerDisconnectAsync`、`BuildRuneTagWeights` 包装。
- Mayhem 状态层：一批 `Initialize…ForNewRun`、`GetMonsterHexForAct` 系列、空的 `ResetForEndlessLoop`、恒等函数 `LastActIndexFor`。
- 配置：7 个从未调用的读写接口、两个只会被置空的旧权重字段（分享码里还带 `w1/w2`）。
- 同步协议：旧版序号格式、配置快照 -5~-8 版本、`Count < 9` 的幕掷骰载荷——SavedProperty 布局本来就阻止跨版本联机，这些分支到不了。
- 其余：锻造器随机发放旧重载与 `RollForgeRarity`、`CreateRandomOptionTransformation`、`DeckContains<TCard>`、`HextechStableRandom.PlayerCombatRoundIndex`、`ModEntry.HandleHextechActStarted`、`RequireModifyAmountMethod`、`IsOfficialEndpoint`、`CanContainOrbModels`、`HasAny`、`IsHandlingGoliathMaxHp`、悲惨命运的空 `BeforeTurnEnd`、自然即是治愈到不了的联机分支、检视界面三个没用的反射句柄、薄暮法衣旧存档兼容（`HextechPersonalHiveSafetyHooks`）、`tools/diff_monster_hex_txt.py`。
- 仅测试使用、测的不是生产路径：`PiercingThreadRune.CalculateBlockableDamage`、`HextechGoldenRerollRules.ShouldActivateForRoll`。
- `_stormLightningAtCardStart` 战斗结束不清，打断出牌会泄漏卡牌引用；风暴升级的算法整个放在 Modifier 里，符文文件只剩一个恒为 true 的判断。

## 三、重复实现与多处手工同步（中等维护成本）

- **单人/联机候选生成两套实现已经分叉**：已见回退的触发条件、候选不足 3 个时是否补、重随回退会清空调用方的已见集合、混沌替换的阶段参数、两种盐构造、只有联机排序。合成一个候选池解析 + 一个抽取循环（随机源用委托），盐字符串保持不变。
- 候选稀有度两种算法：`GetRarityForOptions` 线性扫描、未知一律金色；界面和恢复用注册表。`SelectionExcluded` 符文界面显示棱彩、重随按金色。
- 选择载荷在三处用硬编码偏移解析；敌方调整选项在三处各拼一遍。
- 运行配置双份存储：单独的 SavedProperty 和快照 JSON 都存次数与禁用列表，读档结果取决于属性恢复顺序；幕掷骰载荷也发两遍。
- 混沌海克斯概率 `33` 散在四处，唯一没用蛇形 JSON 键名的字段。
- 16 个锻造器重复"开局给自己施加 Power"；5 个关键词持久化追踪器（思绪覆写/谢幕/Cosplay/腐化树枝/不死虚无）连同快照与序列化各复制 5 份；5 套生物视觉附件脚手架各自逐帧轮询、对所有生物生效且不看模组是否启用；抽牌进度三兄弟（夜狩/战甲精魂/迅捷守护）语义已不同；`_lastProcRound` 防重在 8 个符文里各写一遍；逃生计划/斥力/黎明之决心结构相同；两套"按伤害命令的待处理登记表"。
- 六个敌方海克斯手写了 `TierValue` 已有的分档；玩家人数夹取写了 7 种；41 个敌方海克斯文件重复"同一局、玩家拥有"的判定。
- 手工同步的元数据表：敌方图标载体遗物清单（注释承认"必须同步登记"）、敌方默认禁用和联机禁用列表、预设挑战与稀有度 Modifier 类型（四处）、人数缩放表（含已失效的 ShrinkEngine 行，只有两行有测试）、敌方悬浮提示在表外还有一串 if、图标别名在 C# 和校验脚本里各写一份。`MonsterHexKind` 编号没有快照测试，注释漏了空洞 33。
- DynamicVar 显示值与逻辑里的字面量是两份：逃生计划 50/60、黎明之决心 50、迅捷守护 10、奥术重拳 2、全心为你 25；另有 4 个声明了却没人读的变量。
- 配置菜单：控件与样式工厂散在几个文件；步进按钮漏了手柄焦点样式；两个百分比设置段复制粘贴，混沌那段用的是金色重随的夹取函数；`CreateOverlay` 242 行，分享动作靠数组下标晚绑定。

## 四、版本兼容与反射

- `STS2_108/109/110_OR_NEWER` 在现有三个目标里总是同时定义，三种拼写表达同一个分支；`OkBoomerangCard` 的 `#elif STS2_108_OR_NEWER` 到不了；保存引导的 Legacy/Official 分部用的符号不配对。收成一个符号。
- 共享代码里的行内 `#if`（规则只允许覆写签名与补丁目标）：致命疾病、潘多拉魔盒、玩家系数、怪物交互策略、联机诊断、色彩发现奖励、手柄覆盖层、遭遇兼容、形态特效、官方缓存审计（两处重复）。
- 私有反射绕过 `HextechHookReflection`、没有"原版字段 + 版本"注释、静态初始化里直接抛异常：自动化升级、复视 `_wasGoldStolenBack`（缺失时会把被偷回的金币也翻倍）、坚实时刻、濒死狂宴自带的 DamageResult 写入器、卡牌网格预览、界面安全补丁、万剑特效、灼烧血条、炼金师、资源图标、幻影武器、宝箱符文、属性悬浮；SavedProperty 的 `_netIdToPropertyNameMap` 在 4 处各反射一次，0.107.1 的 NetIdBitSize 有两个写入者；模型池登记反射 `ModHelper._moddedContentForPools` 无版本注释。
- 0.107.1 专用的 net-id 表重排补丁（`HextechSavedPropertyNetIdHooks.cs`）属于设计哲学第 2 节禁止的中枢，未登记为保留；它的动态应用吞掉自己的失败，补丁器照样记成成功。

## 五、补丁基础设施

- 补丁器把 `[HarmonyPrepare]` 返回 false 和内部吞错的动态补丁都记为"已应用"，失败汇总走默认静默的 Info 日志。
- 目标固定却用动态 `Apply` 的（遗物可见性 12 个、敌方 Power 缩放 7 个，后者跳过型前缀还是 `Priority.First`），补丁清单只记到 `dynamic(...)`，目标与优先级没被冻结。
- 界面偏好只在某个补丁的 `Apply` 里顺带加载，那个补丁没装上时所有依赖的界面偏好静默回默认。
- 补丁元数据张冠李戴：濒死狂宴补丁也承载敌方濒死；`reward.card-select` 挂着复视的名字却是禁忌魔典逻辑；形态特效兼容里有战争交响曲逻辑；文档说复视 8 个补丁，实际 11 个。
- 未登记为保留的跳过型前缀：卡牌奖励替代项、Power 按层数定类型、最大生命变化 0 的提前返回、锻造器叠加、敌方海克斯容器焦点、充能球布局软上限。
- 静态状态快照测试跳过了 `static readonly` 的可变集合，漏了出牌费用 4 个以卡牌为键的字典、滑溜、疫病、生物节点登记表（跑完不清，持有上一场的生物）等。
- 开始新局在原方法前重置静态状态，读档却在原方法之后；重置清单在三处各写一份、内容不同。

## 六、结构与注释（低）

- `Hooks/` 下约 9k 行不是补丁（配置菜单全套、战斗特效、各种视觉附件）；符文专属辅助还留在 `Hooks/Runes/`，而它们的补丁已搬进符文文件。`HextechCombatHooks` 是横跨 15 个无关功能的大 partial，共享私有静态。
- 放错地方：`IllusoryWeaponRune.IsAttackForEffects` 实际是全局攻击/技能分类器（16 个文件在用）；`HextechGoldrendSync` 是敌方逻辑却在 `Runes/`；龙魂 Power 在 `Cards/`；`RandomForgeShopRelic` 藏在发放辅助文件里；`HextechModelBaseCompat.cs` 实际装的是 Power/Modifier 基类；敌方专属常量停在 Modifier 上。
- `Selection/Pool/HextechRunePoolBuilder.cs:84-85` 注释里有韩文"룬"（全仓唯一一处），同段"空选项会崩溃"已过时。
- 历史叙述注释：五个形态升级符文的"0.8.4 重做：回合结束时手牌有形态则自动打出"与现行为（开局自动打出所有副本）矛盾；另有十余处"0.8.x 起/PR#18/此前/Bug2/已在 0.108 实机验证"。
- 失效注释：双持意图补丁引用不存在的方法；灼烧血条有悬空的 summary；视觉宿主注释说"加一行即可"但顺序另有一份；设计裁决"手柄"一节说折叠按钮手柄够不着，代码已接入焦点链。
- 界面：几处 Godot `Label`/`TooltipText` 显示中文（应走 MegaLabel）；社区面板无条件抢焦点；图鉴分组标题中英文硬编码在 C#；更新检查提示是硬编码中文；三个独立 `HttpClient`；遥测待发队列读-发-写跨两个锁，可能重复或丢行。

## 七、需要拍板的

1. 遥测（A3）：加游戏内开关并默认关闭或首次明示、解析失败视为关闭、换 HTTPS。
2. 缩小/滑溜/人工制品这三处原版修正（A1/A2）：门控到本局启用，还是确认本来就要全局生效。
3. 女王、神圣干预（A11）：改代码还是改文案。
4. Replay 模式是否要支持；联机"每回合 N 次"上限的口径（A17）。
5. 社区配置鉴权（A4）涉及服务端改动。

## 八、处理进度（2026-09-29 同日）

裁决：遥测保持现状；三处原版修正门控到本局启用；"每 N 回合"改为第 N、2N 回合（黏液史莱姆/拉加维林女族长保持文案、实际变频繁，冰霜幽魂我方改 2/4/6，中文统一"每 N 回合"）；回放按单人；每回合次数按持有者自己的回合计；社区鉴权接受现状。

已处理：A1、A2、A5（联机已见只记初始与最终候选）、A9（三个飞弹符文统一在出牌动作内结算；濒死狂宴未动）、A10、A11、A15（守卫校验异步 `MoveNext`，三个版本的冻结表按 0.111.0 目标清单补齐）、A17、A18 中的回放判定；第二节死代码除以下保留项外均已删除。

有意保留：`DeckContains<TCard>`、`HextechPowerCmdCompat.RequireModifyAmountMethod`（公开类，硬依赖模组可能使用）；旧权重字段 `FirstActRuneRarityWeights` 等（牵涉配置、分享码、本局快照三种 JSON 形状）；自然即是治愈的联机分支（兜底联机禁用前已持有的旧存档）；薄暮法衣旧存档兼容；同步协议的旧版本分支；仅测试使用的两个方法。

未处理：A3、A4（按裁决）、A6、A7、A8、A12～A14、A16，第三至六节的重复实现、元数据手工同步、版本符号、反射集中、补丁基础设施与结构调整。

### 2026-11-10 复核

仍未处理（位置按复核时的代码）：

- A6：运行时过滤仍进入候选池，`Selection/Pool/HextechRunePoolBuilder.cs:30,418`（`IsPlayerRuneAvailableForCurrentRuntime`）。
- A7：顶栏延迟刷新仍会调 `TryRecoverResolvedActsFromPlayerRelics`，`Hooks/RunLifecycle/HextechRunLifecycleHooks.LoadedUi.cs:153-201`。
- A12：大号匕首 `AddGeneratedCardsToCombat` 前缀仍整段吞异常并捎带敌方操纵现实，`Runes/BigKnifeRune.cs:126-178`。
- A13：缺 Modifier 时仍读本地配置的只剩 `Forges/HextechForgeGrantHelper.cs`（稀有度权重、禁用列表）与 `Forges/HextechForgeShopPriceHelper.cs`（售价）；锻造选择协调器已改为联机不读本地值。
- 运行配置双份存储：`Mayhem/HextechMayhem.SavedState.cs` 的配置快照 JSON 与玩家符文禁用列表 JSON 仍各存一份。
- 补丁器仍把 `[HarmonyPrepare]` 返回 false 的补丁记为已应用：`Patching/HextechPatcher.cs:61-70`。
- 补丁元数据张冠李戴：濒死狂宴补丁仍承载敌方濒死，`reward.card-select` 仍挂“复视奖励事务”却含禁忌魔典逻辑。
- 静态状态快照测试仍跳过 `static readonly` 字段（`StaticStateManifestTests.cs` 的 `IsInitOnly` 判断），readonly 可变集合不在清单内。

已解决：

- A8：读档补发锻造器走选择协议同步，按 `SavedInitialForgeGrantCompletedCount` 只发剩余数量，幂等。
- A14：`ShouldRefillMerchantEntry` 跳过前缀已删除，改为条目级 `ClearAfterPurchase` 前缀；`ModifyMerchantPrice` 非跳过前缀已裁决保留并登记在架构说明。
- A16：游戏结算兜底只接 `NScoreLine.Create` 自身的 `InvalidCastException`，UI 安全补丁只在节点失效时跳过原版。
- 单人/联机候选生成合并为同一抽取流程，只差随机源（`HextechRunePoolBuilder`）。
- 选择载荷解析集中到 `Selection/Sync/HextechChoiceCodec*.cs`。
- 混沌概率常量集中为 `HextechRuneConfiguration.DefaultChaosRuneChancePercent`。
- 锻造器开局施加 Power 收进 `HextechForgeBase` 的共用方法。
- 关键词持久化追踪器收进 `Hooks/Runes/KeywordPersistenceTracker.cs`。
- 濒死狂宴的结算不再脱离伤害链（A9 遗留项）。
- 玩家属性悬浮取本机玩家（`LocalContext.GetMe`，`Hooks/UI/HextechPlayerStatsHoverHooks.cs`）。
- `MonsterHexKind` 编号由 `monster_hex_kind_manifest.txt`（`MonsterHexKindManifestTests`）守护。
