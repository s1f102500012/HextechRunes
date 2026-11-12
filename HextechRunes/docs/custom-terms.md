# 模组自创术语的九语言写法

本表只收模组自创或沿用原版名称但机制不同的术语，是文案改动的用词依据，不更改玩法。**原版**术语的中文对照由 `tools/official_zhs_titles.json` 提供，`validate_hextech_content.py` 直接读取它，不要在文档里另维护一份。

九种语言以磁盘为准：`zhs/eng/jpn/kor/esp/spa/ptb/rus/tha`，`esp` 与 `spa` 分别取值、不能互相代用。变格、复数、动词与名词的语法变化不是另一套术语。

## 多数写法及使用边界

写法按修改前模组九语言 JSON 中同概念现有写法的多数选定（统计排除 `.flavor`，剥标签后按大小写不敏感的最长候选非重叠匹配，复数/格变化合并到词典形）。计数只是定位多数的文本证据，不表示每个同形词都属于该 Power，因此修正文案时必须核对模型与上下文，不全局替换同形的原版卡名。

| 概念 | zhs | eng | esp | spa | jpn | kor | ptb | rus | tha |
|---|---|---|---|---|---|---|---|---|---|
| 灼烧 | 灼烧 | Burn | Quemadura | Quemadura | 火傷 | 점화 | Queimadura | Ожог | เผาไหม้ |
| 属性锻造器 | 属性锻造器 | Stat Forger | Forjador de atributo | Forjador de atributo | ステータス鍛造器 | 능력치 모루 | Forjador de Atributos | Наковальня показателей | ช่างหลอมค่าสถานะ |
| 锻体 | 锻体 | Forge Body | Forjar el cuerpo | Forjar el cuerpo | 肉体鍛錬 | 단련 | Forjar Corpo | Ковать тело | หลอมร่าง |
| 临时缓慢 | 临时缓慢 | temporary Slow | Lentitud temporal | Lentitud temporal | 一時的なスロウ | 임시 둔화 | Lentidão temporária | Временное замедление | เชื่องช้าชั่วคราว |

“临时缓慢”是隐藏的 `HextechTemporarySlowPower` 对可见 `HextechPlayerSlowPower` 的临时增减；它没有独立本地化 title，不得把原版 `SlowPower` 的机制说明套过来。原版 Slow 与模组 Slow 名称相同但机制不同，具体效果以对应描述为准。

“活力火花”“流电”“吊杀”等沿用原版名称的自定义 Power，名称优先取同语言对应原版 title，但描述必须保留模组自身机制。泰语等原包缺键时，只能明确标为模组选词，不能宣称官方译法。

中文灼烧施加语句的基线主写法为“施加”，统一为“对目标施加 X 层灼烧”。这是自创术语的局部多数约定，不覆盖原版例句的“给予 X 层易伤”。对象和层数必须保留原行为。

中文角色限定后缀统一为 `（仅战士）`、`（仅猎人）`、`（仅故障机器人）`、`（仅储君）`、`（仅亡灵契约师）`，**只写在 `hextech_relics_summary.txt` 里**，本地化 JSON 中没有也不许加；只有实际联机限定的条目加 `（仅联机出现）`，不得给通用符文仅凭标签加角色限定。

## 其余模组能力的当前名称

| 资源键 | zhs | eng | esp | spa | jpn | kor | ptb | rus | tha |
|---|---|---|---|---|---|---|---|---|---|
| `powers/HEXTECH_ATTACK_REPLAY_POWER.title` | 欺诈魔术 | Trick Magic | Magia trucada | Magia trucada | トリックマジック | 마술 트릭 | Magia de Truque | Иллюзорная магия | มายากลลวง |
| `powers/HEXTECH_BURN_POWER.title` | 灼烧 | Burn | Quemadura | Quemadura | 火傷 | 점화 | Queimadura | Ожог | เผาไหม้ |
| `powers/HEXTECH_CHEMTECH_DRAGON_SOUL_POWER.title` | 炼金龙魂 | Chemtech Dragon Soul | Alma de dragón químicotecnológico | Alma de dragón químicotecnológico | ケムテックドラゴンソウル | 화학공학 드래곤의 영혼 | Alma do Dragão Quimtec | Душа химтек-дракона | วิญญาณมังกรเคมเทค |
| `powers/HEXTECH_CLOUD_DRAGON_SOUL_POWER.title` | 云霄龙魂 | Cloud Dragon Soul | Alma de Dragón Nube | Alma de Dragón Nube | クラウドドラゴンソウル | 바람 드래곤의 영혼 | Alma do Dragão das Nuvens | Душа облачного дракона | วิญญาณมังกรเมฆา |
| `powers/HEXTECH_DRAGON_SOUL_POWER.title` | 海克斯科技龙魂 | Hextech Dragon Soul | Alma de dragón hextech | Alma de dragón hextech | ヘクステックドラゴンソウル | 마법공학 드래곤의 영혼 | Alma do Dragão Hextech | Душа хекстек-дракона | วิญญาณมังกรเฮกซ์เทค |
| `powers/HEXTECH_GALVANIC_POWER.title` | 流电 | Galvanic | Galvanismo | Galvanismo | ガルバニック | 전류 | Galvanização | Гальванизация | กัดกร่อน |
| `powers/HEXTECH_HANG_POWER.title` | 吊杀 | Hang | Ahorcar | Ahorcar | 絞首 | 교살 | Enforcar | Повешение | แขวนคอ |
| `powers/HEXTECH_INFERNAL_DRAGON_SOUL_POWER.title` | 炼狱龙魂 | Infernal Dragon Soul | Alma de Dragón Infernal | Alma de Dragón Infernal | インファーナルドラゴンソウル | 화염 드래곤의 영혼 | Alma do Dragão Infernal | Душа огненного дракона | วิญญาณมังกรนรก |
| `powers/HEXTECH_MOUNTAIN_DRAGON_SOUL_POWER.title` | 山脉龙魂 | Mountain Dragon Soul | Alma de dragón de montaña | Alma de dragón de montaña | マウンテンドラゴンソウル | 대지 드래곤의 영혼 | Alma do Dragão da Montanha | Душа горного дракона | วิญญาณมังกรภูผา |
| `powers/HEXTECH_NEXT_TURN_DAMAGE_POWER.title` | 下回合伤害 | Next Turn Damage | Daño el próximo turno | Daño el próximo turno | 次ターンダメージ | 다음 턴 피해 | Dano no Próximo Turno | Урон в следующий ход | ความเสียหายเทิร์นถัดไป |
| `powers/HEXTECH_OCEAN_DRAGON_SOUL_POWER.title` | 海洋龙魂 | Ocean Dragon Soul | Alma de dragón oceánico | Alma de dragón oceánico | オーシャンドラゴンソウル | 바다 드래곤의 영혼 | Alma do Dragão do Oceano | Душа морского дракона | วิญญาณมังกรสมุทร |
| `powers/HEXTECH_PLAYER_SLOW_POWER.title` | 缓慢 | Slow | Lento | Lento | スロウ | 둔화 | Lentidão | Замедление | Slow |
| `powers/HEXTECH_VITAL_SPARK_POWER.title` | 活力火花 | Vital Spark | Chispa vital | Chispa vital | 生命の火花 | 생명의 불꽃 | Centelha vital | Искра жизни | ประกายชีวิต |

## 更新方式

官方文本从本机 `SlayTheSpire2.app/Contents/Resources/Slay the Spire 2.pck` 的 `localization/<lang>/*.json` 现取（用本机 sts2-rich-text-tags 技能自带扫描脚本的 `read_pck_entries` / `read_entry`；该脚本不在本仓库），导出文件只放临时目录，不进仓库。游戏升级后重新提取相同键，人工裁决缺译或改名；不得把模组翻译回写成“官方”。卡牌、能力、附魔、关键词不能因中文相似而混用 title。
