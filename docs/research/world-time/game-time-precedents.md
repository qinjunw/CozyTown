# 农场生活游戏的走时、睡眠与日常规则调研

## 1. 范围与证据口径

资料核验日期：2026-09-05。本报告核验玩家可观察的产品规则，不修改 CozyTown 的时间设计或产品代码。

核验三部具体作品：《Stardew Valley》的 1.4／1.6 官方更新说明及当前官方 Wiki；《STORY OF SEASONS》（Nintendo 3DS，无副标题、舞台为 Oak Tree Town）的 Nintendo Quick Guide；《STORY OF SEASONS: Grand Bazaar》重制版的 PS5 官方 Web Manual。Grand Bazaar 重制版于 2025 年开始发行，PS5／Xbox Series X|S 版于 2026-05-28 发行；本报告不混用旧掌机版规则。[Nintendo 3DS 官方 Quick Guide](https://www.nintendo.com/eu/media/downloads/games_8/quick_start_guide/QuickStartGuide_3DS_StoryOfSeasons_EN.pdf)、[Grand Bazaar 官方产品信息](https://www.storyofseasons.com/grandbazaar/characters/samir/)

证据分为两类：

- **一手发布**：开发者发布的更新说明、发行商官网和 Nintendo 托管的官方手册。
- **公开玩法说明**：星露谷官方 Wiki 的社区维护条目。它们用于说明玩家规则，不当作开发者源码或内部实现证明；涉及反编译方法名的引用未用于本报告。

除明确标注的官方 Wiki 玩法说明外，未取得一手支持的细节列为未知。没有用普通攻略、玩家讨论或其他版本的规则补齐缺口。

## 2. 午夜、强制结束与睡眠

| 核验问题 | Stardew Valley | Grand Bazaar 重制版（PS5 官方手册） |
| --- | --- | --- |
| 午夜是否必须停止活动 | 午夜仍可活动。1.6 官方说明把冒险者公会营业延长至 02:00，并分别规定午夜后停止播放音乐，证明午夜与停止营业是不同规则。[1.6 官方更新说明](https://www.stardewvalley.net/stardew-valley-1-6-update-full-changelog/) | 手册把强制结束设在 05:00；因此按其规则，午夜不是当天工作的强制截止点。这是从手册阈值得出的产品层推论。[Farm Life：Getting Your Rest](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 是否有凌晨强制结束 | 02:00 倒下。1.4 官方说明明确修复了到 02:00 不退出小游戏，以及 02:00 后仍能钓鱼或蓄力工具的漏洞。[1.4 官方更新说明](https://www.stardewvalley.net/stardew-valley-1-4-update-full-changelog/) | 05:00 倒下并结束当天工作；睡眠不足会减少次晨恢复的体力。[Farm Life：Getting Your Rest](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 上床后到什么时候 | 官方 Wiki 描述为选择上床、确认结束当天，次日 06:00 恢复活动；可提前结束当天。[官方 Wiki：Day Cycle](https://wiki.stardewvalley.net/Day_Cycle) | 可以随时在自家床上睡觉，恢复体力并推进至下一天。该手册没有给出醒来时刻的完整计算表。[Farm Life：Your Bed](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 是否能直接选择睡眠时长 | 上述说明支持“选择入睡时刻、睡至次日固定时刻”，没有提供“睡 1／4／8 小时”的选择流程。不能把提前上床称为任意睡眠时长选择。[官方 Wiki：Day Cycle](https://wiki.stardewvalley.net/Day_Cycle) | 手册只说明睡至下一天；本轮未找到选择任意时长或任意醒来时刻的一手证据，不能据此断言所有版本均没有这种功能。[Farm Life：Your Bed](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 现实时间与游戏时间的比例 | 官方 Wiki 给出的普通区域比例为现实 7 秒推进游戏 10 分钟；骷髅洞穴另有比例，不能概括为所有场景统一倍率。[官方 Wiki：Day Cycle](https://wiki.stardewvalley.net/Day_Cycle) | 本轮所核验的官方手册没有给出明确倍率；未知。 |
| 暂停规则是否所有模式相同 | 开发者的多人模式说明明确：单人打开菜单会暂停，多人打开菜单不会自动暂停，需由主机使用暂停命令。该文对应 1.3 多人模式发布阶段。[开发者多人模式说明](https://www.stardewvalley.net/stardew-valley-v1-3-beta/) | 本轮未找到足以核验所有菜单、对话及失焦状态的官方条款；未知。 |

### 牧场物语／Story of Seasons 系列内的版本对照

| 问题 | STORY OF SEASONS（Nintendo 3DS，Oak Tree Town） | Grand Bazaar 重制版（PS5） |
| --- | --- | --- |
| 入睡与醒来 | 次日醒来；恢复体力和醒来时刻都取决于入睡时刻。[发行商：Bed](https://www.storyofseasons.com/sos/farm2.html) | 推进至下一天，手册未公开完整醒来时刻公式。[官方手册：Your Bed](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 凌晨强制结束 | 所查指南未给出时钟阈值；只确认体力归零会倒下并于次日醒来。不能借用另一作品的 05:00。[Nintendo 指南，第 6 节](https://www.nintendo.com/eu/media/downloads/games_8/quick_start_guide/QuickStartGuide_3DS_StoryOfSeasons_EN.pdf) | 05:00 强制倒下；不是商店库存刷新条款。[官方手册：Getting Your Rest](https://www.storyofseasons.com/grandbazaar/manuals/ps5/01/) |
| 能否选择任意睡眠时长 | 公开床铺说明是“何时睡影响何时醒”，未说明小时数选择器。[发行商：Bed](https://www.storyofseasons.com/sos/farm2.html) | 本轮未取得任意时长或任意醒来时刻选择的一手证据。 |

因此，牧场物语系列也不能统一概括为“固定早晨醒来”或“任意睡几个小时”。上述差异是已公开规则与证据覆盖面的差异，不代表其他版本必然相同或相反。

## 3. 商店、出货与日更新

星露谷的官方 Wiki 区分两条出售路径：直接卖给商人立即收款；放入出货箱则在睡觉后、下一天获得款项。它描述的是玩家可见的交付与到账，不证明引擎在某个凌晨分钟统一提交所有经济状态。[官方 Wiki：Shipping](https://wiki.stardewvalley.net/Shipping)

星露谷 1.4 官方说明还分别记录了商人有限库存的同步、重新打开商店不应补回限量商品，以及农场对象漏做每日更新导致作物成熟／换季异常。这些是库存状态和每日处理确实存在的证据；说明没有公开所有商店统一刷新时刻或全世界的事务提交顺序。[1.4 官方更新说明](https://www.stardewvalley.net/stardew-valley-1-4-update-full-changelog/)

Grand Bazaar 官方手册区分了以下经济活动与时点：

- 镇上的各商店有各自营业时间；Miguel's Mercantile 支持日常买卖，Café Madeleine 提供餐饮。手册没有列出每店的完整小时表。[Zephyr Town：Shops](https://www.storyofseasons.com/grandbazaar/manuals/ps5/02/index.html)
- 集市每周六举行，分上午和下午两班；玩家摆出库存，按顾客请求完成出售。每班结束展示销售汇总，评议机构在次晨发来评价，达标后提高集市等级。[The Bazaar：Bazaar Days／Bazaar Sales](https://www.storyofseasons.com/grandbazaar/manuals/ps5/03/index.html)

Nintendo 3DS《STORY OF SEASONS》的指南则说明：在 Trade Depot 向贸易伙伴出货农作物和畜产品赚钱，也可在商人摊位、杂货店和木工店购买物品。该指南未交代精确到账时刻或每日补货时刻，不能套用星露谷的“出货箱睡后结算”规则。[Nintendo 指南，第 3、6 节](https://www.nintendo.com/eu/media/downloads/games_8/quick_start_guide/QuickStartGuide_3DS_StoryOfSeasons_EN.pdf)

这些来源足以区分营业时段、成交、销售汇总和次晨评议。它们没有证明 Grand Bazaar 把全部库存、农业、畜牧和 NPC 都放到 05:00 的同一刷新操作中；05:00 在手册中的明确含义是玩家强制倒下。

## 4. NPC 个人日程能确认到什么程度

星露谷的个人日程有开发者直接发布的证据。1.6 官方说明列出修复的具体安排：Lewis 在冬季周日到图书馆；Maru 和 Penny 在夏季周日一起活动；Maru 在夏季周一修理东西；部分拜访还随与玩家的好感变化。这支持“按角色、季节、星期和关系条件选择安排”，但不公开完整逐分钟表或调度器结构。[1.6 官方更新说明](https://www.stardewvalley.net/stardew-valley-1-6-update-full-changelog/)

Grand Bazaar 本轮核验的官方城镇手册和角色页说明了人物身份、交流及各店营业时间，没有给出可逐项验证的个人全天路线表。例如 Samir 的官方页介绍其来访身份，但未列出何时离店、上山和回家。因此其完整个人日程、雨天替代表和婚后日程在本报告中仍为未知，不用社区攻略中的时刻补作一手结论。[官方角色页：Samir](https://www.storyofseasons.com/grandbazaar/characters/samir/)、[官方城镇手册](https://www.storyofseasons.com/grandbazaar/manuals/ps5/02/index.html)

Nintendo 3DS《STORY OF SEASONS》的指南说明地图能查看其他人物所在位置，但未给出个人全天日程表。地图位置可见不等于公开了日程选择条件；该作的个人时刻表及异常天气安排仍未核验。[Nintendo 指南，第 3 节](https://www.nintendo.com/eu/media/downloads/games_8/quick_start_guide/QuickStartGuide_3DS_StoryOfSeasons_EN.pdf)

## 5. 可用于设计讨论的边界

以下是依据上述证据整理的设计问题，不是对这些游戏内部架构的还原，也不表示 CozyTown 已采纳：

1. **分别定义午夜、工作日结束、玩家醒来和每日处理边界。** 已核验作品给出了不同的强制结束阈值；从午夜仍可活动，推不出日历或内部“日编号”何时改变。
2. **把“何时入睡”“醒来时刻”“睡眠时长”分别写清。** 固定次晨醒来、按入睡时刻算醒来时间、让玩家选择小时数，是三个不同产品规则。若 CozyTown 选择第三种，应把它写成自身需求，不称为已核验作品的共同做法。
3. **睡眠跳时需要分别定义各模块的经过时间。** 星露谷 1.4 官方说明披露过机器夜间折算：02:00～06:00 每小时按 100 分钟处理，其余睡眠时段按 60 分钟。这是开发者公开的特定规则，说明直接改变钟面并不能替代生产推进规则；它不证明其他游戏采用相同算法。[1.4 官方更新说明](https://www.stardewvalley.net/stardew-valley-1-4-update-full-changelog/)
4. **个人日程与营业／结算规则需要明确各自条件。** 一个人按季节或星期改目的地，不足以推出商店库存何时补货；早晨展示新结果，也不足以证明此前完整逐分钟模拟了所有 NPC。

本轮没有核验到“以上作品普遍允许任意睡眠时长”或“每天固定 05:00 原子刷新整个世界”的公开证据。要采用这两项规则，需要由 CozyTown 单独定义并验收；未知不等于证明其他作品没有该能力。
