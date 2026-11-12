# 海克斯开发规范

本文记录当前维护契约；目录结构与补丁保留清单见 [架构说明](architecture.md)，命令和共享代码见 [工具手册](developer-tools.md)，单项内容的设计裁决见 [设计裁决](design-decisions.md)，自创术语的九语言写法见 [自创术语](custom-terms.md)。用户最新明确要求优先于旧设计稿。仓库根总规则在 [模组设计哲学](../../docs/模组设计哲学.md)。

## 1. 先定效果，再选扩展点

开始实现前用几行确定：触发时机、作用对象、数值/取整、次数限制、重置时机、玩家归属。只有有歧义的项目才记录，不要求每次填长表。

- “失去生命”与“攻击造成未格挡伤害”不同；回复、直接设血、失去生命也不是同一条命令链。先确认原版 Hook 的实参含义。
- “每回合”需明确玩家侧/敌方侧、额外回合、回合开始效果之间的顺序；不能只靠在某个开始 Hook 清零。自有基类的回合钩子已按 participants 跳过没参与本次回合的持有者（队友额外回合）；直接覆写原版带 participants 的 Early/Late 钩子时要自己判断 `participants`，宠物按主人算（`HextechTurnParticipants`）。“战斗第一回合”看持有者自己的回合数 `IsOwnersFirstTurn`，不看 `RoundNumber`。
- “每个敌人每场一次”按敌人身份记录，不用全局一个布尔值；死亡、复活、分裂、换阶段、逃跑与召唤物各按原版生命周期处理。
- “免费”要核对能量、X 费与辉星，区分一次打出和整回合有效；不要把显示费用当实付费用。
- 多人场景区分效果拥有者、卡牌拥有者、伤害来源及宠物，不拿第一个玩家代替所有者。
- 兼容第三方怪物时先核对它的伤害上限、回复、复活、随从及回合契约。不要为一个内容全局取消其他模组的规则；例如限伤 Boss 与高额回血组合需要明确设计裁决，不能擅自修改数值。

实现顺序：自己的模型覆写 → 官方事件/API → Harmony postfix → 不跳过原版的 prefix → 有证据的局部替换。跳过原版必须说明缺失的 Hook、激活条件、优先级与版本守卫。不要给第三方所有子类批量打补丁。

## 2. 文件和身份

| 内容 | 放置与关联入口 |
| --- | --- |
| 我方符文 | `src/Runes/<Name>Rune.cs`，继承 `HextechRelicBase`；`src/Content/HextechPlayerRuneRegistry.cs` 注册品级、角色池、标签及开关。外部模组的注册入口与契约见 [对接指南](../INTEGRATION.md)，判断海克斯归属用注册表（`HextechCatalog.IsHextechCustomRelic`），不要只写 `is HextechRelicBase` |
| 敌方效果 | `src/EnemyHexes/<Name>EnemyHex.cs`，继承 `HextechEnemyHexEffect`；同时检查效果目录、`HextechMonsterHexRegistry`、`MonsterHexKind` 及展示用遗物 |
| 卡牌/Power | `src/Cards`、`src/Powers`；自定义模型登记见 `src/Content/HextechCustomModelRegistry.cs` |
| 选择流程 | `src/Selection/Coordinator` 编排；`Pool`/`Reroll` 生成；`Sync`/`EnemyAdjust` 通讯；`UI` 展示和输入 |
| 战斗共享状态 | `src/Mayhem` 中 Tracking、ProcTracker 和序列化分部；不要把单符文算法堆进 Modifier |
| 版本差异 | `src/Compat`，保存引导在 `src/Bootstrap`，根加载器在 `loader` |

注册表仍有多个入口，不能把“未来统一元数据”的重构目标当成已实现。先追踪同类内容的完整引用。新增枚举追加到尾部，保留旧序号（`MonsterHexKind` 由测试清单 `monster_hex_kind_manifest.txt` 守护，`HEXTECH_WRITE_PATCH_MANIFEST=1` 只追加新行，删除的编号记为 `retired=N`）；模型名称、配置标识、SavedProperty 名称和同步消息编号都是兼容契约。身份变更必须核对旧档/联机代价，不能靠更新快照掩盖。

## 3. 代码风格与共享能力

- 沿用 `namespace HextechRunes;`，Tab 缩进、Allman 花括号、UTF-8。方法/类型 PascalCase，参数/局部 camelCase，私有实例字段 `_camelCase`；优先跟随相邻实现，不全文件格式化。
- 小效果一个文件，方法名表达触发或结果；复杂流程按已有职责拆分 partial，不按版本日期堆“新增功能区域”。
- 注释解释为什么：原版契约、联机顺序、非直观边界。不要逐行翻译代码；不留无落实计划的 TODO。
- 内部能力不随意升级为公开 API。复用前看辅助类的边界，不能因为名字像就套用；两个真实调用点语义一致时再提取。
- 通过原版 `PowerCmd`、`CreatureCmd`、`CardCmd` 等结算，保留他人的 Hook；不要直接改集合或用反射模拟完整结算。
- 同步状态和命令链必须等待，不能用 fire-and-forget 隐藏异常。循环会增删目标时先取快照；异步后只重查确实可能失效的生命周期条件。
- 预览回调可多次执行：只计算，不扣次数、不推进 RNG、不生成模型。加法修饰默认 0，乘法修饰默认 1；以所覆写 API 契约为准。
- 防御只放在真实边界：网络输入、外部模型、Godot 节点销毁、反射版本差异。不在内部每层重复判空或吞异常；表现失败不能打断已经确定的共享状态。

## 4. 状态、随机与时序

- 模组关闭时，不能改变原版池、Boss 生成或共享 RNG。挂钩前确认本局激活状态；“安装了 DLL”不等于启用功能。参考设计裁决的 [生成与权重](design-decisions.md#生成与权重)。
- 每个计数明确是每卡、每玩家、每敌人、每回合还是整场。复用已有 Tracking 和序列化路径；不要新增永不清理的静态字典。
- 模型持久字段用项目既有 SavedProperty 路径；临时状态可用已有战斗追踪，但要确认存档恢复要求。UI 节点引用不能写进保存数据。
- 使用当前流程规定的 RNG；`HextechStableRandom` 是带稳定输入的确定性工具，不是替换所有 run RNG 的许可。相同输入会得出相同结果，重复触发需要明确 ordinal/回合/玩家等区分量；候选顺序也必须稳定。
- UI、日志、动画不消耗共享随机数。两端候选可能不同就传最终模型 ID；`LocalContext` 仅用于 UI/输入，不能决定仅本机执行一次伤害。
- 死亡/逃跑会移除 CombatState，依赖死亡者的数据先采集。不要把“不可切换的逃跑意图”盖在原版复活状态机上。参考设计裁决的 [敌方海克斯](design-decisions.md#敌方海克斯)。
- 原版 Power 未必支持敌方 Owner：可能在 `AfterApplied` 访问 Player/牌堆。转移时按真实类型契约决定；沿用 [祸水东引实现](../src/Runes/ScapegoatRune.cs) 的有限支持策略，不写任意 Power 通用克隆器。
- 历史记录可以读取，但必须核对事件类型、归属和两端路径；日志中 RNG 相同不证明所有历史相同，也不证明历史就是根因。

## 5. 文案与资源

- `assets/localization` 当前有九种语言：`zhs/eng/jpn/kor/esp/spa/ptb/rus/tha`。集合以磁盘为准，不能把 esp/spa 当重复而删掉。
- 模型标题/描述通常为 `SNAKE_CASE.title/.description`；敌方选择与悬浮读取 camelCase `.enemyDescription`。后者可能与我方效果不同，或者含动态变量，不能全量强行对齐。
- 数字用现有蓝色、关键词用现有金色标签；保留 SmartFormat 变量名及分支，金额/百分比/层数按实际语义译。HoverTip 给玩家必要关键词说明，避免在主描述塞实现过程。
- 改数值同时核对实际计算、预览、两条适用描述、TXT。仅修字句不改战斗代码。中文批准文案是翻译基准；技术例外不自行扩写进玩家描述。
- TXT 中有人工文案，`sync_content_txt.py` 默认只报告，只有明确采纳的条目才覆盖描述。工具使用见手册。
- 新贴图沿用 `HextechAssets` 的命名/路径规则，匹配同类尺寸和透明度，完成 Godot 导入及 PCK；源码 PNG 存在不代表游戏已能读取。

## 6. 检查与交接

当前维护版本以 `src/HextechRunes.csproj` 和 `tools/build_and_deploy.sh` 为准；本文编写时为 0.107.1、0.111.0（0.110.0 已于 0.9.8 退役），模组版本由 manifest/ModInfo 决定。不要把某个版本的 DLL 当跨版本证据。

角色卡牌升级符文的作用范围与永久成长、战斗中发放金币的联机路径与递归限制、隐藏的角色二次升级遗物（先古遗物+）的获取与隐藏约定，都记在 [设计裁决](design-decisions.md) 的对应小节。

文档改动查差异和链接；文案查 JSON/变量及相关条目；公式/Hook 用能复现旧错误的定向回归和目标编译；身份/保存/补丁变化才检查对应快照；发布才构建所有声明支持的变体并验证完整包。不要每次都运行整套测试。原版 API 使用本机反编译工具读取匹配版本的程序集，外部工具与引用路径见 [工具手册](developer-tools.md#构建环境与外部工具)。

交接记录只需写：行为结果及原因、关键文件、做过的验证、未做的验证、实际部署/发布位置，写进提交信息即可，不再按批次新建功能笔记。仍有约束力的设计裁决按 [条目模板](templates/change-note.md) 追加到 [设计裁决](design-decisions.md)。不把推测写成根因，不把打包成功写成实机通过。
