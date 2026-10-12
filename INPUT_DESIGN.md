# 输入服务 · 设计稿与实施记录

> 目标：框架只做**输入基础设施服务**（生命周期 / 上下文 / 查询 / 后端适配），
> **不做**具体操作映射（不出现 `Jump` / `Attack` 这类游戏语义）。

## 0. TL;DR

- **契约层** `Framework.Abstractions.Input`：`IInputService` / `IInputBackend` / `InputContext` / `InputActionEvent` / `InputTriggerPhase`。**不引用任何输入包**。
- **实现层** `Framework.Adapters.Input`：`InputService`（上下文栈 + 转发）、`NullInputBackend`（默认，恒 false）、`InputSystemBackend`、`MockInputService`、`InputServiceModule`（Order=10）。
- **项目层** `Game.Inputs`：`GameActions`（动作/上下文常量）、`GameInputModule`（Order=100 覆盖后端）、`GameInputAssetStepProvider`（把 `InputActionAsset` 接上）、`InputDemo`（事件驱动演示）、`GameInputActions.inputactions`。
- **UI 自动阻断**：面板配置填 `inputContextId = UI` → 打开自动 Push、关闭/销毁自动 Pop（`UIService` 按打开集合自愈推导，不会泄漏）。
- **不新增程序集**：契约沿用 `Abstractions`，实现沿用 `Adapters`（两个 asmdef 各加一条 `Unity.InputSystem` 引用）。
- **不需要 tick**：Input System 默认在引擎逐帧更新中自行刷新，且其更新插在逻辑 `Update` 之前 → 业务读取天然安全。框架既有「零 tick」架构保持不变。
- 本项目 3 处与「原方案」的偏差见 §12。

## 1. 边界

| 框架负责 | 框架**不**负责 |
|---|---|
| 输入服务的生命周期（随容器创建/释放） | `Jump` / `Attack` / `Build` 等操作语义 |
| 上下文栈（Push/Pop/优先级/引用计数） | 某个游戏类型的操作映射表 |
| 基础查询（`IsPressed` / `GetAxis` / `GetVector2`） | 连招、输入缓冲、重绑定 UI、网络预测、录像回放 |
| 后端适配（Input System / 空实现 / 测试桩） | 按键提示图标、UI 自动 Push/Pop |
| `playerId` 透传（单玩家恒 0） | 设备分配策略（`PlayerInput` / `InputUser`） |

后端策略（已定）：**只做 Input System 后端**；运行时**不**动态切换后端（换后端 = 换注册的 `IInputBackend`）。

## 2. 接口

```csharp
public interface IInputService : IDisposable          // 刻意不继承 IService / ITickable
{
    void PushContext(string contextId, int priority = 0);
    void PopContext(string contextId);
    bool IsContextActive(string contextId);
    IReadOnlyList<InputContext> ActiveContexts { get; }

    bool IsPressed(string actionId, int playerId = 0);
    bool WasPressedThisFrame(string actionId, int playerId = 0);
    bool WasReleasedThisFrame(string actionId, int playerId = 0);
    float GetAxis(string actionId, int playerId = 0);
    Vector2 GetVector2(string actionId, int playerId = 0);

    event Action<InputActionEvent> ActionTriggered;
}

public interface IInputBackend : IDisposable
{
    void Initialize();
    void ApplyContexts(IReadOnlyList<InputContext> activeContexts);  // 已由服务筛好优先级；空列表 = 不限制
    void Tick(float deltaTime);                                      // 可选：手动驱动钩子（框架不调用）

    bool ReadButton(string actionId, int playerId = 0);
    bool ReadButtonDown(string actionId, int playerId = 0);          // ← 原方案缺这两个，服务无法实现边缘检测
    bool ReadButtonUp(string actionId, int playerId = 0);
    float ReadAxis(string actionId, int playerId = 0);
    Vector2 ReadVector2(string actionId, int playerId = 0);

    event Action<InputActionEvent> ActionTriggered;
}
```

`InputContext`（`readonly struct`）：`ContextId` + `Priority`。
`InputActionEvent`（`readonly struct`）：`ActionId` / `PlayerId` / `Phase` / `Scalar` / `Vector`。
`InputTriggerPhase`：`Started` / `Performed` / `Canceled` —— **框架自有枚举**，避免把 `InputActionPhase` 泄进契约层。

## 3. 上下文语义（必须记住的一条）

> **有效上下文 = 栈中优先级最高的那一批**（同优先级并存 = 并集）。

- Push `Gameplay(0)` → 有效集 `{Gameplay}`
- 再 Push `UI(100)` → 有效集 `{UI}`（Gameplay 被**独占**压制 → 玩法输入被阻断）
- Pop `UI` → 自动回落到 `{Gameplay}`
- 同名重复 Push 采用**引用计数**：Push 两次需 Pop 两次
- **空栈 = 不限制**（后端启用全部输入分组），这样不用上下文的项目行为不变
- 有效集**变化时**才会通知后端（避免 Enable/Disable 抖动）
- `PopContext` 对未推入的上下文**记警告并忽略**（不抛异常）

## 4. 分层与程序集

| 层 | 命名空间 | 程序集 | 引用 |
|---|---|---|---|
| 契约 | `Framework.Abstractions.Input` | **Abstractions** | 仅 `UnityEngine`（`Vector2`）；**无 InputSystem** |
| 实现 | `Framework.Adapters.Input` | **Adapters** | + `Unity.InputSystem`（新增） |
| 项目 | `Game.Inputs` | **Game** | + `Unity.InputSystem`（新增，承载 `InputActionAsset`） |

不新增程序集的原因：`ServiceModuleRegistry` 的默认扫描前缀是
`Abstractions / Adapters / Bootstrap / Game / Assembly-CSharp`，
新程序集名若不带这些前缀就**不会被自动发现**。沿用现有两个程序集最省事。

> 命名空间刻意用 `Game.Inputs` 而非 `Game.Input`：后者会与 `UnityEngine.Input` 在类型解析上打架。

## 5. 后端

### 5.1 `NullInputBackend`（框架默认）

查询恒 `false` / `0`，`Initialize` 时**告警一次**，提示注册真实后端。
作用：保证 `IInputService` **永远可解析**（玩法代码无需判空）。
覆盖注册后它**不会被构造** → 不会产生告警。

### 5.2 `InputSystemBackend`

映射约定：

| 框架概念 | Input System 概念 |
|---|---|
| `contextId` | `InputActionMap` 名（默认同名，可用 `contextMap` 重命名） |
| `actionId` | `InputAction` 名（在**当前启用**的 map 中查找） |

要点：
- 绑定资源通过 **`Attach(asset, handle)`** 提供（可在加载管线里异步载入后再接上），所以「暂时没有资产」也能安全构造/初始化。
- `handle`（Addressables 句柄）的所有权**交给后端**，由后端 `Dispose` 时释放 → 不会泄漏。
- `ApplyContexts` 会 Enable/Disable map，并**重建查找表**：被阻断的动作**根本查不到** → 查询天然返回默认值。
- **值类型路由**：按 `action.activeValueType` 选择读取通道（`float`/`double` → 标量；`Vector2`/`Vector3` → 向量）。
  ⚠️ 直接用 `ReadValue<float>()` 读 `Vector2` 动作会**抛异常**（详见 §10）。
- 诊断属性：`HasAsset` / `ActiveActionCount` / `EnabledMapNames`（无需真实设备即可断言上下文阻断是否生效）。
- 单玩家：`playerId` 统一透传为 0。

## 6. 接入 4 步（新项目 / 新动作）

1. **绑定资源**：建 `*.inputactions`（本项目 `Assets/Scripts/Game/Inputs/GameInputActions.inputactions`），
   建 ActionMap `Gameplay` / `UI`，并把资产**标记为 Addressable**（本项目地址 `Input/GameInputActions`）。
2. **常量**：在 `Game.Inputs.GameActions` 加 `actionId` / `contextId` 常量（**改框架？不用**）。
3. **注册模块**：`GameInputModule : IGlobalServiceModule`，`Order > 10`，`registry.Register<IInputBackend>(new InputSystemBackend())`
   —— 后注册者覆盖框架默认的空后端。
4. **接入资产**：`GameInputAssetStepProvider : IStartupLoadStepProvider` 把 `AttachInputActionsStep` 加进启动管线，
   在配置加载后异步载入资产并 `Attach`（复用框架的进度/超时兜底）。

> **新增一个动作 = ①资产里加同名 Action ②`GameActions` 加常量**。框架零改动。

## 7. UI 集成（已支持**自动** Push/Pop）

框架仍然不知道「UI 打开」是什么语义，但 `UIService` 会**按面板配置**自动维护输入上下文：

- 面板在配置里声明 `inputContextId`（本项目 `DemoPanel` → `UI`，优先级 100）即参与。
- 打开面板 → 自动 `PushContext`；关闭 / `CloseAll` / **面板被场景销毁** → 自动 `PopContext`。
- 多个面板声明同一上下文时，只在 **0→1 / 1→0** 时真正 Push / Pop（不会重复推）。
- 未声明 `inputContextId` 的面板完全不碰输入（HUD / Toast 这类不该阻断的天然安全）。

实现要点（值得记住的设计）：

- **按「当前已打开面板集合」推导期望上下文**，而不是增减计数器 → 任何一条路径漏调，
  下一次对齐都会**自愈**，不可能把上下文永久泄在栈上（那会导致玩法输入再也不响应）。
- `UIService` 的 `IInputService` 是**可选**构造参数：拿不到就整体关闭该能力，不产生硬依赖。
- 同步点四处：`EnsureOpen` 之后 / `CloseCore` / `CloseAll` / `DisposeScenePanels`。

仍可手动控制（例如不由面板触发的暂停菜单）：

```csharp
input.PushContext(GameActions.Contexts.UI, GameActions.Priorities.UI);
input.PopContext(GameActions.Contexts.UI);
```

> 手动与自动可以**共存**：`InputService` 对同名上下文是**引用计数**，各自 Pop 各自的，互不影响。

## 8. 更新顺序：为什么不需要 tick

- Input System 默认 `updateMode = ProcessEventsInDynamicUpdate`，其更新被插入 PlayerLoop 的 `Update` **之前**
  → 业务在自己的 `Update` / 协程里读取是**安全的**。
- 上下文栈由 `Push/Pop` 驱动，与帧无关。
- Legacy 输入同样是引擎逐帧。
- 因此 `IInputService` **不继承 tick 契约**；`IInputBackend.Tick` 仅保留为「项目若改用 `ProcessEventsManually` 时才需要实现」的钩子。

## 9. 测试

`Assets/Scripts/Tests/InputServiceTests.cs`（17 例，纯同步）：
上下文 Push/Pop、优先级独占、同级并集、引用计数、空 id/未知 Pop 告警、
`ApplyContexts` 仅在有效集变化时调用、有效集内容正确、上下文阻断端到端、
查询转发、事件转发（含**单订阅者异常隔离**）、`Dispose` 幂等、
`MockInputService` 的按钮/边缘标记/轴/向量/事件/上下文。

`Assets/Scripts/Tests/InputSystemBackendTests.cs`（8 例）：用内存 `InputActionAsset`（`InputActionAsset.FromJson`），
覆盖 Attach/HasAsset、**值类型路由判据**、Button 查询、未知动作、上下文阻断（`ActiveActionCount` / `EnabledMapNames`）、
`contextId → map` 重命名、`Attach(null)` 安全、`Dispose` 幂等。

`Assets/Scripts/Tests/UIServiceTests.cs`（+6 例）：UI 上下文自动 Push/Pop ——
打开即推 / 关闭即弹 / 未声明不参与 / 重复打开只推一次 / `CloseAll` / **面板被场景销毁时自动弹掉** / 无输入服务不抛异常。

`Assets/Scripts/Tests/PlayMode/`（新程序集 `Tests.PlayMode`，3 例）：**真实输入**回归 ——
`ReadVector2` 读到真实摇杆值、`ReadAxis` 对向量动作**不抛异常**（下方 §10 那个 bug 的真回归）、
上下文阻断 → 归零 → 取消阻断 → 恢复、按钮按下触发 `ActionTriggered`。

> ⚠️ `Tests.asmdef` 因此新增了对 `Unity.InputSystem` 的引用；`Tests.PlayMode.asmdef` 是**非 Editor-only** 的第二个测试程序集。
> ⚠️ 本项目 UTF 版本**不认 `async Task` 测试方法** → EditMode 一律同步 `[Test]`。

### 模拟输入的可行姿势（踩过坑，重要）

- 只有把输入系统切到 `ProcessEventsManually`，再 `QueueStateEvent` + `InputSystem.Update()`，模拟输入才会真正作用到 `ReadValue`。
  **默认动态更新模式下** `QueueStateEvent` 对 `ReadValue` **不生效**（原生后端会互相覆盖，设备态读回 0）。
- 键盘/鼠标的模拟尤其不可靠（会与原生后端打架）；**合成手柄**最可靠。
- 🔴 `GamepadButton` 是**顺序枚举**（`South == 6`，**不是**位标志）→ 必须用 `GamepadState.WithButton(...)`。
  对比：`MouseState.buttons` 才是位域（`1` = 左键）。我在测试里先用 `(ushort)GamepadButton.South` 写错了，用例当场失败。

## 10. 验证记录（2026-10-12，Play 实测）

启动链路（零错误零警告）：

```
[Modules] 已注册全局模块 InputServiceModule（Order=10）
[Modules] 已注册全局模块 GameInputModule（Order=100）      ← 覆盖生效
[Bootstrap] 接入输入绑定…
[Load] 接入输入绑定：已接入 Input/GameInputActions
[InputDemo] 演示就绪：…
```

端到端（用 `InputSystem` 队列事件 + `ProcessEventsManually` + `InputSystem.Update()` 注入真实输入）：

| 场景 | 结果 |
|---|---|
| `Gameplay` 上下文 + 左摇杆上推 | `GetVector2("Move") = (0.00, 1.00)` ✅ |
| Push `UI` 后同一查询 | `(0.00, 0.00)` ✅ 阻断 |
| Pop `UI` 后再 `Update` | `(0.00, 1.00)` ✅ 恢复 |
| UI 激活时**按住鼠标左键** | `IsPressed("Attack") = False` ✅ |
| Pop `UI` 后重新按左键 | `IsPressed("Attack") = True` ✅ |
| 按 Escape（Gameplay 的 Pause） | 事件 `Pause[P0] Started/Performed/Canceled`，`scalar=1` ✅ |
| Escape 切换 UI / 再按切回 | 上下文 `Gameplay → UI → Gameplay` ✅ |

### UI 上下文自动 Push/Pop（B1，Play 实测）

配置只改一处：`DemoPanelConfig.inputContextId = "UI"`（优先级 100）。全程**没有手写任何 PushContext**：

| 步骤 | `DemoPanel 已打开` | `UI 上下文` | `GetVector2("Move")`（左摇杆持续上推） |
|---|---|---|---|
| 基线 | False | False | `(0.00, 1.00)` ✅ |
| `OpenAsync<DemoPanel>()` 后 | **True** | **True** | `(0.00, 0.00)` ✅ 自动阻断 |
| `Close(DemoPanel)` 后 | False | **False** | `(0.00, 1.00)` ✅ 自动恢复 |

关闭后 `ActiveContexts = Gameplay(0);` —— 直接回落到 `InputDemo` 推入的玩法上下文。

### 🔴 Play 实测抓到的真 bug（EditMode 抓不到）

对 `Vector2` 动作调用 `GetAxis`（即 `InputAction.ReadValue<float>()`）会抛：

```
InvalidOperationException: Cannot read value of type 'Single' from composite
'UnityEngine.InputSystem.Composites.Vector2Composite' bound to action 'Gameplay/Move'
(composite is a 'Int32' with value type 'Vector2')
```

修法：先看 `action.activeValueType`，按类型选读取通道（`IsScalarValueType` / `IsVector2ValueType`）。

**诚实标注**：该异常**只在 Play 下复现**。EditMode 下动作未绑定真实设备时
`activeValueType` 为 `null`、`ReadValue<T>()` 直接返回 0 而不做类型校验
（实测：EditMode 里即使 `AddDevice` + `ProcessEventsManually` + `InputSystem.Update()` 也不产生设备状态）。
因此**没有**写「`ReadAxis` 不抛异常」这种在 EditMode 下永不失败的假测试；
改为把**类型路由判据**做成可单测的真判据（`ValueTypeRouting_Vector2MustNotBeTreatedAsScalar`）。

后来补上了 **`Tests.PlayMode`** 程序集，把这个 bug 的真回归补上：
`InputSystemBackendPlayModeTests.ReadVector2_ReturnsDeviceValue_AndReadAxisDoesNotThrow`
（用合成手柄 + `ProcessEventsManually` 让动作真的“活”起来，旧实现会当场抛异常）。

## 11. 反模式清单

- ❌ 框架里写 `Jump()` / `Attack()` 这类操作语义
- ❌ `Abstractions` 引用 `UnityEngine.InputSystem`（当前只有 `Vector2`，属引擎内核）
- ❌ 玩法代码直接读 `InputAction` / `Input.GetButton` / `Input.GetAxis`（应只依赖 `IInputService`）
- ❌ 为了兼容旧输入而阉割 Input System 特性
- ❌ 运行时动态切换后端
- ❌ 用 `async Task` 写 EditMode 测试（本项目 UTF 不支持）

## 12. 与原方案的偏差（重要）

| 原方案 | 实际落地 | 原因 |
|---|---|---|
| `IInputService : IService, ITickable, IDisposable` | 只 `IDisposable` | 框架**没有** `IService` 接口；**没有** tick 机制，且不需要（§8） |
| 新建 `Framework.Input.Abstractions` / `.InputSystem` / `.Legacy` 程序集 | 契约进 `Abstractions`、实现进 `Adapters` | 默认扫描前缀不含新程序集名 → 不会被自动发现；且「目录=程序集」现状是短名 |
| `IInputBackend` 只有 `ReadButton` | 增加 `ReadButtonDown` / `ReadButtonUp` | 否则服务无法实现 `WasPressedThisFrame` |
| `InputActionEvent` 含 `InputActionPhase` | 改用框架自有 `InputTriggerPhase` | 否则 InputSystem 类型泄进契约层 |
| 阶段 4 做「多玩家设备分配」 | 只做 `playerId` 透传（恒 0） | 复杂度最高，留待后续；接口已留口子 |
| 阶段 7「输入服务 Tick → 逻辑」 | 无 tick，靠 InputSystem 自身更新顺序 | 见 §8 |
| 阶段 7 之后才做「UI 集成」 | 已完成：`PanelDefinition.InputContextId` + `UIService` 自动 Push/Pop | 配置驱动，无需改框架 |
| 阶段 8 PlayMode 测试程序集 | 已完成：新增 `Tests.PlayMode`（3 例） | 那个 Play-only 的 bug 必须靠它回归 |

## 13. 未做 / 后续扩展

以下都**不进**核心输入服务：重绑定服务、输入缓冲、连招窗口、录像回放、网络输入预测、按键提示图标切换。

本轮明确未做：Legacy 后端、多玩家设备分配、运行时后端切换。

## 14. 文件清单

**新增（契约）** `Assets/Scripts/Framework/Abstractions/Input/`
`IInputService.cs` · `IInputBackend.cs` · `InputContext.cs` · `InputActionEvent.cs` · `InputTriggerPhase.cs`

**新增（实现）** `Assets/Scripts/Framework/Adapters/Input/`
`InputService.cs` · `NullInputBackend.cs` · `InputSystemBackend.cs` · `MockInputService.cs` · `InputServiceModule.cs`

**新增（项目层）** `Assets/Scripts/Game/Inputs/`
`GameActions.cs` · `GameInputModule.cs` · `GameInputAssetStepProvider.cs` · `InputDemo.cs` · `GameInputActions.inputactions`

**新增（测试）** `Assets/Scripts/Tests/`
`InputServiceTests.cs` · `InputSystemBackendTests.cs`

**新增（测试）** `Assets/Scripts/Tests/PlayMode/`
`Tests.PlayMode.asmdef` · `InputSystemBackendPlayModeTests.cs`

**修改** `Adapters.asmdef` · `Game.asmdef` · `Tests.asmdef`（各加 `Unity.InputSystem`）·
`UIService.cs` · `PanelDefinition.cs` · `UIPanelConfigSO.cs` · `UIPanelSetupStep.cs`（B1 自动上下文）·
`UIServiceTests.cs` · `DemoPanelConfig.asset` · `README.md` · `PROJECT_GUIDE.md`

## 15. 与既有决策的关系

`UI_POPUP_DESIGN.md` §6 写的是「**弹窗系统**不含任何输入概念：不定义 `IBackInput`、不加 `GameRoot.Update` 轮询」。
那条决策**只针对弹窗的返回键语义**，与本次「独立的输入基础设施服务」不冲突——
本模块同样**不加** `GameRoot.Update` 轮询（§8），并且弹窗的返回键仍由项目层自行绑定到 `IInputService`。
