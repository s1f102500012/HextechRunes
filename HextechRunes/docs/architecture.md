# HextechRunes 架构重构路线

本文件记录已采用的分层与后续重构方向，不是要求立即执行的迁移清单。日常维护先读 [开发规范](development.md) 和 [工具手册](developer-tools.md)；结构重构不改变游戏行为、模型 ID、随机算法或联机协议语义。

## 依赖方向

长期目标是让业务逻辑沿单向依赖流动：

```text
Platform/Hooks/Config/Localization/Telemetry
  -> Mayhem
  -> Selection
  -> Core/Catalog/Runes/EnemyHexes
```

多人同步和随机数属于横切能力，所有会影响联机一致性的选择结果都必须通过明确 payload 或稳定随机输入表达，不允许依赖“各端本地池子刚好一样”。

## 补丁层（2026-09 重构后）

所有 Harmony 补丁都是自描述的嵌套静态类，入口 `ModEntry.Initialize` 只做编排，不再有安装顺序契约：

- 声明：`[HarmonyPatch(...)]` + `[HextechPatch(id, feature, Rune=/Runes=/Optional=/CopiesVanillaLogic=)]`，由 `src/Patching/HextechPatcher.ApplyAll` 统一应用，逐条成败可见；`Optional=true` 的目标缺失只记 Info。只带 `[HarmonyPatch]` 却缺 `[HextechPatch]` 的类照常应用但启动时 Warn。目标需要运行时枚举时，类只带 `[HextechPatch]` 并声明 `static void Apply(Harmony)`；找不到目标或安装失败必须抛异常交给 Patcher 归因，不能自己吞掉后返回（否则启动摘要误报成功）。
- 同一目标的执行序只由 `[HarmonyPriority]` / `[HarmonyAfter]` 决定，禁止依赖安装顺序。
- 符文专属补丁内嵌在符文自己的文件里（如 `SurvivorUpgradeRune` 里的 `SurvivorPatch`）；`src/Hooks/**` 只放跨符文的横切补丁（战斗、UI、资源、商店、运行生命周期）与共享辅助。
- 版本差异写成整文件 `#if` 的分部文件（`HextechSavedPropertyBootstrap.Legacy.cs` / `.Official.cs`、`HextechCreatureVisualsCompat.V110.cs` / `.V111.cs`），共享代码里不写 `#if`；剩余的行内 `#if` 只允许出现在原版虚方法签名随版本变化的覆写处，以及 `[HarmonyPatch]` 目标签名随版本变化的特性声明。预处理指令顶格写。
- AsyncLocal 作用域补丁（Prefix 入栈）必须同时有 Postfix 与 Finalizer：Postfix 同步出栈并把 `ref __state` 清零，Finalizer 只在异常且 `__state` 仍持有作用域时出栈，异步续体只清账。
- 私有成员访问集中经 `HextechHookReflection`，缺失成员会在启动摘要里列出。

三道护栏（`tests/HextechRunes.Tests/`，三编译目标各一份，用 `HEXTECH_WRITE_PATCH_MANIFEST=1` 重生成）：`patch_manifest.<target>.txt` 冻结补丁目标与优先级；`static_state_manifest.<target>.txt` 冻结可变静态字段清单；`tests/vanilla_copy_guard.<target>.txt` 冻结所有可跳过原方法的 bool 前缀目标、以及声明 `CopiesVanillaLogic` 的补丁目标的 IL 哈希，异步目标连同编译器生成的 `MoveNext` 一起冻结（入口只是启动状态机的桩，原版改动几乎都落在 `MoveNext`）。目标清单来自 0.111.0 的 `HEXTECH_DUMP_PATCHES` 导出，各版本的行由测试 `VanillaCopyGuardFreezesEntriesAndAsyncBodies` 在对应 sts2.dll 上补齐；该测试只追加缺失行，漂移行只报告不刷新。游戏更新后 headless 日志出现 `[VanillaCopyGuard] DRIFT` 或测试报漂移即需对照原版复核替换逻辑。

## 当前 Selection 分层

`src/Selection` 已按职责拆分为以下目录：

- `Coordinator/`：选择流程编排。负责何时弹界面、何时等待远端、何时落地奖励。这里可以调用其它 selection 服务，但不应继续堆具体池生成和同步编解码细节。
- `Pool/`：玩家海克斯池生成、过滤、标签权重、幕数限制、配置过滤边界。`HextechRunePoolBuilder` 是当前池构建入口，Coordinator 只保留兼容门面和流程侧调用点。
- `Reroll/`：玩家海克斯重随机制。所有重随逻辑必须保持本地 UI 随机不推进共享 run RNG，联机路径要保持可重放或同步最终选项。
- `Sync/`：多人选择 payload、远端等待、选择确认、ack 等同步边界。只负责“传什么、如何还原”，不负责具体 UI。
- `EnemyAdjust/`：选择界面里的敌方海克斯重随/移除同步。
- `AI/`：AI 队友或主机代选逻辑。
- `UI/`：`HextechRuneSelectionScreen` 的渲染、交互、hover、音效、布局。

## 已裁决保留的补丁

以下补丁点在 2026-09 重构中逐条评估过，结论是**保留**，理由已核实，不要再翻案；要改先拿出新的原版证据。

- `CardPileCmd.Draw` 前缀：卡牌检视是"用选牌界面替换抽牌返回值"，`ShouldDraw` / `ModifyHandDraw` / `BeforeHandDraw` 都表达不了，且改走 Hook 会把 PlayerChoice 挪到不同的同步点。
- `RunManager.OnEnded` 前缀 + 后缀：前缀必须在 `ToSave` 之前补战斗历史（原版败北存档缺房间记录），`OnMetricsUpload` 只在 `ShouldSave` 且首次上报时触发，替不了。
- `NGame.StartRun` / `LoadRun`：需要包住原版 UI 任务链再做延续，`RunManager.RunStarted` 只在模型层触发。
- 剩余的资源图标补丁组：理论上可整组删除，但纹理加载多轮返工过，headless 验证不了视觉，必须真机看过遗物栏/图鉴/检视/悬浮四处再删。
- 复视奖励事务 8 个补丁：改用 `AfterRewardTaken` 需要重新设计"同一事务只复制一次"的幂等键，属于重做而非迁移。
- 濒死狂宴的 `GainBlock` 跳过 **不**改成 `ModifyBlock=0`：原版对 0 格挡仍播音效/特效并触发 Before/AfterBlockGained。同理治疗管线的"禁止回血"也必须继续跳过原方法（`CreatureCmd.Heal` 的 amount 为 0 也会播演出），只给封顶前缀补 `[HarmonyAfter]` RitsuLib / BaseLib。
- 星尘的 `SpendResources` 跳过 **不**改成 `ModifyStarCost=0`：那会让牌在没有星星时也能打出，是语义变化。
- 遗忘 / 腐蚀波 / 主宰等升级符文的 Power 前缀：只对"持有符文的玩家自己的 Power 实例"生效，官方 Hook 无法表达"按实例替换回调"。
- 敌方海克斯的联机缩放前缀：已用 AsyncLocal 限定在本模组自己的 `PowerCmd.Apply` 窗口内，不必子类化。
- 形态自动打出的代表牌必须继续走原版出牌管线（`CardCmd.AutoPlay`）：那里才有附魔、流电、克隆语义；改成直接施加 Power 会全部丢失。

### 跳过型前缀与原版拷贝登记（2026-09 Hooks 审查）

以下补丁都会替换或复制一段原版逻辑。共同约束：只在本模组内容/本局启用海克斯的条件下生效，其余情况 `return true` 走原版；默认 `Priority.Low`；目标 IL 进原版拷贝守卫，游戏更新报 DRIFT 时按代码注释对照原版复核。

- 商店随机锻造器（`Shop/HextechShopForgeHooks.cs`）：`MerchantRelicEntry.OnTryPurchase` 跳过前缀——原版购买直接 `RelicCmd.Obtain` 条目模型，没有"改发别的遗物"的 Hook，替换体按原版扣款/购买历史/同步顺序改为选锻造器后发放；`RestockAfterPurchase` 与 `ClearAfterPurchase` 跳过前缀——随机锻造器条目购买后常驻，分别覆盖 `Hook.ShouldRefillMerchantEntry` 为真/为假两条路径；`NMerchantRelic.OnSuccessfulPurchase` 跳过前缀——占位图标不能播放"飞入遗物栏并清空槽位"的购买动画（纯本地表现）。
- `Hook.ModifyMerchantPrice` 非跳过前缀（`shop.random-forge.price`）**已裁决保留**：设计哲学禁止给 `Hook.*` 分发打补丁，但此处没有等价虚方法。分发迭代顺序是牌组 → 遗物/药水 → Modifier → 模组订阅者，会员卡、信使等折扣遗物排在 Modifier 之前，由 `HextechMayhemModifier` 覆写 `ModifyMerchantPrice` 只能改折后价；条目模型 `RandomForgeShopRelic` 从未被获得，不在监听列表里；基准价 `_cost` 由 `CalcCost` 按商店 RNG 浮动生成，改它要么改变 RNG 消耗，要么反射写保护字段并失去随配置实时刷新。前缀不跳过、不改其他条目，只把本模组条目的输入基准价换成本局设定价。原 `Hook.ShouldRefillMerchantEntry` 跳过前缀已删除，改为上面的 `ClearAfterPurchase` 条目级前缀。
- 锻造器叠层 `RelicCmd.Obtain`（`Shop/HextechForgeStackingHooks.cs`）：已持有同名锻造器时改为给已有实例叠层，原版获得流程没有"合并到已有遗物"的 Hook；保留原版的遗物选择历史与"已见"记录。
- 卡牌奖励备选项 `CardRewardAlternative.Generate`（`Compat/HextechRewardSafetyHooks.cs`）：原版在 `Hook.ModifyCardRewardAlternatives` 之后对超过 2 个备选项直接抛异常，多次浮木重掷与多份佩尔之翼献祭会越界；替换体逐步复制原版，只去掉数量上限并合并重复献祭项。
- 充能球布局 `NOrbManager.TweenLayout`（`Runes/HextechPlayerRuneHooks.Orbs.cs`）：原版半径按 `Mathf.Lerp(225, 300, (槽位-3)/7)` 外插，超过 10 槽继续放大；只在超过软上限时替换为半径封顶的同一环形布局，纯本地表现层。
- 缩小 `PowerCmd.ModifyAmount`（`Combat/HextechCombatHooks.ShrinkPower.cs`）：原版缩小正层数为临时、负层数为永久，敌人对已有临时缩小的玩家施加永久缩小时层数直接相加、永久效果被抵掉；只在本局启用海克斯、玩家无人工制品、施加者为敌人时改为移除临时实例并施加等量永久缩小。
- 人工制品 `ArtifactPower.TryModifyPowerAmountReceived`（`Compat/HextechArtifactCompatibilityHooks.cs`）：判定没有可挂的 Hook。三种情况直接给出结果：包围/夹击等遭遇战机制能力不挡；已有正层数临时缩小的到期递减（无外部施加者）不挡——原版缩小允许负层数，`GetTypeForAmount(-1)` 把递减判成负面效果，临时缩小永不结束且每回合吃一层人工制品；本模组隐藏的临时力量/敏捷丢失按可见负面效果整体抵挡——原版只挡可见能力，会放行外壳、只挡住其中的负力量，回合末外壳回收后目标凭空多出永久属性。前两项只在本局启用海克斯时生效，第三项只涉及本模组能力。
- 缓慢 `PowerModel.GetTypeForAmount`（`Combat/HextechCombatHooks.SlowPower.cs`）：非虚方法，原版把 Counter+AllowNegative 的负层数判为 Debuff；只对本模组两种缓慢能力返回 None。
- 形态批次 `PlayerCmd.EndTurn`（`Combat/HextechFormAutoPlayHooks.cs`）：虚空形态 OnPlay 自带结束回合，开局批量自动打出会吃掉首回合；只在本模组形态批次的 AsyncLocal 作用域内跳过。
- 形态特效容器 `NCreatureVisuals.AddFormVfx/RemoveFormVfx`（`Compat/HextechFormVfxSafetyHooks.Official.cs`，0.110+）：自定义角色缺 `%FormVfx` 容器时原版空引用；优先级已从 `Priority.First` 改为默认 `Priority.Low`（没有必须抢先执行的理由）。
- 珠光护手 `MonsterModel.PerformMove` Postfix（`Combat/HextechCombatHooks.JeweledGauntlet.cs`）：不跳过原方法，但在原版行动完成后按原版步骤再执行一次行动（刻意不推进 `MoveStateMachine`）；声明 `CopiesVanillaLogic = true` 纳入原版拷贝守卫。
- 敌方能力联机缩放 `GetScaledAmountForMultiplayer` 跳过前缀（`combat.enemy-power-scaling`）**保留 `Priority.First`**：只在本模组 `HextechEnemyPowerScalingHooks.Apply/ApplyExact` 的 AsyncLocal 窗口内、目标为敌人时生效，窗口外恒 `return true` 对其他模组透明；窗口内层数已按最终口径算好，排在第三方缩放前缀之后会被重复缩放或被其跳过。亮出你的剑的 `OrbModel.Evoke` 跳过前缀（`Runes/DrawYourSwordRune.cs`）已改为 `Priority.Low`：本模组没有其他 Evoke 前缀，别的模组若已跳过原方法就让给对方。

- 符文专属跳过前缀（均只对持有者生效、`Priority.Low`、进原版拷贝守卫）：科学狂人 `OrbCmd.AddSlots`（上限 10 写死，没有 Hook）；升级循环 `LoopPower.AfterPlayerTurnStart`（原版只触发 `Orbs[0]`；替换体未保留原版每次触发后的 0.25 秒等待，已在注释写明）；升级狂怒/升级遗忘 `RagePower/OblivionPower.AfterSideTurnEnd`（`PowerCmd.Remove` 不经过任何 Hook）；升级倒映 `ReflectPower.AfterSideTurnStart`（`Decrement` 虽经过 `ModifyPowerAmountReceived`，但区分不出衰减来源且会写历史）。
- UI 跳过型前缀（均 `Priority.Low`，三个版本原方法一致）：`NRelicBasicHolder.OnFocus/OnUnfocus`——原版固定展示图标遗物自身提示，没有 Hook 能换成敌方海克斯提示，只对本模组 `EnemyHex-` 图标节点生效；`NInspectRelicScreen.UpdateRelicDisplay`——原版按已解锁/已见硬分三支，海克斯隐藏遗物会落进“未解锁”，只对海克斯遗物复刻“正常”分支；`NRelicInventoryHolder.PlayNewlyAcquiredAnimation`——持有者离开场景树时原版空引用且无取消入口，只对海克斯遗物跳过或吞异常；`NMultiplayerPlayerIntentHandler.BeforeActionReadyToResumeAfterPlayerChoice` / `NCardPlayQueue.BeforeRemoteCardPlayResumedAfterPlayerChoice`——卡牌节点脱树时原版 Reparent 抛异常打断联机恢复，只在节点失效时跳过；`NRelicInventoryHolder.DoFlash`——闪光挂在顶栏特效层，隐藏持有者挡不住，只在玩家勾选隐藏遗物时跳过；`NHealthBar.RefreshForeground`——原版只认中毒/灾厄两段前景，有灼烧预测时整段替换。
- 纯表现补丁（飞踢尸体击飞、夺金音效）不设 `Rune =`：补丁失败时不把玩法符文标为不可用。

### SavedProperty net-id 规范化（仅 0.107.1）

`Hooks/Compat/HextechSavedPropertyNetIdHooks.cs` 在 `OneTimeInitialization.ExecuteEssential` 后缀里把 `SavedPropertiesTypeCache` 的 net-id 表按"原版前缀 + 模组条目确定性排序"重排，并重写位宽。设计哲学第 2 节原则上禁止重排 net-id 表；**裁决：0.107.1 变体保留，0.109+ 不存在**。原因：0.107.1 的 net-id 按注册先后分配，本模组与 RitsuLib 等模组在 ModInitializer 与 LocManager 两个阶段注入 SavedProperty 载体，两端加载顺序不同就会得到错位的 net-id，序列化抛异常导致联机失败；0.109 起游戏自己的 `ModelIdSerializationCache.Init` 做确定性排序与哈希，本补丁随之只编译进 0.107.1。删除它会让 0.107.1 联机在装有其他 SavedProperty 模组时重新随机失败，且与已发布的 0.107.1 变体 net-id 布局不一致，因此不删。维护约束：只在冻结点之前注册载体（冻结后注入会告警）；规范化失败只记错误、不宣称完成（`IsCanonicalized`）。

## 共享边界（2026-09 代码审查整理）

- **加载器单源码**：`HextechRunes/loader/` 是两个包唯一的加载器源码，拓展包 loader 以链接方式编译并用 `HEXTECH_SPONSOR_LOADER` 保持命名空间；宿主版本已知却没有不高于它的变体时两个包都停止加载。变体没能加载（含 Windows 智能应用控制拦截 `0x800711C7`）时，加载器在主菜单用原版 `NErrorPopup` 弹一次说明；文字写死在加载器里，因为变体的本地化同样不可用。拓展包变体引用本体程序集，本体程序集出现前不交给游戏（`RequiredAssemblyName`），本体始终没加载时拓展包不加载并提示，本体已弹窗则不再重复。
- **拓展包只走公开 API**：售价修正（`RegisterForgeShopPriceModifier`）、归属判定（`IsHextechRelic`）、稳定哈希（`StableIndex`）都经 `HextechRunesApi`，不反射 internal 类型、不复制实现。`SponsorPatcher` 仍独立实现（公开 `HextechPatcher` 会把大量内部类型带进 API 面），约定对齐本体，由声明完整性测试守护。
- **选择同步**：锻造选择与遗物选项选择共用 `HextechSyncedRelicChoice` 事务和 `HextechChoiceCodec.RelicChoice`（消息类型 5/7，线格式不变）；远端核对候选 ID 后返回本端同位置的候选实例。远端载荷先做内容校验：符文候选必须是已登记的玩家符文；敌方调整校验槽位数、海克斯来源与每槽重掷上限。任何一项不通过都走 `CreateProtocolFailure`——协议失败的唯一出口，只记录一次。
- **外部扩展点**：`HextechRuneGeneration` 对第三方混沌变换的结果做校验（同条数、全部是已登记玩家符文），异常或不合法时回退原候选并告警；外部 API 登记本体内置的符文/锻造在任何副作用之前被拒绝。
- **敌方海克斯分发**：`AfterShuffle`、`AfterCardDrawn`、`ShouldPlay` 只对本局战斗中的玩家侧分发；`ShouldDraw` 与其余钩子在分发层不过滤，玩家侧与本局归属由各效果自己判断（不少效果里的同类判断与分发层重复，统一收口要逐个核对钩子语义，暂不做）。Power 基类的回合钩子走 `*ForParticipants` 入口，兼容桥不吞 participants；Modifier 直接覆写原版回合钩子（签名本身就带 participants）。
- **Modifier 不承载单项内容**：白洞由牌自己监听 `AfterCardDrawn`；雷暴升级的算法与按牌层数在 `StormUpgradeRune` 里，只有调用时机仍由 Modifier 分发（保证补发闪电排在所有监听者之后）。

## Source of truth 方向

内容元数据只有一个来源：

- 三张注册表（`HextechPlayerRuneRegistry`、`HextechForgeRegistry`、`HextechMonsterHexRegistry`）加外部 API 登记，经 `HextechContentRegistry` 按注册表版本派生 `PlayerRuneMetadataCatalog`、锻造器稀有度分组和 `MonsterHexMetadataCatalog`；稀有度、角色池、标签、默认禁用、是否进入图鉴、是否进入抽选池都从这里取，不另建名单。
- 类型层面的派生不调用 `ModelDb`；按 ModelId 的查表在 `HextechCatalog.ModelIdLookups` 单独缓存，两个缓存不能合并（合并会提前 ModelId 的捕获时机，RitsuLib 前缀尚未生效时会缓存错误的 ID）。
- 本地化、配置界面、统计中文名、图鉴可见性不应各自维护重复名单。

## 高风险规则

- 不在结构重构中顺手改平衡、文案或触发时机。
- 不改变模型注册顺序，除非明确重打版本并接受联机 hash 改变。
- 不改变 `PlayerChoiceResult` 的既有语义；如需扩展 payload，必须兼容旧 payload 或提供明确 fallback。
- 多人池过滤不能只同步 index；只要各客户端候选池可能不同，就必须同步最终选项 ID。
- 按工作区设计哲学第 8 节选择与改动相关的验证；目录搬移核对引用，补丁重构核对目标与优先级。构建、部署、加载和实机是不同交付层级，不要求每次重构都部署或启动游戏。
