# ADR-0005：隔离 Unity 适配层并采用窄接口注入

- 状态：已接受
- 日期：2026-08-28

## 背景

`CozyTown.Runtime` 需要保持可在不加载场景的条件下测试。M1 同时需要接入 Unity 生命周期、Input System、Physics2D、交互探测和调试 HUD。如果场景组件取得完整 `CozyTownServices`，任意 NPC、商店或交互对象都可以直接修改钱包、背包、时间、生产和存档状态，从而绕过应用协调器的事务边界。

## 评估选项

### 选项 A：在每个场景组件中查找或解析完整服务集合

装配代码较少，但服务依赖不可见，组件可以越权调用无关模块；场景对象的创建顺序和查找结果也会影响行为。

### 选项 B：Bootstrap 公开完整服务集合，由组件自行取用

避免了全局静态单例，但仍形成场景级服务定位器。代码评审无法从组件构造或绑定入口判断其实际写权限。

### 选项 C：独立 Unity 适配程序集，Bootstrap 私有持有对象图并推送窄接口

Runtime 不引用 Unity。Bootstrap 是组合边界，只向 presenter 或控制器注入已确认用例需要的接口；通用交互上下文只携带 Actor 等表现信息，具体交互适配器再显式接收对应应用用例。

## 决策

采用选项 C。

- `CozyTown.Runtime` 设置 `noEngineReferences: true`，包含领域规则、应用协调器、默认内容和组合根。
- `CozyTown.Unity` 单向引用 Runtime 与 Input System；`CozyTown.Unity.Editor` 仅包含 Editor 菜单和装配工具。
- `CozyTownBootstrap` 可以通过工厂创建 `CozyTownServices`，但完整服务集合保持私有，不提供公开 `Services` 属性或 `Get<T>()`。
- Bootstrap 通过显式绑定把 `ITimeService`、`IWallet` 等窄接口推送给组件。新增交互用例时，为该用例增加专用适配器或应用接口，不扩展通用交互上下文为服务袋。
- `InteractionContext` 只携带 Actor；`IInteractable` 不得借此取得钱包、背包、时间、生产或存档写接口。
- 禁止使用静态 `Instance`、`FindObjectOfType`、`DontDestroyOnLoad` 服务容器或字符串服务解析。

## 验证规则

- Runtime 程序集不得引用 UnityEngine。
- Unity 适配测试检查交互上下文没有 `Services` 属性，并覆盖可独立验证的移动和 HUD 映射。
- Reviewer 搜索完整服务集合泄漏、全局服务定位器和场景查找 API。
- 场景生命周期、输入启停、碰撞目标选择和一次按键一次交互在 M2 增加 PlayMode 或组件测试。

## 后果

正面结果：

- 领域和应用测试不依赖 Unity 场景。
- 场景组件的依赖与写权限在绑定入口可见。
- AI 对话、NPC 表现和通用交互对象不能通过共享服务袋越权修改确定性状态。

成本和风险：

- 每种业务交互需要专用适配器或窄用例接口，装配代码会增加。
- Bootstrap 的序列化绑定必须通过 Unity 编译和场景测试验证；EditMode 纯函数测试不能发现全部 Awake 顺序或缺失引用问题。
- 当前开发场景生成器只是装配骨架，不代表 M2 的可见移动与交互验收已经完成。

## 复审条件

当场景装配数量导致人工绑定错误频发，或需要跨场景持久对象图时，评估类型化 installer、子场景入口或生成式绑定。复审不得以重新公开完整服务集合或引入全局服务定位器作为默认方案。
