# Agent 世界迭代漂移审计

## 范围与依据

冻结提交 `68832af00675ecf3388085bee379d46b3fa5947d`；Agent 主链从引入前的 `207e42c4f5962154417d7315d179bde130504a12` 比较，上一轮 tests/docs 另与 `ec2082233bf64ff29d86350aaae9e0c0ac77884b` 比较。审查 Runtime/NpcAgents、Unity/Npc、代理和上一轮续谈复现；共享工作区的美术、农田修改不在范围内。

规格来自 [Agent 世界 PRD](../AGENT_WORLD_PRD.md)、[观察与表达规格](../AGENT_LOCAL_OBSERVATION_SPEC.md)、[架构规则](../ARCHITECTURE.md)、ADR-0002、0015–0018 及 [GitHub 地图](https://github.com/qinjunw/CozyTown/issues/50)。两个独立审查分别评价标准与规格；本次审计未新运行测试或模型，既有测试结果引用冻结报告。

## Standards

发现一项文档标准偏差，未发现执行权威越界：[居民控制器](../../Assets/CozyTown/Unity/Npc/CozyTownTownLifeController.cs)自行创建 AgentWorld、MeetingBoard 和 Scheduler，而架构说明第 2、6 节仍规定唯一对象图组合入口。[ADR-0016](../adr/0016-bounded-autonomous-decisions.md)认可该控制器为实现入口，但没有说明会话装配例外。应在后续明确会话对象图与默认经济对象图的组合边界，本次调度修复不扩大到装配重构。`CharacterResourceTrading` 是窄应用协调器，未发现将完整服务集合或内部资产集合交给模型。

一项风格建议：`NpcFactSpeech` 的声明措辞与 `ActorName` 重复维护角色名映射，覆盖集合也不同。可以复用已有方法，不需要增加抽象；该问题不影响本次两个角色的续谈修复。

调度、代理仍约束实际请求、续调和纠正；取消后未返回的任务仍占用在途限制。[上一轮报告](../verification/npc-conversation-budget-2026-09-14.md)明确区分目标失败、缺陷表征通过、历史时间推断和真实模型证据。

## Spec

一项已登记的功能缺口：等待请求因旧游戏时间期限清除，预算释放后没有新事件重新派发；临界派发仍沿用旧时间。观察规格已明确由[保留预算等待中的会话轮次并重新采样请求](https://github.com/qinjunw/CozyTown/issues/83)接手，继续按该范围修复。

模型只收到复制的上下文并提出候选，宿主继续检查动作集合、身份、轮次、实际到场及资源交付。地点查询限于本人已知地点；局部投影筛选实际空间、区域和距离；会面记录只写双方记忆，保留说话者和时间。未发现将玩家自然语言触发引入后台主链、把目的地冒充当前位置或向第三人传播声明。

默认仍为 FreeText，结构化表达为显式对照；正式表达方式、普通活动期限、四人负载和会面持久化已有后继任务，没有被本轮复现暗中替代。

## 结论与后续

标准轴：1 项装配文档偏差、1 项风格建议；规格轴：1 项已登记的续谈缺口、0 项新增范围漂移。两轴均未发现需要暂停并重新确定架构方向的问题，可以领取后继调度修复；具体实现仍须先验证失败，再运行行为及场景回归。

装配偏差已登记为独立后继[明确 Agent 会话的对象装配边界](https://github.com/qinjunw/CozyTown/issues/85)，本轮未修改对象装配。
