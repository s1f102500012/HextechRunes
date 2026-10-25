# 设计裁决与约束

本文只收录**代码和 CHANGELOG 里读不出来、但改这块代码的人必须知道**的裁决：为什么选这个扩展点、哪个顺序不能动、哪条边界是踩过坑换来的。过程记录写提交信息，玩法数值看 `CHANGELOG.md`，分层与补丁保留清单看 [架构说明](architecture.md)，日常契约看 [开发规范](development.md)。

新增条目沿用 [条目模板](templates/change-note.md)：一句结论 + 理由 + 涉及的类名。已经被代码自解释的不要往这里搬。

## 玩家符文

- **一呼百应同一批自动打出不会递归启动另一批。** 否则放血回手一类效果会自身无限重入；按模型 ID 匹配同名牌（升级与附魔不影响匹配），只取抽牌堆（2026-09 起不再连带手牌：遥测胜率偏差 +10.5，初始牌组打 1 张打击等于打出 5 张）、排除触发牌自身，目标死亡则按 CombatId 改选存活可命中的敌人。`RallyingCallRune`
- **轮转不息的辉星优惠必须用临时费用 + `TryModifyStarCost` 延续。** 原版辉星 ThisTurn 在打出后会被清理，不延续则"打出再回手"不再免费；能量侧用整回合减费。无每回合次数限制。`EndlessRotationRune`
- **见血封喉是加算进原版攻击伤害，不是额外伤害事件。** 因此照常吃攻击伤害修饰与格挡，预览用同一公式；小刀身份复用项目规则，所以"大号匕首"替换的君王之剑也受益。`VenomousBladeRune`、`HextechKnifeHelper`
- **森罗万象先确认持有者在本次结束回合的 participants 里再冻结球队列。** 避免队友额外回合触发；已离场的球不再触发，新生成的球不扩大本批次。`MyriadManifestationsRune`
- **祸水东引只转移逐个核对过支持敌人 Owner 的 Power 类型。** 原版没有统一的目标适用性接口；Hex、Ringing 等依赖玩家牌堆的效果只从玩家侧移除、不给敌人，否则会在敌人侧访问不存在的 Player 或带入卡牌引用。按实际层数判定类型，负数力量/敏捷也在范围内；转移时清除玩家侧 SkipNextDurationTick。`ScapegoatRune`
- **血债血偿的成长按卡牌实例记在遗物的战斗内字典里。** 牌离开手牌仍保留，同名的另一张不共享，不写回永久牌组、不给之后生成的牌补发；通过伤害加算 Hook 同时作用于预览与实际，不新增伤害事件。`BloodDebtRune`
- **瞄准镜类返还的能量/辉星只读随出牌同步的 `CardPlay.Resources`（手动出牌为实付，自动打出为 0），不读本机静态记账栈。** 玩家报告（2026-09-29）：一呼百应连打同名攻击牌 + 最万用的瞄准镜返还时，客机的能量总比房主多"被自动打出并返还的张数"，六次分叉都只有房主能量不同。旧实现在记账栈缺值时退回牌面费用，自动打出（实付 0）就多退 1；原版手动出牌的 `EnergySpent` 本来就是 `SpendResources` 的实付（X 费即实付 X）。星尘保留的辉星上报为已花费但未扣除，不返还。`HextechCombatHooks.GetResourceSpend`、`UniversalScopeRuneBase`
- **双生火焰、魔法飞弹、点亮他们！的伤害单人与联机都在出牌动作内等待结算，弹道只做视觉。** 此前单人等牌结算完、弹道命中后才用独立任务扣血，脱离命令链且目标选取时机与联机不同；代价是伤害数字先于弹道出现。`TwinFlamesRune`、`MagicMissileRune`、`LightEmUpRune`
- **冥土追魂挂在 `AfterSideTurnEndLate`（回合弃牌与虚无消耗之后），并要求持有者属于本次 participants。** 否则队友的额外回合会触发它。`NetherSoulRune`
- **自有基类的四个回合钩子（开始前/后、结束前/后）统一按 participants 过滤：持有者阵营的回合里、持有者不在本次参与者中就不转发。** 队友的额外回合（佩尔之眼等）只带那名玩家重入回合钩子，阵营检查照样通过、`RoundNumber` 也不推进，此前约 20 个符文/锻造器与几种龙魂、灼烧会在队友额外回合里多结算一次（公开仓库 PR #35 修了符文与 Power 基类）。原版传入的集合不统一：普通回合开始含宠物，额外回合开始与玩家侧回合结束只含玩家本人，所以宠物身上的 Power 按主人判定（否则奥斯提身上的灼烧永远不结算）。异阵营触发不过滤。Modifier 没有持有者，敌方海克斯改由 `HextechMayhemModifier` 把"本次参与回合的玩家"传下去：`BeforePlayerSideTurnStart` 的玩家列表、回合结束时逐玩家生效的遗忘之魂/达夫的陈年佳酿、精灵魔法的待结算无法抽牌都只作用于参与者；作用于敌人的回合开始效果照旧每个玩家侧回合都触发；按回合清空的记账仍整体清空。`HextechTurnParticipants`、`HextechRelicBase`、`HextechPowerBase`、`HextechModifierBase`
- **"每回合最多 N 次"按持有者自己的回合计：额外回合算新回合、给新次数，队友的额外回合不影响。** 本地计数按 `PlayerCombatState.TurnNumber` 判断新回合；联机计数 `PlayerRuneProcsThisTurn` 不再在每个玩家侧回合开始时整体清空，只在参与本次回合的玩家开始回合时清掉该玩家的键，两条路径口径一致。`HextechRelicBase.TurnProc`、`HextechMayhemCombatTrackingState.BeginPlayerTurnStart`
- **缩小、滑溜、人工制品三处原版漏洞修正只在本局启用海克斯时生效。** 它们修的是原版就有、但海克斯内容更易触发的问题（临时缩小叠永久缩小丢失永久效果；钨合金棍/奥斯提替身让滑溜不扣层；人工制品挡掉包围/夹击、把临时缩小的到期递减当成负面效果挡掉），不改没开模组功能的对局。`HextechCombatHooks.IsVanillaFixActiveFor`
- **原版对局重放（`NetGameType.Replay`）一律按单人流程处理。** 它没有联机连接，此前在部分入口被当成联机、走到需要同步通道的分支就抛异常；统一走 `HextechPlayerContextHelper.IsSinglePlayerFlow`，候选池始终套用本局配置。
- **"战斗第一回合"类的持有者效果看持有者自己的 `PlayerCombatState.TurnNumber == 1`（`IsOwnersFirstTurn`），不看 `RoundNumber`。** 持有者在第 1 回合拿到额外回合时回合号仍为 1，开局多抽、开局锻造器会再结算一次；原版 TurnNumber 只在该玩家开始新回合（含额外回合）时递增。已有 `_lastProcRound`/本场标记防重的、以及语义是"第 1 回合内"的持续效果（死神收割、卡卡）不改。`PreparedForge`、`OrbSlotForge`、`SilverStarsForge`、`SilverOrbForge`、`ForgingForge`、`NecrobinderForge`、`HubrisRune`、`ZealotRune`、`BrutalForceRune`
- **玩家侧活力火花必须走 `PowerCmd.Apply<HextechVitalSparkPower>`，不能把原版敌方增益直接施加到玩家。** 牌上污染层数 = 本玩家模组层数 + 场上原版活力火花总层数，但打出时每个 Power 只施加自己那份，避免重复乘算。原版 `BeforeCombatStart` / `AfterPowerAmountChanged` / `AfterRemoved` 会覆盖或清空侵蚀，普通模型 Hook 保证不了执行在原版写入之后，因此这三处用等待原 Task 的 postfix 重算。`HextechVitalSparkPower`
- **百炼成钢的临时缓慢在官方 `BeforeSideTurnStart` 清理，并按"变化后总量减本次新增量"识别旧层。** 叠层回调会刷新整个实例的 `_appliedRound`，让旧层连续多个回合逃过清理（水银沙漏的回合开始伤害是触发链）。修正依赖 `PowerCmd.ModifyAmount` 的公开契约：先改层数 → 派发 `AfterPowerAmountChanged` → 最后才检查移除零层实例。不为沙漏或冰淇淋写特例。`HextechTemporarySlowPower`
- **我方扇巴掌 / 恶趣味监听"持有者自己收到负面效果"（来源不限），且不限每回合次数；折磨者仍监听"给敌人施加"，同样不限次数。** 去掉上限是为了让同轴海克斯能叠加，而不是拿到第二个就零收益。判定沿用敌方侧的口径：只认层数增加、排除临时属性的包装 Power。`LimitedDebuffProcRelicBase` 的 `SavedProcsThisTurn` 对无上限子类已无用，但它在 SavedProperty 清单里，不能删。`SlapRune`、`BadTasteRune`、`TormentorRune`
- **坚若磐石（2026-10 重做）监听"持有者自己获得增益效果"，当场获得格挡、不限次数。** 判定只认层数增加、可见、按本次增量（`GetTypeForAmount(amount)`）判为增益的能力，排除临时属性的包装 Power（其内层力量/敏捷单独计一次）。沿用 `LimitedDebuffProcRelicBase` 基类，只覆写 `TryMatchProc`：基类的 `SavedProcsThisTurn` 在 SavedProperty 清单里，换基类会改变保存与联机布局。`AdamantRune`、`HextechRelicBase.TryGetOwnerReceivedBuff`
- **回归基本功的"无法打出 3 费及以上"只限手动出牌，自动打出一律放行。** 与敌方同名海克斯、卡卡同口径；否则同时持有"升级：XX形态"时，3 费形态牌开局自动打出会被拦下直接进弃牌堆。`BackToBasicsRune`
- **仅联机的协作海克斯按固定规则选队友，不弹选择。** 花晓之剑取"当前生命/最大生命"最低、平局按 NetId 最小的存活队友，各端算出同一个人；回复 2% 最大生命向下取整、至少 1 点，同一张牌的重放只算一次。俯冲轰炸挂在持有者自己的 `AfterDeath`：原版先分发 `AfterDeath` 再停用死亡玩家的钩子，所以能收到；伤害无来源、不吃力量与易伤，可被格挡。全心为你多人持有时乘算叠加，持有者倒下后停止生效：格挡走原版全局 Hook（持有者遗物对每个玩家目标返回倍率），治疗在 `HextechPlayerCoefficientHelper` 里按全队存活持有者计数；属性悬浮额外补上队友持有的格挡份额。我们的治疗改为"持有者被治疗时分享给队友"，仍用同一异步链防重入，防止双持互相回血。`BlossomBladeRune`、`DiveBomberRune`、`AllForYouRune`、`OurHealingRune`
- **王国军势生成仆从牌期间，嵌套进来的铸造不再生成牌。** 它与凝辉（生成牌→辉星）、王令（辉星→铸造）三件同持时构成无终点循环：mplab 复现中 1 颗辉星在 2.5 秒内把牌数从 10 刷到 144，遥测里三件同持的对局 2 胜 26 负、集中卡在拿到第三件后的第一场战斗。加防重入后，同一场景 1 颗辉星只多出 3 张牌就结束。只持有王国军势＋王令时不受影响。`KingdomArmyRune`
- **濒死狂宴（玩家与敌方）的力量补差只在 `AfterCurrentHpChanged` 里等待执行，同步的失血前缀只记债务。** 原版 `CreatureCmd.Damage` 对 `UnblockedDamage > 0` 必派发该钩子、`SetCurrentHp` 写入负值也必派发（0.107.1/0.110.0/0.111.0 相同），此前的 fire-and-forget 会让力量命令脱离命令链；其他模组绕过命令直接改生命时，补差延后到下一次生命变化。敌方版同步失败改为回滚后抛出，与玩家版一致。`NearDeathFeastRune`、`HextechEnemyNearDeath`、`NearDeathFeastEnemyHex`、`HextechNearDeathHpLoss`
- **带“每回合”状态的符文继承 `TurnScopedRelicBase`，只实现 `ResetTurnScopedState()`；回合身份按持有者 `TurnNumber` 计。** 连发（FanTheHammer）原先自造按 `RoundNumber` 的回合态，额外回合时单机不清零、联机清零，统一后两条路径一致。补偿（Compensation）的清零不按持有者阵营判断，没有并入。`TurnScopedRelicBase`、`FanTheHammerRune`
- **本体符文的治疗系数由符文实现 `IHextechHealingMultiplierProvider` 自报，用 `IsFirstOwnedInstance` 保持“同类只乘一次”。** 此前 `HextechPlayerCoefficientHelper` 硬编码 1.25/1.4/1.2/2 等倍率，与符文自身数值各写一份；链上只有 decimal 连乘，改后乘积不变。全队效果（全心为你）仍在 helper 里按持有者计数。`HextechPlayerCoefficientHelper`、`OverflowRune`、`FirstAidKitRune`、`GoliathRune` 等
- **大法师、死亡之环、快乐意外、升级隐藏宝石的单机稳定随机序号跨战斗累加、读档归零（联机按每场计数），按现状保留。** 这些序号只作稳定随机的 ordinal，改为每场清零会改变单机随机结果；字段名已改为 `_localXxxOrdinal` 表明口径。
- **角色限定符文在触发时仍判断角色。** 发放闸门 `HextechCatalog.IsAvailableForPlayer` 覆盖不了 `RelicBundleGrantHelper.GrantRelics`、控制台和其他模组直接 `RelicCmd.Obtain` 的路径。
- **升级雷暴的补发闪电仍由 Modifier 分发、排在所有监听者之后；算法与按牌记录的层数在符文自己身上。** 原版监听顺序是生物 Power → 该玩家遗物 → 牌 → Modifier，改为遗物覆写 Late 钩子会让闪电提前到奥术重击、恶魔之舞等符文的联机补记之前。补发改用钩子传入的 choiceContext。`StormUpgradeRune`、`HextechMayhem.CardEvents`
- **白洞由牌自己覆写 `AfterCardDrawn`（与原版虚空同写法），非大乱斗下（如控制台给的牌）也会回能。** 回能时机从“所有监听者之后”提前到这张牌在监听列表里的位置，仍在持有者自己的遗物/Power 之后、敌方海克斯分发之前。`WhiteHoleCard`
- **`HextechPowerCmdCompat` 在有 choiceContext 的调用点暂不改传。** 原版会把 context 传给 `Hook.AfterPowerAmountChanged`；Hook 自带的 context 在玩家做选择时会放行其他玩家的命令队列，`BlockingPlayerChoiceContext` 不会，改传会改变联机时序。强类型重载已提供，`object?` 重载只为已编译的拓展包二进制保留。`HextechPowerCmdCompat`
- **御前试剑每次打出君王之剑都生成仆从牌，重放（`PlayIndex > 0`）也算，只排除自动打出。** 原版剑圣给君王之剑加的重放每次都派发 `AfterCardPlayed`；仆从牌的稳定随机序号在每次触发时消费（联机走 Mayhem 共享计数），两端按同一出牌序列推进。`RoyalTrialRune`
- **接二连三、点亮他们！、袖中连环按原版苦无（`Kunai`）口径计数：持有者相应牌的每一次 `AfterCardPlayed` 都计 1，含重放（`PlayIndex > 0`）与自动打出；本地计数与联机历史计数（`HextechCombatHistoryHelper`，同样含重放与自动打出）必须同口径。** 旧口径只计首次手动打出，接二连三计数停在 2 时，自动打出的攻击牌每张都被多打一次而计数不推进。`TwiceThriceRune`、`LightEmUpRune`、`ChainInSleeveRune`
- **接二连三与袖中连环在战斗中单机与联机都只读原版战斗完成历史（`CardPlayFinished`，含重放与自动打出），不再有“单机本地计数、联机读历史”的双路径。** 原版每次打出先记完成历史、再依次派发 `AfterCardPlayed`；一呼百应（先获得）在外层攻击牌的 `AfterCardPlayed` 里嵌套自动打出攻击牌时，外层已在历史里，而接二连三自己的 `AfterCardPlayed` 还没执行。旧单机路径读本地计数会少算外层这张，同一张嵌套攻击单机与联机追加次数相反（起始计数 1：单机不追加、联机追加；起始 2 反过来）。袖中连环同理只差发奖时点（单机晚到外层那次），一并统一：`_shivsPlayedThisCombat` 只记已结算到的历史张数，`AfterCardPlayed` 与 `AfterCardPlayedLate` 都按历史补结算。接二连三的 `SavedAttacksPlayedThisCombat` 改为兼容占位（`get => 0; set { }`）：原版不存战斗中途状态，战斗内计数本来就不需要随存档恢复，联机分支原先也一直存 0。点亮他们！只有一条本地路径，不受影响。魔鬼之舞、秘术冲拳与 `DrawThresholdRuneBase` 仍是“单机本地计数、联机读历史”的双路径，本次范围外未改，嵌套时点是否有同类差异待逐个核查。`TwiceThriceRune`、`ChainInSleeveRune`
- **接二连三在 `ModifyCardPlayCount` 里看本张牌这一系列打出（c+1 … c+playCount）是否跨过 3 的倍数，跨过就追加一次；追加的那次也计数（会自我加速，已接受）。** 原版 `GeneratePlayCount` 在第一次打出前一次性算出总次数，`Hook.ModifyCardPlayCount` 按监听者顺序累加，0.111.0 没有 Late 版本：持有者身上的 Power 都排在遗物之前，同一玩家的遗物按获得顺序。所以比接二连三晚获得的双刀流等 +1 它看不到（计数 1 时先有双刀流：看到 2 次、追加，共打出 3 次；后有双刀流：看到 1 次、不追加，共 2 次），这些额外打出只在事后推进计数。不为此拦截 Hook 分发或复刻监听者求值。`TwiceThriceRune`
- **愈战愈勇与大法师的"随机一张手牌本回合免费"按原版木乃伊之手（`MummifiedHand`）的分级挑选：基础费 > 0 且计入全局修正后仍要花费 → 计入全局修正后仍要花费 → 基础费 > 0 → 任意手牌。** 愈战愈勇原先只看本地费用，会挑中已被三头犬、奇巧许可、剑意等全局修正变成 0 费的牌而浪费免费；X 费按原版 `CostsEnergyOrStars` 口径不进前两级。两者共用 `HextechFreeCardPicker`，各自的稳定随机键不变（分级名进键；愈战愈勇的分级名由 base/global 改为共享的四级名，同一局面的随机结果可能与旧版本不同）。`GrowingStrongerRune`、`ArchmageRune`
- **储君海克斯（无瑕、三棱镜、空白支票）判定"无色牌"与原版传家宝锤相同：`VisualCardPool.IsColorless`。** 君王之剑、仆从牌、小刀等衍生牌、事件牌都算无色，不再维护模组自己的名单；第三方卡池按其自身的 `IsColorless`。`HextechColorlessCardHelper`
- **属性转换类符文（敏捷转力量、力量转敏捷、亮出你的剑、敏捷力量转集中）只认原版施加管线登记过的那次施加。** 原版 `PowerCmd.Apply` / `ModifyAmount` 先调 `BeforePowerAmountChanged` 再问接收修正，传同一个能力实例，随机数预测不调本模组的 Before 钩子；登记按后进先出存放（施加可能嵌套），接收修正遇到登记过的实例就消费掉——即使前面的修正器（如人工制品）已把接收量改成 0；原版在 After 回调前那段 0.1–0.25 秒的等待里，模拟调用改写不了已记下的待转换量。查询本身照常返回 0，预览结果不变。`AttributeConversionRelicBase`

- **吸血习性（`DeathHarvestRune`，类名与配置 ID 沿用重做前的“死亡收割”）禁止持有者的其他回血，但放行当前生命为 0 的复活类回血。** 复活效果（原版 `LizardTail`、`FairyInABottle` 一类）靠治疗把角色拉回；把这次治疗吞掉会留下消耗了复活、0 生命却存活的状态。本符文自己的回血用调用期间的静态标记放行（`Heal` 前缀在调用时同步执行，调用返回即复位）；队友与宠物的回血不受影响。`DeathHarvestRune.BlocksHeal`、`HextechCombatHooks.HealPatch`

- **大地苏醒每次新增一块初始 5 层的滚石（原版 Instanced），回合开始时所有滚石一起滚。** 原版每块滚石各自播放动画并等它结束，Hook 逐个等待，几块就依次滚几遍。持有本符文且有两块以上滚石时，第一块被调用时一起滚全部（彼此错开 0.08 秒，最多错开 8 块），同一次分发（原版对所有监听者传同一个 choiceContext）里其余滚石跳过。伤害不按各块动画各自碰撞结算：每个敌人第一次被任意一块碰到时，按滚石顺序依次结算全部滚石对它的伤害，顺序只取决于滚石顺序和敌人站位，不受各端帧时间影响。`EarthAwakensRune.RollTogetherPatch`

- **科学狂人持有者的充能球超过 10 个时，每个球触发后的表现等待按 10/球数 压缩，整串触发总时长与 10 个球时相同。** 原版每次被动触发后本机等 0.1～0.25 秒（随游戏速度缩放）、其他玩家等 0.05 秒，写死在方法里；0.110 起在 `OrbModel.TriggerPassive`，0.107.1 在私有 `OrbQueue.SmallWait`（该版本生成充能球后的等待也走它，一并压缩）。只改等待时长，不改触发次数与顺序；联机两端的等待原本就不同，不影响同步。`MadScientistRune.MadScientistOrbWaitPatch`

- **升级打击/防御放开上限后，“镜中倒影”与敌方“神化”的降级只降一级，其余降级（魔法骑士 `DampenPower`、`WelcomeToWongos` 等）照原版清零。** 原版所有降级都走 `CardCmd.Downgrade` 并清零，调用方不可区分；`Reflections.TouchAMirror` 的状态机单步与 `UpgradeEnemyHex` 的同步调用期间进入降一级作用域（`[ThreadStatic]` 计数，不跨 await），`CardCmd.Downgrade` 的前缀记下“原等级 - 1”、原版照常清零后由后缀按原版读档同口径逐级升回。不跳过原版，降级记录与 `DowngradeInternal` 的重建保持原样。`HextechStarterUpgradeHooks.SteppedDowngradePatch`
- **升级打击/防御持有者手牌里本场未打出过的牌库打击/防御显示原版金色高亮。** 通过 `CardModel.ShouldGlowGold` 后缀实现，去重口径与胜利后升级相同（按 `DeckVersion`，战斗中生成的临时牌不发光）；手牌高亮只在牌面刷新时读取，所以每次直接扫描本场出牌历史，不另建缓存。纯本机表现。`HextechStarterUpgradeHooks.UnplayedGoldGlowPatch`

## 敌方海克斯

- **`MonsterHexKind` 一律尾部追加，不重排旧编号。** 编号是保存与联机契约。
- **"每 N 回合"= 第 N、2N、3N… 回合触发，第 1 回合不触发（N=1 即从第 2 回合起每回合）。** 与英文 "Every N turns" 一致，中文文案写"每 N 回合"（2026-09 起不再用"每过 N 回合"及其 `%(N+1)` 口径）。敌我双方统一走 `HextechRoundInterval.IsDue`，调用处直接传文案里的数字；额外回合不推进回合号，调用方仍按回合号防重。改口径时黏液史莱姆、拉加维林女族长保持文案 3/2/1、实际触发变频繁，冰霜幽魂（我方）从第 3、5、7 回合改为第 2、4、6 回合。`HextechRoundInterval`、`HextechEnemyHexContext.TryConsumeRoundInterval`
- **敌方蓝烛药箱（玩家状态/诅咒牌耗能 +1）优先级最低，视同加在基础费用上。** 原版费用 = 基础 → 卡牌临时修正 → 常规 Hook → Late Hook，敌方修饰器在监听顺序里排在遗物、能力、卡牌之后。所以 +1 在常规阶段按卡牌临时修正折算后再加（轮转不息一类的本回合 0 费会吃掉它，相对减费照常叠加）；我方蓝烛药箱的 0 费因此移到 Late 阶段，否则会被加回 1；敌方开悟的 1 费下限同在 Late 且排在遗物之后，仍最优先。原版无法打出的状态/诅咒基础费用是 -1，原版 Hook 直接跳过，这类牌不受影响。`BlueCandleMedkitEnemyHex`、`BlueCandleMedkitRune`
- **豪猪（每 4N 次）与百炼成钢（每 N 次）的"未被格挡伤害"按敌人 CombatId 计数，整场战斗累积、余数带到下回合，战斗结束才清空；获得的荆棘与临时缓慢仍只到本回合。** N 为联机人数。描述里的 `{HitsNeeded}` 由 `MonsterHexCatalog` 的按人数缩放阈值表填值，表里必须写字面量（`sync_content_txt.py` 按字面量渲染 TXT 的"4N"），测试断言它与效果类常数一致。`PorcupineEnemyHex`、`HundredRefinementsEnemyHex`
- **偷窃草蜢：AsleepPower / SlumberPower 不属于原版 `IsStunned`，必须单独排除。** 计划偷牌和行动结束实际偷牌两处都要排。`ThievingHopperEnemyHex`
- **偷窃草蜢：MinionPower 单位不偷牌逃跑。** 仆从退场常绑在首领的 `AfterDeath` 上（女王的 TorchHeadAmalgam），而 `CreatureCmd.Escape` 不发死亡回调，逃跑会破坏遭遇关系；不能靠改女王或伪造死亡事件绕过。
- **偷窃草蜢：逃跑意图靠 FollowUpState 自循环保留，不要设 `MustPerformOnceBeforeTransitioning`。** 那把锁会让千足虫 ReattachPower 的 `SetMoveImmediate(DeadState)` 失效，挡住复活。
- **偷窃草蜢（2026-10 重做）：血量低于 20% 的非首领敌人偷牌，取代每回合 20% 概率。** 敌方回合末 RollMove 时已低于阈值的直接规划偷牌意图；玩家回合里被打到阈值以下的立即补进已显示的下一次行动并刷新意图。计划后回血不撤销。`ThievingHopperEnemyHex`、`HextechCombatHooks.PlanThievingHopperTheftNow`
- **偷窃草蜢：每个敌人每场只偷一张，复活不重置次数。** 归还走原版 `SwipePower.BeforeDeath`，逃跑走 `CreatureCmd.Escape` 因而不触发返还。
- **活雾的技能上限在 `BeforeCardPlayed` 计数。** 只有在这里计数才能拦住技能内部的自动打出越过上限；手动与自动共同计数，同一张牌重放不重复占名额。这与原版 SmoggyPower 的"只能打一张 + 迷雾附魔"不同，不施加该附魔。`LivingFogEnemyHex`
- **仪式兽追加的力量在整次行动后结算一次。** 多段伤害不多次加力量；珠光护手实际重复行动时也只结算一次追加效果。
- **扇巴掌 / 折磨者 / 巨像的勇气监听的是"仍存活的敌人实际收到减益"。** 不监听敌人对玩家施加减益或自身增益；减少层数、临时属性的包装 Power 不触发，临时力量到期的自扣也不触发；同一张牌作用于多个敌人时各敌人独立记录。扇巴掌用独立的 `HextechSlapTemporaryStrengthPower` 按原版 TemporaryStrength 生命周期收回。
- **夜狩与狂徒豪气共用抽牌进度算法但各自独立字段。** `NightstalkingPlayerCardsDrawnThisCombat` 同时接入 Tracking 与 Snapshot，避免两者互相消费进度；阈值固定 12，不按联机人数调整；加滑溜用 `ApplyExact` 绕过原版滑溜的联机倍增。`NightstalkingRune`
- **敌方"多多益善"统计本局所有玩家的完整 `Player.Relics`（含作为遗物持有的海克斯）。** 每满 N 个加 1%，N = 本局玩家数，先合计再取整、无上限，每次重算不缓存开局值。
- **敌方"开悟"在能量费用 Late Hook 把低于 1 的费用抬到 1，不改卡牌基础值。** 与"无本万利"并存时，手动打出按抬高后的费用判断，不再算零费；辉星费用与 X 费不变。
- **敌方"重铸战盔"把本场收到的负数 `StrengthPower` 变化量归零。** 因此临时力量到期的回收（施加负数力量）同样不扣减；临时效果本身仍由原版移除，不在施加后补发力量。
- **敌方默认禁用项写进可修改的默认禁用集合，不从可配置内容目录里硬删除。** 用户手动重新启用必须持续有效。
- **鲜血神像在战斗已结束/正在结束/无战斗状态时改用 `CreatureCmd.SetCurrentHp` 扣 1 点、最低保留 1 点。** 奖励界面的死亡不能交给已经停止的战斗流程处理，也避免非战斗伤害修正把扣血放大到致死；仍发送原版生命变化回调。`BloodIdolRune`
- **仅开启敌方海克斯时才显示独立确认界面。** 条件是我方数量为 0 且本幕确有新增敌方海克斯；沿用现有重掷/移除/撤销与敌方同步消息，不生成玩家候选、不发放玩家遗物；界面未确认退出不标记该幕完成。
- **欧米茄、大法师、偷窃草蜢的战斗计数键统一经 `HextechCombatProcTracker` 拼接。** 旧版本战斗中途留下的快照读回后：欧米茄的一次性标记失效（只有载入后同一回合 4 再触发回合开始钩子才可能重复），大法师当场随机序号从 0 重新计；不会多触发。`OmegaEnemyHex`、`ArchmageEnemyHex`、`ThievingHopperEnemyHex`
- **八文门“每回合最多 2 次”保留两个 HashSet。** 它们参与战斗快照序列化，改成字典需要迁移旧 JSON，收益太小。
- **敌方"开悟"用专属图标载体 `EnlightenmentHex`（棱彩），玩家符文"开悟"仍是 `EnlightenmentRune`（黄金、原图标）；不再被读取的 `enlightenmentRune.enemyDescription` 已删除（同感染棱柱先例）。** 敌方海克斯按 `MonsterHexKind` 编号存档与同步（`SavedMonsterHexByAct`/`SavedMonsterHexesByActJson`/`SavedCarriedMonsterHexes` 与选择消息都是 int），换展示载体不影响旧存档读回；新载体追加在 `EnemyHexIconRelicTypes` 末尾，保持既有载体的 SharedRelicPool 登记顺序。新增模型改变模型表，两端须同版本。`EnlightenmentHex`、`HextechMonsterHexRegistry`
- **敌方 `MonsterHexKind.MadScientist` 重做为"升级：蜂群术士"：敌人减少 30/15/0% 最大生命值，并获得 1 层原版人体蜂房（`PersonalHivePower`）；删除自定义的"受到伤害时往弃牌堆加晕眩"。** 晕眩改由原版能力结算（只认攻击伤害，加到攻击者抽牌堆随机位置，奥斯提的攻击归到主人），旧实现里单人/联机归属晕眩的差异随之消失。人体蜂房与最大生命减少共用同一持久标记 `MadScientistApplied`（开战时所有敌人、战斗中召唤/分裂出的敌人各一次，首领转阶段随最大生命重放），直接 `PowerCmd.Apply`：原版不按人数缩放它，固定 1 层。原版蜂群术士自带 1 层，叠加为 2 层，其吐丝招式在不足 3 层时照常 +1（提前一回合到 3 层上限）；0.107.1 的吐丝兼容补丁只在没有人体蜂房时接管，不受影响；人体蜂房在薄暮法衣的不可镜像名单里，玩家侧实例由安全补丁吞掉。图标载体改为敌方专用 `EntomancerHex`（追加在 `EnemyHexIconRelicTypes` 末尾，暂借 `madScientistRune.png`），玩家符文科学狂人（`MadScientistRune`）不变；不再被读取的 `madScientistRune.enemyDescription` 已删除。枚举成员名与编号保留为配置禁用列表、遥测与存档的兼容契约。`MadScientistEnemyHex`、`EntomancerHex`
- **「我饥饿」的消耗名额不记账，每次从 `CombatManager.History.CardPlaysFinished` 数本回合该玩家已手动打出的非能力牌（重放只算第一次）。** 原版 0.107.1–0.111.0 都在进入出牌循环前问去向，此时本次出牌尚未记入历史，真实结算与预览数到同一个值；旧实现在去向钩子里记账，被随机数预测的悬停预览占掉名额导致联机分歧（#22）。`EightPennyGateEnemyHex`
- **「我细看」的已阻止次数在 `AfterPreventingDraw` 里记，`ShouldDraw` 只读。** 原版钩子不带玩家参数，由 Mayhem 在 `ShouldDraw` 里记下最近一次拦下的玩家；原版 `CardPileCmd.Draw` 与 `HextechSelectedDrawHelper` 都在查询返回 false 后同步紧接着调 After，中间没有等待。`HextechMayhemModifier.CombatModifiers`、`IInspectEnemyHex`
- **原版"战斗状态变化"通知不继承伤害命令作用域。** 它在伤害命令内部启动延迟任务，下一帧带着命令 ID 调订阅者，随机数预测在订阅里模拟回合结束，滑溜、穿针引线这些按命令 ID 记账的查询钩子会把模拟记成真实结算（代偿已改为不依赖命令 ID，见下条）。补丁在同步的 `NotifyCombatStateChanged` 入口清空、返回前恢复。`HextechCombatHooks.CombatStateNotifyScopePatch`
- **代偿（我方/敌方）不再依赖伤害命令作用域，改为"查询记录 + 原版 After 确认"。** `ModifyHpLostAfterOsty` 每次都按同步状态拆分并覆盖"最近一次拆分"记录，不记待结算；原版只在 `CreatureCmd.Damage` 里、紧接着改动了数值的那次 `Hook.ModifyHpLost` 调 `AfterModifyingHpLostAfterOsty`，在这里把记录确认成按目标的待结算项，受伤回调再取走施加下回合伤害/灾厄。旧实现要求查询时能读到 AsyncLocal 命令 ID，联机中出现过主机读不到（全额扣血）而客户端能读到（拆分）的分歧，且可从读档稳定复现；查询结果只取决于同步状态后，各端拆分一致。只在查询里调用、不走 After 的模拟（随机数预测）只改写记录，下一次真实查询会覆盖。滑溜、穿针引线仍按命令 ID 记账。`CompensationEnemyHex`、`CompensationRune`、`HextechMayhemModifier.AfterModifyingHpLostAfterOsty`

## 卡牌升级

- **`CardUpgradeRuneBase<TCard>` 的局部替换一律是 `Priority.Low` 的条件 prefix。** 原版没有改变单张卡/Power 内部操作的细粒度 Hook；只有模型所属玩家持有对应符文时才跳过回调，其他玩家与未启用效果保持原版。
- **这些局部替换的入口与异步 `MoveNext` 的 IL 冻结在 `vanilla_copy_guard.<target>.txt`。** 由通用守卫测试 `VanillaCopyGuardFreezesEntriesAndAsyncBodies` 校验；`HEXTECH_WRITE_PATCH_MANIFEST=1` 只追加缺失行，漂移行必须人工核对原版行为后手改，刷新快照不能代替行为审查。哈希取自"元数据 token 解析成成员/字符串描述"后的 IL，不是原始字节：同一版本的 sts2.dll 在 Windows、macOS arm64、macOS x86_64 上分别编译（PE 目标机器不同），守卫目标里原始 IL 有九成只差 token 编号，按原始字节冻结会让非 macOS arm64 玩家整表误报 DRIFT（#22 的 Windows 日志即此）。不按平台分表、也不按 `release_info.json` 的 `main_assembly_hash` 选表：后者与任何一份实际发行的 sts2.dll 都对不上。`HextechVanillaCopyGuard.NormalizeIl`
- **爪击的永久成长按 `DeckVersion` 去重写进 `HextechSelfUpgradeCardStore`。** 原版爪击的本场成长不写入永久计数；之后新获得的爪击不追溯之前的触发。`ClawUpgradeRune`
- **吊杀施加独立的 `HextechHangPower`（倍率 2、4、8……），适用于该目标受到的所有伤害。** 但不把直接失去生命改成伤害，也不替换其他玩家原版吊杀的 Power。`HangUpgradeRune`
- **狱火用自身伤害命令返回的 `DamageResult.TotalDamage` 施加灼烧。** 包含被格挡的伤害，但不把伤害链中其他效果的伤害算成狱火的。`InfernoUpgradeRune`
- **范围限定的三条：** 子弹时间只阻止该牌自身施加的无法抽牌；狂怒/倒映只阻止持有者对应 Power 的定时清理，不禁止外部移除；粒子墙只改战斗卡实例的格挡，不回写牌库本体。
- **烟囱在持有者抽到状态牌时补伤害，不是"加入手牌"就触发。**
- **升级散射炮（2026-10 重做）把消耗牌堆里的状态牌也计入命中数，只消耗不在消耗牌堆的那些。** 原版命中数与卡面显示都经私有 `FlakCannon.GetStatuses`，后缀补丁对持有者扩展到消耗牌堆；`OnPlay` 跳过前缀避免把已消耗的状态牌再消耗一次。`FlakCannonUpgradeRune`
- **升级重启（2026-10 重做）把末尾的随机抽牌换成从抽牌堆选牌，走 `HextechSelectedDrawHelper`（与验牌同一条选择抽牌路径，按抽牌结算并受不能抽牌、手牌上限约束）。** 不再去掉消耗。抽入一张已选牌可能嵌套触发别的抽牌（升级自动化），把同一轮里还没抽入的已选牌提前抽走：这些牌已按抽牌结算过，跳过且不计额度，下一轮从剩余抽牌堆补选差额（`DrawSelectedRounds`）。`RebootUpgradeRune`
- **五件"升级：XX形态"（恶魔/群蛇/虚空/回响/死神）统一为棱彩阶，共用同一张棱彩图标。** 五件效果同构（开战自动打出全部对应形态牌），2026-09-30 按玩家反馈把虚空、回响、死神从黄金提到与恶魔、群蛇同阶；稀有度是候选池分组依据，改动即改变生成池，两端须同版本。配置 ID 是模型 ID、不含稀有度，已有启用/禁用设置不受影响。`VoidFormUpgradeRune`、`EchoFormUpgradeRune`、`ReaperFormUpgradeRune`、`HextechPlayerRuneRegistry`

## 金币与奖励

- **战斗内发金币一律在模型 Hook 里等待原版 `PlayerCmd.GainGold`，不按 `LocalContext` 筛选执行端，也不追加金币同步消息。** 确定性来自各端执行同一条触发链，不能从 UI 或本地回调补发。涉及献祭、收集者、炽燃利息、小猪存钱罐、夺金、升级：王国资产。
- **小猪存钱罐、夺金、炽燃利息在自身发钱期间禁止自身重入（`finally` 解除）。** 防"获得金币 → 鲜血神像伤害 → 受击/反击 → 再次发钱"的循环；后续独立伤害仍正常触发。
- **`SavedCountThisCombat` / `SavedCounter` 只是存档占位（getter 恒为 0、setter 丢弃），名称与类型保留。** 金币在触发时立即发放；旧档里的遗留计数读档后总会先经过 `BeforeCombatStart` 清零，所以不设战后补发路径。改名或删除这些属性会破坏旧档反序列化。
- **欧洛巴斯二次强化只在 `TouchOfOrobas.GetUpgradedStarterRelic` 的 postfix 里、且原结果是头环时才补映射。** 不覆盖其他模组已有的非头环升级结果；已持有"+"版时再次获得仍映射到同一"+"版，不叠加也不降级。`OrobasPlusUpgrades`
- **"+"版继承 `RelicModel` 而非 `HextechRelicBase`。** 因此不参加海克斯计数、重铸与候选生成，只注册到 EventRelicPool；保留 Starter 稀有度并默认在图鉴隐藏。类名是联机契约，不要改。
- **启用判定复用 `HextechMayhemModifier.IsEnabledForRun`：缺少 modifier ≠ 禁用。** 单机旧局或控制台缺 modifier 时读菜单开关，否则单机会回退成头环；联机缺快照时不使用各端本地配置。
- **古老牙齿遇到永恒牌会抛异常并卡住整条组合奖励链。** 原版 `CardTransformation` 构造器与 `CardCmd.Transform` 都检查 `IsTransformable`。修法是 AsyncLocal 作用域 + `CardModel.IsTransformable` postfix 只放行本次记录的那一张永恒牌，不改 `IsRemovable`、不删原牌关键词、不复制原版转换命令、不吞其他异常。`ArchaicToothEternalHooks`
- **神迹事件的锻造器稀有度 65/25/10 是固定值，不跟随本局配置里改过的权重。** 是否跟随属于平衡裁决，未改。`MiracleEvent`（拓展包）

## 视觉

- **带文字的按钮不用 Godot `Button.Text`，文字交给居中的 `MegaLabel`。** `Button` 用主题默认字体，中日韩等语言会落到系统回退字体而发虚；`MegaLabel` 在 `_Ready` 时按当前语言替换字体。选择界面的确认按钮统一走 `CreateConfirmButton`（与重随/移除按钮同一套铜金色面板）。
- **濒死狂宴红光用的是原版 0.111.0 SOUL_NEXUS 图集的 `glowie` 区域，未改色未重绘。** 源 `res://animations/monsters/soul_nexus/soulnexus.png`，图集 1063×656，裁切 `(2, 77, 580, 577)`，无旋转；模组自己打包该纹理，运行时不依赖原版图集位置、Spine 或怪物场景脚本。重新提取用 `tools/extract_near_death_feast_glow.gd`，该脚本只校验整张图集尺寸，识别不了同尺寸重排 —— 游戏更新后先人工核对 `.atlas` 里 `glowie` 的坐标。`HextechNearDeathFeastVisual`
- **红光几何是调出来的固定值：** 中心在碰撞框自底向上 64% 处，基础宽度 = 碰撞框宽度夹取到 120–360 像素后的 2.45 倍，两层同步缩放。
- **表现节点不进战斗状态、不调用共享 RNG。** 死亡或脱离濒死时隐藏，角色节点销毁时释放。
- **灼烧常驻火焰保持程序化渐变粒子(沿骨骼发射的火焰、烟与火星);每次灼烧结算额外升起原版地面火 `NGroundFireVfx`(状态牌"灼伤"同款)。** 实机试过三种替代都被否决:原版火把 4 帧翻页图放大后像多边形碎片,整团着色器火焰摆在脚下像站在一排篝火上,着色器火苗无论撒在身上还是从脚底窜起都像火焰贴纸。`HextechBurnVisual`
- **夺金命中爆金币复用原版小鬼佣兵的 `vfx_coin_explosion_regular` 场景，挂到被命中生物的父节点、定位到碰撞框中心，并在主线程读取坐标。** 挂 `CombatVfxContainer` + 读 `VfxSpawnPosition` 的原版组合在实机上把金币放到了屏幕左上角；Godot 在非主线程读全局坐标会得到原点。不用 `VfxCmd.PlayOnCreatureCenter`，它会跳过已死目标。`HextechCombatVfx.CoinBurst`
- **海克斯选择界面的金色重随按钮光与点击、卡牌弹出、重随、常驻、悬停、选中与未选中特效按原版海克斯大乱斗选取界面的粒子定义播放，不再手调着色器。** 数据和贴图来自英雄联盟 16.20 `UI.wad.client` 的 `KiwiAugmentSelection/Particles`（共 25 个系统，播放其中 21 个、223 个发射器；背景魔法流、任务与"必出新"标签三组不适用，理由见 `assets/images/effects/kiwi_selection/SOURCE.md`），出处与换算见同一文件。接入时机和上下层次照抄原版 `UIBase` 里各粒子节点：Layer 低于卡面图标（15）的常驻（5）、悬停（6）、未选中（6）、重随重建（7）插在稀有度边框之后、卡面内容之前，弹出与重随闪光（21）、选中（25）盖在卡面上，同一区域按 Layer 排序；粒子原点是节点矩形的中心，不是锚点 (0.5,1.0)。常驻光去掉重画卡底与卡框的发射器（本模组卡牌自己画边框，银色因此没有常驻光）并再乘 0.75 亮度；悬停光跟鼠标悬停与手柄焦点，离开淡出 0.2 秒；粒子寿命 -1 表示常驻粒子，直到特效被释放。选中后的退场照抄原版：确认时按钮切到选中状态（原版各状态列表只剩常驻光），悬停光立即撤掉，选中卡播放选中特效、其余卡播放未选中特效（压在内容下方、0.4 秒内自行褪去的灰卡，不负责移除卡牌）；界面关闭时三个卡槽按原版 `AugmentSelection` 场景的 `sceneTransitionOut`（1.0 秒、透明度 255→0）原大小原位置一起淡出，选中卡不单独停留，原版没有位移、缩放或飞向 HUD。缓动 `easingType` 18 只有枚举值，按常见排列推断为 InOutExpo；原版场景背景也随之淡出，本模组的面板底与其余元素没有原版对应物，为不在地图上留半透明黑底直接透明。时间线与出处见 `SOURCE.md` 的选中流程一节。每个发射器一个 `MultiMeshInstance2D`，曲线在 CPU 推进，贴图合成在着色器。本模组重随按钮相对卡牌比原版大，所以按钮外框一类发射器按按钮缩放、其余按卡牌缩放（数据里的 `space` 字段），卡牌比例取卡框高 = 2×187 单位。贴图寻址 0/1/2 按重复/夹紧/镜像、溶解默认读红通道、混合模式 4 视为加法，都是按原版用法推断的，实机效果不对先查这几处。随机数用节点 id 播种的本地序列，不进共享 RNG。模组程序集没有经过 Godot 源码生成器，引擎调不到模组节点覆写的 `_Process`，所以特效在进入场景树时订阅 `SceneTree.ProcessFrame` 推进；粒子每帧整块写入 MultiMesh 缓冲。原版亮度叠在本模组卡面上偏闪，着色器整体乘一个亮度系数（加法 0.55、Alpha 混合 0.8）。重随会重建全部卡槽，没被重随的卡上的特效先摘下再挂回，不中断；退场渐隐时界面挪到根节点下 Layer 100 的 CanvasLayer，画在随后打开的地图之上，选择结果与联机同步不等这段动画；锻造器选择用同一套退场。退场层高于叠加层栈，退场期间若有新界面入栈（连续锻造器选择、选完海克斯紧接着发锻造器、锻造器后的原版选牌界面），剩余淡出压到 0.12 秒，避免旧卡盖住新界面开头；局内场景退出时立即释放。数据与贴图在模组初始化时后台预解码。`HextechKiwiVfxPlayer`、`HextechGoldenRerollVisual`、`HextechRuneSelectionScreen.Vfx`
- **原版能力牌飞入特效 `NCardFlyPowerVfx` 的 `SwooshPath` 曲线是场景共享资源，每个实例在 `_Ready` 改写同一条曲线的终点；曲线中间的弯曲点按“出牌区正中飞向角色”固定。** 同时飞多张时必须给每个实例一份私有曲线（`Create` 后缀），横向展开的牌还要把整条曲线按正中那张牌的路径做旋转与等比缩放对齐到自己的起终点（`_Ready` 后缀，按登记的横向偏移），否则终点排成直线、离角色近的牌越过角色再折回。单张飞行不登记偏移，保持原样。`HextechCombatVfxHooks.PowerFlyCurvePatch`、`PowerFlyReadyPatch`
- **开战直接获得原版能力的海克斯不用“能力牌飞向角色”的动画，改为以角色为中心的专属演出，每段播完再施加对应能力（不动如山两段、霸符兄弟两段、基石猎人一段）。** 战争交响乐施加形态时原版自带两个形态演出，不再叠加。演出等待只是本地计时，不改结算内容与顺序。`HextechCombatVfx.CombatStartShows`

## 生成与权重

- **模组关闭时模型仍无条件注册，只在 `SharedRelicPool.GetUnlockedRelics` 的窄范围 postfix 里排除海克斯内容（`HextechRelicBase` 或注册表内的符文/锻造/商店/敌方图标载体）。** 原版 `RelicGrabBag.Populate` 会给 Starter 稀有度条目洗牌，即使它们最终不掉落也会消耗与遭遇/Boss 共用的 UpFront 随机数，生成后再移除条目无法回退已推进的 RNG。过滤保留原版与其他模组条目及顺序。
- **不引用本程序集的外部模组经 `HextechRunesInterop.RegisterPlayerRune` 注册只继承 `RelicModel` 的符文；"是不是海克斯内容"一律按注册表判定，类型判定只作兜底。** 强类型 `HextechRunesApi.RegisterPlayerRune<T>` 保留 `HextechRelicBase` 约束，拓展包等既有调用方不变。外部符文必须是 Starter 稀有度，否则 `IsAvailableForPlayer` 拒发——原版多处按稀有度取遗物，只有 Starter 能挡住自然池以外的漏出。基类虚方法换成登记时附带的可用性委托，首个登记者生效，抛异常按不可用。Interop 签名发布后不再改，新能力新增方法并递增 `ApiVersion`；不公开 `HextechAssets`，外部符文自己覆写图标路径。来源标签（`HEXTECH_POOL.<key>`）与配置菜单分组标题由外部模组经 `SetPlayerRunePoolLabel` / `RegisterConfigSectionTitle` 自选，未指定时分别是角色名或"通用"、"外部模组：<id>"；额外拓展包保留内置的"拓展包"与"额外拓展包"，不必为此发版。外部给的键可能缺失，而原版缺键会抛 `LocException` 打断整个界面，所以这类文字统一走 `HextechRuneLabels` 先查键、缺失时显示键名。对接说明见模组根目录 `INTEGRATION.md`。`HextechRunesInterop`、`HextechCatalog.IsAvailableForPlayer`、`HextechNaturalRelicPoolHooks`
- **该过滤只读本局冻结配置，不读本地菜单值。** 联机房主配置尚未同步时读本地值会产生不同候选池。已接受的代价：开启模组的新局种子结果也可能与旧版本不同；旧存档已生成的房间和遭遇不重置。
- **角色专属海克斯用动态权重，没有固定位置保底。** 每名玩家新局 150%，刷出非专属 +10 个百分点、刷出本角色专属 −10 个百分点，最低 0%、无上限；三个位置依次抽取并立即使用更新后的倍率。
- **计数依据是刷出的候选，不是最终拿取。** 重掷成功生成的候选也计一次，未发生替换不计；混沌实验室在普通候选生成之后替换，不产生第二次计数。
- **倍率为零且合法池只剩专属时回退到原有标签权重，避免空选项。** 没有映射到原版角色池的模组角色不推进专属权重。
- **权重确认后提交绝对值，不按最后三个候选反推被重掷覆盖的历史。** 远端缺倍率或格式错误时中止该选择，禁止默默回退到 150%。`HextechWeightedRuneOptions`
- **权重存进既有 `SavedRuneSelectionJournalJson` 的 `characterWeights`（按玩家 ID 排序），没有新增或改名 SavedProperty。** 旧存档缺这部分数据时从 150% 开始；无尽循环清理选择流水时保留倍率。
- **每名玩家本局见过的海克斯（展示过、重随刷出过的）不再出现在之后的候选里；未见过的抽完后才回到见过没选的，再抽完就只给"继续"界面。** "已见"按玩家存进 `SavedSeenPlayerRuneIdsJson`，只记条目名，还原时必须用遗物的真实分类（`RELIC`）；这份 JSON 参与双端比对，所以联机时本次展示过的完整候选（含重随中途刷出又被换掉的）随确认结果同步，两端在确认后统一写入（`CommitSeenRuneSelection`）；已见区段每块最多 64 个 ID、可跨多块，重随次数不受 64 上限约束，缺少已见区段的旧消息一律拒绝（与旧版本不能联机）；单人在展示和重随时照记，自选补记最终所选；2026-04 起误用模组名作分类，排除从未生效（遥测 0.9.5 后续幕约 10% 的候选含早幕见过的）。单人与联机的每幕生成、玩家重随都是"未见池为空才放开已见"。"继续"界面是纯本机界面、不同步、不发放；每名玩家每幕最多弹一次；单人若本幕还能调整敌方海克斯，就用仅敌方界面代替。联机时敌方调整的权威玩家若本次没有候选，本幕不开放敌方调整，否则其余客户端会一直等他的调整结果。`HextechMayhemChoiceHistoryState`、`ShowNoRuneOptionsScreenAsync`
- **敌我海克斯不再互相回避同名（2026-09 删除）。** 玩家三选一与重随只排除本局已见过的符文；敌方每幕掷骰只排除敌方已出现过的，敌方重掷只避开同一界面上其他敌方槽位和已见过的敌方海克斯。原先玩家候选会排除敌方当前海克斯的同名、敌方重掷会避开玩家当前候选，但敌方首掷从不看玩家已持有的，遥测 0.9.5 仍有约 1.9% 的对局出现同名，规则不完整也不直观，因此整体去掉。
- **玩家重随次数设为无限时，每幕选择界面改成直接自选：列出本稀有度的全部合法海克斯，与配置界面的启用开关同一套过滤（配置、幕、角色、已拥有、互斥）。** 判定读本局冻结配置（联机是房主的）；三候选照常先按权重生成，所以候选 RNG 与角色倍率照常推进，稀有度也由候选决定。自选池只在本机用 `BuildSelectableRunePool` 构造，不消耗 RNG；提交时把最终候选换成所选的一个（序号 0、无重随历史、倍率原样），远端按 ID 还原，同步格式不变。锻造器选择与只选敌方海克斯的界面不受影响。`HextechRuneSelectionScreen.SelfPick.cs`、`BuildSelfPickPool`
- **海克斯选择二次确认（来自公开仓库 PR #33）是本机界面偏好，默认关，放在配置-杂项。** 开启后普通每幕三选一点卡片只标记待定，按确认才提交；待定时仍可重随，重随待定的那张会清掉待定。它只改变本机何时提交，提交内容与同步协议不变，所以存在 UI 偏好文件而不是本局冻结配置，联机各端可以不同；界面打开时读一次。锻造器、只选敌方海克斯、自选模式都不启用：自选本身就是点选加确认，而且它的确认走同一个选定入口、不带卡槽，若启用会被当成无效待定而吞掉。`ShouldUsePlayerRuneConfirmation`
- **`HextechForgeRarityWeights` 是 `HextechRarityWeights` 的全局别名，配置 JSON 与分享码形状不变。**
- **敌方图标载体表 `EnemyHexIconRelicTypes` 按手写顺序保留。** 这个顺序就是模型进 SharedRelicPool 的登记顺序，从 MonsterHex 注册派生会改变顺序；由测试守住两者集合一致。`HextechCustomModelRegistry`
- **`IsUpgradeRune` 保持按类名后缀判定。** 这些符文没有共同基类（另有 3 个直接继承 `HextechRelicBase`），外部模组也沿用该命名。`HextechRunePoolBuilder`
- **外部 API 登记本体内置的符文或锻造器时，在参数校验之后、登记窗口检查与任何副作用之前就被拒绝，只告警不抛出。** `HextechExternalContentRegistry` 保留同一检查作兜底；窗口关闭后再登记内置类型也只告警。`HextechRunesApi`

## 手柄

- **是否给默认焦点只看游戏自己的输入模式（0.110 起 `IsUsingDirectionalNavigation`，0.107.1 为 `IsUsingController`），鼠标玩家打开界面不出焦点框。** 不能从事件类型自己判断：Steam Input 下按键到达时已是合成的动作事件，没有原始 `InputEventJoypadButton`；不走 Steam 时第一下按键又会被原版切换手柄模式时吞掉。原版切进手柄模式和切换界面时都会聚焦 `IScreenContext.DefaultFocusedControl`，选择界面靠这个拿到焦点。`HextechControllerInput`
- **原版确认键 A/× 映射为 `ui_select`，只有原版 `NClickableControl` 认它；本模组的 Godot `Button` 和自绘控件只认 `ui_accept`，而游戏里没有任何手柄键映射到 `ui_accept`。** 选择界面和配置菜单在自己子树有焦点时，把 `ui_select` 的按下和松开延后转成 `ui_accept`；原版可点击控件不转换，以免触发两次。`HextechControllerInput.TryTranslateSelectToAccept`
- **配置菜单挂在场景根上，不是原版认得的当前界面。** 打开期间关掉主菜单的可聚焦性，否则方向导航会跨过遮罩落到背后的主菜单按钮；社区面板和上传对话框登记为子弹窗，按 B 先逐层关闭它们；LB/RB 切页签。`HextechControllerOverlay`
- **顶栏的敌方海克斯折叠按钮和隐藏 UI 开关不在原版顶栏焦点链上，手柄够不着，暂未处理。**

## 配置、遥测与流程

- **`rune_config.json` 里的 `ChaosRuneChancePercent` 保持已发布的 PascalCase 键名，其余字段是 snake_case。** 改名会让旧配置读不到；用显式 `JsonPropertyName` 钉住。默认值与钳制集中在 `DefaultChaosRuneChancePercent` / `ClampChaosRuneChancePercent`。
- **遥测配置缺 endpoint 或 endpoint 为空时只补默认地址，不覆盖用户写的 `enabled`。** 默认开启、明文 HTTP、无游戏内开关维持现状（用户决定）；文件损坏时的行为不变。`HextechTelemetry.Config`
- **无尽模式检测只认已加载的 EndlessMode，在 ModManager 初始化完成后缓存。** 旧实现读 `_mods`，把未加载的模组也算进去。`HextechCatalog.IsEndlessModeLoaded`
- **配置菜单的计数与网格同口径：打开菜单时按网格实际列出的条目算一次 ID 集合，页脚、角标、社区配置摘要共用。** 我方总数不再包含生成型符文，敌方总数按网格条目，禁用集合里的未知 ID 不计入。`HextechRuneConfigMenuHooks`
- **远古事件结束后等待其他玩家完成事件，不设单端超时，只在等满 18000 帧时告警一次后继续等待，换局才退出。** 超时由各端独立判定：先放弃的一端会等下一房间，后完成的一端进入选择界面等它，而地图行进已被禁用、原版进入下一房间需要全员投票，双方互相卡住。要取消只能走同步协议让所有客户端一起放弃。`HextechRunLifecycleHooks.EventSelection`
- **“已连接的联机”统一用 `HextechPlayerContextHelper.IsMultiplayerConnected()`，NetService 为空时返回 false。** 此前选择、夺金、锻造发放三处在 NetService 为空时会抛空引用。
- **单机在海克斯选择或其拾取效果（如棱彩海克斯连续发放 6 个锻造器）途中保存并退出，读档一律回滚到选择前重选（用户裁决）。** 选择开始到本幕完成之间不存档，原版“保存并退出”本身也不存档，所以读档回到选择前的最后一个存档、本幕未决，按原调度（进地图房时，或先古“继续”后）重新弹出选择；半途发放的结果不保留、不重复发放、不跳过选择。`InitialForgeGrantRune` 的待发标记只作联机/其他来源意外存档的兜底。`HextechRuneSelectionCoordinator.Core`
- **初始锻造器补发幂等，只发剩余数量（2026-09-30 新增 `SavedInitialForgeGrantCompletedCount`，SavedProperty 集合变化，与旧版本不能联机；本次发布本来就不兼容）。** 旧实现只存待发布尔，读回后按完整数量重发（6 个已领 3 个再发 6 个，累计 9 个）。现在每完成一步（含被配置禁用拦下的一步）递增完成数并随存档保存，恢复时只发 `InitialForgeCount - 已完成数`，稳定随机序号从已完成数接着走；候选盐里还有 `player.Relics.Count`，背包没有其他变化时它与一次发完时第 k 步相同，所以接续的候选与未中断时一致。旧存档没有完成数（读回为 0），按背包推断：原版背包按获得顺序追加、存档按列表顺序写出读回，数排在该符文之后、同一楼层（`FloorAddedToDeck`）获得、属于注册表锻造器的遗物；已完成数取保存值与推断值的较大者并封顶为总数——宁可少补也不重复发放。`InitialForgeGrantRune`、`HextechForgeGrantHelper.TryObtainRandomForges`
- **原版退出清理期间不推进、不完成本幕。** `RunManager.CleanUp` 先置 `ShouldSave=false`、`IsCleaningUp=true`，再清覆盖层，最后才清 State；清覆盖层会取消等待中的选择/锻造界面，让本局任务链在 State 仍指向本局时同步续跑。只比引用会把正在拆除的对局当成当前局，继续标记已决并打出误导性的存档日志，所以拾取返回后、完成本幕前和存档前都用 `IsCurrentRunInProgress`（引用相同且不在清理中）复查。`HextechRuneSelectionCoordinator.IsRunInProgress`
- **本模组自己的存档必须带上已完成的事件房。** 先古/涅奥结束、点“继续”后才开始的选择，此时 `SaveRun(null)` 会让读档按地图坐标把先古当新事件重开，而存档里本幕已决，玩家就看不到海克斯选择、直接重进先古。当前房是 `EventRoom { IsPreFinished: true }` 时作为 preFinishedRoom 传入，与原版事件完成时 `SaveRun(this)` 同口径；单机完成存档与联机检查点都走这条规则。`HextechRuneSelectionCoordinator.SelectPreFinishedRoomForSave`
- **原版模组列表里本体与拓展包的名称、介绍按当前游戏语言显示，manifest 保留中英双语作兜底。** 原版 `ModManifest.name/description` 只是单个字符串，`NModMenuRow._Ready` 与 `NModInfoContainer.Fill` 原样显示，没有本地化入口；由本体的两个后缀（`Priority.Low`，只认 `HextechRunes` / `HextechRunesSponsorPack` 两个 id）换成原版会合并的 `main_menu_ui` 表里的 `HEXTECH_MOD_*` / `HEXTECH_SPONSOR_MOD_*`。拓展包的键放在拓展包自己的 `main_menu_ui.json`，拓展包未加载时原版不合并它，自然回退，不给拓展包另加补丁。键缺失、查表异常或标签已被其他模组改写时保持 manifest 原文，绝不显示键名。语言只能在主菜单设置里切换，原版随即 `NGame.Relocalize` 重建主菜单，所以不订阅语言切换事件。manifest 的 `name` 不参与联机校验（`GetGameplayRelevantModNameList` 用 `id-version`），设置存档按 id + 来源记启用状态，改名不影响识别、存档与联机。`HextechModListLocalizationHooks`

## 待实机验证

- **F04 幽灵鳗 Skittish 的动画顺序改动已回滚，源码保持 `6a5ec941` 原样。** 曾把"BlockEnd 音画失败仍保留 Skittish"改成先移除 Power 再补出场音画，但这个顺序正是玩家实报"感受燃烧打四鳗卡死"的修复点，未经实机验证不要再调整。`src/Combat/HextechMonsterInteractionPolicy.cs`
- **F10/F11 在自有基类边界用 `new` 隐藏非虚的 `Flash` 重载与计数事件，只捕获表现回调，尚未实机验证。** 目的是防 UI 订阅者抛错截断共享写入；只隔离原版本来就属于 UI 的事件，不拦原版/第三方全局事件，也没有把所有 Flash 搬到共享写入之后。`HextechRelicBase.TurnProc.cs`、`src/Compat/HextechPowerBase.cs`、`src/Compat/HextechModifierBase.cs`；表现层兜底统一用 `HextechPresentation.TryRun`
