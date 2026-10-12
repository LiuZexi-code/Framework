# UI 弹窗系统设计稿（方案 3）

> 状态：**已定稿待实施**（2026-10-12）。所有选项均由作者拍板。
> 关联：`PROJECT_GUIDE.md` §5.9（UI 系统）、`README.md` §2.8、`UIService` / `PanelDefinition` / `UIRootCreator`。

---

## 1. 目标与范围

在现有 `IUIService`（配置驱动注册 → Addressables 异步加载 → 分层挂载 → 生命周期）之上，补齐三件事：

| # | 能力 | 要解决的问题 |
|---|---|---|
| 1 | **弹窗栈** | 现在 `Close<T>()` 只 `Hide()`，**不会恢复下层**；无法表达「设置页 → 关闭 → 回主菜单」 |
| 2 | **模态链** | 弹窗压弹窗时，下层不该还能点 |
| 3 | **跨层/层内排序** | 现在同一层内绘制顺序 = 创建顺序（`PanelFactory` 的 `SetParent` 顺序），**重新打开旧面板不会置顶**，也无法表达「A 永远压 B」 |

**非目标（本期不做）**：窗口拖拽/最小化、多显示器、动画过渡系统、输入绑定（见 §6）。

---

## 2. 决策表（拍板结果）

| 轴 | 取值 | 说明 |
|---|---|---|
| ① 栈作用域 | **(a) 全局单栈** | 栈项携带 `layer/priority/seq/isModal`，既能算「跨层最顶层」，也能按层批量操作 |
| ② 栈语义 | **(c) 栈 + 模态** | `Push` = 显示 + 入栈；`Pop` = 隐藏栈顶 + **恢复显示下层**（下层用「隐藏保活」而非关闭） |
| ③ 排序机制 | **(c) 每面板 `Canvas.overrideSorting`** | 排序与 Transform 解耦 → 可跨层；`order = LayerBase + Priority` |
| ④ 排序依据 | **(c) `(Priority, 打开序 seq)`** | 注册给默认值，`Push` 时可覆盖；同优先级「后开的在上」 |
| ⑤ 场景切换 | **(c) 按场景归属分别处理** | `SurvivesScene(entering)` 判定；不禁止打开非全局弹窗，切换时静默剔除 |
| ⑥ 异步竞态 | **(b) 串行队列** | 保证「调用顺序 = 栈顺序」；队列项**可取消** + 超时兜底 |

补充拍板：

| # | 决策 |
|---|---|
| 模态粒度 | **per panel `IsModal`**（不是「非栈顶一律模态」——Toast 之类天生不该挡点击） |
| 点遮罩 | **不**默认关闭弹窗（由业务决定） |
| `Pop` 恢复下层 | **隐藏保活**（入栈时下层只 `Hide`，不销毁） |
| 切换中打开非全局弹窗 | **不禁止**，`DisposeScenePanels` 时静默剔除并重算模态 |
| 返回键 | **链 A：框架不含任何输入概念**（详见 §6） |

---

## 3. 数据模型

```csharp
// Adapters 内部
private sealed class PopupEntry
{
    public IUIPanelLifecycle Panel;
    public PanelDefinition Definition;
    public PanelLayer Layer;
    public int Priority;
    public long Seq;          // 单调递增的打开序号（tie-break）
    public bool IsModal;
    public int Order;         // 最终 sortingOrder = LayerBase(Layer) + Priority 之和（按 seq 微调）
}
```

排序空间（`Canvas.sortingOrder`，需落在 int16 内）：

```
LayerBase(layer) = 1000 + (int)layer * 1000          // 1000..6000，保证在场景 Canvas(0) 之上
panelOrder       = LayerBase(layer) + 1 + ClampPriority(Priority)   // Priority 夹到 [-499, 498]
modalBlocker     = 栈顶 modal 的 order - 1            // 夹在「被遮面板」与「栈顶 modal」之间
```

> **两条「静默失效」规则（都已实施）**：
>
> **① 嵌套 Canvas 必须先写 `overrideSorting = true`，再写 `sortingOrder`。**
> 否则 `sortingOrder` 被**丢弃**（实测读回 0），事后补开也不恢复：`setOrderThenOverride → 0`，`overrideThenSet → 4000`。
> （`UIRootCreator` 原来给 6 个层 Canvas 只设了 `sortingOrder`，层顺序是靠「创建顺序 = 兄弟顺序」侥幸正确的。）
>
> **② 必须先挂到最终父级，再配置 Canvas。**
> 实测：`overrideSorting = true; sortingOrder = 4010;` 之后再 `SetParent`，
> **`SetParent` 会静默重置 overrideSorting** → 读回 `override=False`，且 `sortingOrder` 变成**父级的 order（4000）**；
> 调换顺序才对（`True / 4010`）。这就是第二阶段的模态遮罩一开始失效的原因。
> 重挂父级后必须**重新写**。
>
> 两条规则已收口为**唯一入口** `Framework/Adapters/UI/NestedCanvas.cs`（`Configure` / `SetOrder`），
> 调用点：`UIRootCreator.CreateCanvas`、`PanelSortTarget.Ensure/SetOrder`、`PopupService.EnsureBlocker`。
> ⚠️ 两个行为都**不抛异常、不打日志**，只能读回值验证 → 改这里必须 Play 读回 `Canvas.overrideSorting` / `sortingOrder`。
>
> 实测结果（Play）：层 Canvas = `1000/2000/3000/4000/5000/6000`（override=true）；
> 面板实例 = `ForPanel(layer, priority)`（Loading@HighFrequency = `3001`，DemoPanel@Popup,pri=10 = `4011`）；
> 模态遮罩 = `4010`（= `4011 - 1`），父级与模态面板同层 Canvas。

---

## 4. API 契约

```csharp
// Framework.Abstractions.UI
public interface IPopupService
{
    int  Depth { get; }
    bool CanPop { get; }

    void Push<T>() where T : class, IUIPanel;
    Task PushAsync<T>() where T : class, IUIPanel;                 // 走串行队列
    Task PushAsync<T>(int priority) where T : class, IUIPanel;     // 覆盖注册时的 Priority

    void Pop();                                                    // 关栈顶 + 恢复下层
    void PopAll();
    void PopTo<T>() where T : class, IUIPanel;

    bool TryPeek(out IUIPanel panel);
    T    Peek<T>() where T : class, IUIPanel;

    void Clear();                                                  // 清空（不弹确认）

    event Action<IUIPanel> Pushed;
    event Action<IUIPanel> Popped;
}
```

- **`IUIService` 只增加排序所需的东西**（`PanelDefinition.Priority` / `IsModal`），不引入栈概念。
- `IPopupService` 由业务模块注册（`IGlobalServiceModule`），内部复用 `IUIService` 打开/关闭面板。

---

## 5. 关键行为

### 5.1 Push
1. 查定义 → `CheckSceneTag`（沿用现有校验）
2. 分配 `Seq`（单调递增）→ 计算 `Order`
3. `IUIService.OpenAsync<T>()`（已缓存则直接取实例）
4. 若 `IsModal`：把当前栈顶（若存在）视为「被遮」→ 放置/更新共享 blocker
5. `panel.SetOrder(Order)`（`PanelFactory` 在实例根上挂了排序句柄）
6. 入栈 → 触发 `Pushed`

### 5.2 Pop
1. 取栈顶 → `Hide()`
2. 出栈 → 触发 `Popped`
3. 若新栈顶存在且「被隐藏保活」→ `Show()` 恢复
4. 重算模态：把 blocker 放到新的「栈顶 modal 之下、其余之上」；无 modal 时**隐藏 blocker**

### 5.3 场景切换

> ✅ **已修复的现存缺陷（2026-10-12，阶段 3）**：启动时日志先出现 `[Load] 预加载面板：1 个（DemoPanel）`（为**进入**的场景预加载），
> 紧接着步骤 7 释放**离开**的场景 → `ReleaseScene → DisposeScenePanels()` → **无差别**销毁所有非全局面板 →
> **刚为进入场景预加载的面板被白白销毁**（实测 `IsCached(DemoPanel) = false`，且其预制体句柄也一并释放，导致随后的同步 `Open<DemoPanel>()` 也失败）。
> 根因：`DisposeScenePanels()` 不看场景归属。

**当前实现**：

```
SurvivesScene(definition, entering) = definition == null            // 定义缺失 → 不销毁
                                   || IsGlobal                      // SceneTags 为空 = 所有场景通用
                                   || string.IsNullOrEmpty(entering) // 拿不到目标场景 → 不销毁（保守）
                                   || SceneTags.Contains(entering)
```
- **阶段 B 已落地**：`IsGlobal` 改为只读属性 `SceneTags == null || SceneTags.Count == 0`（字段已删），彻底消除「`IsGlobal=true` 却填了 `SceneTags`」的自相矛盾配置；`CheckSceneTag` 里原先那句 `if (IsGlobal) return true;` 因此成为死代码，已删。
- `IUIService.DisposeScenePanels(string enteringScene)` **已改带参**：判据用「**进入的场景**」，不能用 `ActiveSceneName`（Additive 过渡期它还是旧场景 —— 同一个坑 `CheckSceneTag` 的注释里已记录）。
- `ISceneFlowHooks.SceneResourceRelease(resolver, leavingScene, enteringScene)`：为把目标场景名送到钩子，**改了签名**（4 处调用点同步）。
- 被销毁的面板若在栈内 → 由 `PopupService.Prune()` 从栈中剔除 + 重建模态（每次查询/入栈/场景卸载时都会跑）。
- **回归覆盖**：`UIServiceTests.DisposeScenePanels_KeepsGlobalAndEnteringScenePanels_DestroysOthers`（服务层）、
  `SceneFlowTests.SceneResourceRelease_DisposesLeavingScenePanels_ButKeepsGlobalAndEnteringScenePanels`（钩子层）。
- 顺带清理：删游离资产 `Game/UIPanelConfig.asset`（未被 `UIConfigAsset` 引用）；`LoadingPanelConfig.sceneTags` 清空（原为 `isGlobal=1` + `sceneTags=[LoadingScene]`，标签是死配置）。

### 5.4 串行队列（⑥b）—— 已实现

**结构**：显式 `Queue<PendingPush>` + `async` 泵 `DrainQueueAsync()`，一次只处理一项 → 「调用顺序 = 栈顺序」。
每项带一个 `TaskCompletionSource`（⚠️ **不加** `RunContinuationsAsynchronously`：调用方的延续要回到 Unity 主线程）。

**快路径（关键性质）**：底层 `OpenAsync` 已同步完成时**直接 return、不建计时器**；
又因为串联用的是 `await`（**不是** `ContinueWith`－后者会排到线程池），所以「已缓存面板」的 `PushAsync` 在**调用帧内同步完成**。
Unity 与测试都依赖这条性质（测试断言 `task.IsCompleted` 而不阻塞主线程）。

**取消（隐式）**：

| 项的状态 | 行为 |
|---|---|
| 还在队列中 | **当场**摘出队列，`Task` 立刻以 `null` 完成 |
| 正在打开中 | 只打 `Cancelled` 标记，等它的打开落地（或被超时截断）后才能决定是否要关掉刚建出来的面板 |

- 触发点：`Pop` / `PopAll` / `Clear` / `PopTo` / 场景卸载 / `Dispose`（`Dispose` 额外直接终结飞行项，否则调用方会永远等不到结果）。
- 已取消的项：**不显示、不入栈**；若面板已被建出来则 `Close()` 掉；失败/取消/超时**统一以 `null` 完成**（不抛异常）。
- 设计取舍：`Pop` 同时也作废待处理项 —— 语义是「按了返回/关闭，就不该再冒出此前排队要开的弹窗」。

**超时兜底**：`UISettings.PopupQueueTimeoutSeconds`（默认 **10s**，`<= 0` 关闭）。
用 `await Task.WhenAny(openTask, Task.Delay(timeout))`；超时则记错误、跳过并继续下一项（避免一个卡住的加载把**整条队列**堵死）。
超时后那次打开**最终仍可能成功** —— 那时 `UIService` 已经把它创建并 `Show()` 了出来（但不在弹窗栈里，会是一个「无主的可见面板」），
所以额外挂一个 `AbandonLateOpenAsync` 等它落地后 `Close()` 掉（延续必须回到主线程，所以用 `await`）。

**新增查询**：`IPopupService.PendingCount` = 队列中 + 正在打开的那一项（仅统计 **异步** Push；同步 `Push` 不占队列）。

**测试写法约束（踩过坑）**：本项目 UTF **不支持 `async Task` 测试方法**（报 `Method has non-void return value`），
也不能用 `.Wait()/.Result/WaitAll` 阻塞主线程（会死锁卡死编辑器）。
但 EditMode 的 **`[UnityTest] IEnumerator` 能驱动 await 延续**（实测 `SyncContext = UnitySynchronizationContext`），
所以这类需要「等待中被打断 / 超时后完成」的用例用 `[UnityTest]` + `yield return null` 写。
配套用了 `GatedFactory`（可人为卡住某个面板的 `CreateAsync`，再 `Release` 放行）。

---

## 6. 输入解耦（链 A）

**框架不含任何输入概念**：不定义 `IBackInput`、不加 `GameRoot.Update` 轮询、不 `using UnityEngine.InputSystem`。

```csharp
// 业务侧（Game 程序集）—— 唯一出现输入依赖的地方
cancelAction.performed += _ =>
{
    if (_popup.CanPop) _popup.Pop();   // 有弹窗 → 关栈顶
    else OpenPauseMenu();              // 无弹窗 → 业务自己的逻辑
};
```

理由：「返回键」的语义是**分层**的（有弹窗→关弹窗；无→开暂停菜单；再否则→忽略/退出），不该由框架独占。框架只提供 `Pop()/CanPop/Peek/Depth`。

依赖方向（输入依赖只允许出现在最上层）：

```
Abstractions → Core
Adapters     → Abstractions
Bootstrap    → Adapters
Game         → Bootstrap        ← 唯一持有 Unity.InputSystem 的地方
```

---

## 7. 测试计划

| 类 | 覆盖 |
|---|---|
| `UIServiceTests`（现有 12 例） | **必须保持全绿**；非 MonoBehaviour 面板（纯 C# 测试替身）走「无排序句柄 → 跳过排序」的降级路径 |
| `UIServiceOrderingTests`（新） | `Priority` → 生成的 `Canvas.sortingOrder` 正确；同优先级按打开序；非 MonoBehaviour 面板不抛异常 |
| `PopupServiceTests`（新） | push/pop/peek/depth；重复 push 同面板；`Pop` 恢复下层；模态 blocker 的 order 夹位；`SurvivesScene` 四种组合；场景切换剔除 + 重建模态；串行队列顺序 + 取消 + 超时 |

Play 验收：两个 Popup 叠加 + 一个 Toast；验证点击命中（嵌套 Canvas 的 `GraphicRaycaster` 是否需要单独挂 —— **需实测确认**）。

> **实测结论（阶段 2）**：嵌套 Canvas **必须自带 `GraphicRaycaster`**，否则它自己的 `Graphic` 不会被任何 raycaster 收录
> （`GraphicRegistry` 按「最近的 Canvas」登记，父层 Canvas 的 raycaster 看不到子 Canvas 的图形）。
> 实测 `RaycasterManager` 里每个层 Canvas / 面板 Canvas / 遮罩 Canvas 都各自注册了一个 `GraphicRaycaster`。
> 遮罩验证：`EventSystem.RaycastAll` 命中顺序 = `DemoPanel(4011) / PopupBlocker(4010) / Loading 及其子图(3001)…`。
> ⚠️ 测试陷阱：**同帧新建的 Graphic `depth == -1`，会被 raycaster 跳过** → 刚创建完立刻射线会「命不中」，跨帧再测才准。

---

## 8. 分阶段实施

| 阶段 | 范围 | 验收 | 状态 |
|---|---|---|---|
| **1 基础 + 排序原语** | `UIRootCreator` 补 `overrideSorting`；`PanelDefinition.Priority/IsModal`；`PanelFactory` 挂排序句柄（`Canvas(overrideSorting)` + `GraphicRaycaster`）；`UIService` 打开时应用排序（降级安全）；配置 SO 接线 | 现有 12 例全绿 + 排序新例；Play 视觉不回归 | ✅ 76 例绿 + Play 验证 |
| **2 栈 + 模态 + 队列** | `IPopupService`/`PopupService`、全局栈、共享 blocker、`(Priority, 打开序)`、异步串行队列、场景联动 | `PopupServiceTests` 全绿 + Play 双向验证 | ✅ 87 例绿 + Play 验证（遮罩/排序/点击命中） |
| **2b 队列取消 + 超时** | `PendingCount`；队列项取消（排队当场出队 / 飞行项打标）；`UISettings.PopupQueueTimeoutSeconds` 超时兑底 + 迟到面板回收；`[UnityTest]` 异步用例 | 全量测试 | ✅ 95 例绿（PopupServiceTests 17 例，其中 4 例为 `[UnityTest]`） |
| **3 收尾** | `SceneTags` 阶段 B（删 `IsGlobal` 字段）、删游离资产 `Game/UIPanelConfig.asset`、清 `LoadingPanelConfig.sceneTags`、`DisposeScenePanels(enteringScene)` 修「预加载面板被误删」、文档 | 全量测试 + 文档 | ✅ 89 例绿；`SceneResourceRelease` 签名已加 `enteringScene` |

---

## 9. 已知风险

1. ~~**嵌套 `overrideSorting` 三层**~~（UIRoot → 层 Canvas → 面板 Canvas）：**已实测通过**。关键约束是「先挂父级、再 override、再 order」，见 §3。
2. ~~**嵌套 Canvas 的点击命中**~~：**已实测确认**需要自带 `GraphicRaycaster`，且遮罩确实能拦住下层面板，见 §7。
3. **`SceneTags` 语义三合一**（打开校验 + 预加载筛选 + 销毁判定）：一个面板若误填标签，会同时「打不开 + 被销毁」。
   阶段 B 已把「无标签 = 全局」定为唯一真相（删掉了会打架的 `isGlobal` 字段）；
   编辑器侧目前**只能靠 `UIPanelConfigSO.sceneTags` 的 Tooltip 提示**，没有硬校验 —— 若以后误配变多，可加重合/拼写校验。
4. **`Priority` 与 `PanelLayer` 的关系**：**层优先**，`Priority` 只在层内生效；要跨层必须靠 §3 的排序空间（已支持）。
5. ~~**异步队列的取消/超时未实现**~~：**已完成（见 §5.4）**。
   唯一保留的取舍：超时后「迟到但成功」的面板会被建出来再 `Close()` 掉（而不是事前阻止）——
   因为 `UIService.OpenAsync` 内部先创建后返回，无法从中途取消；代价是一个多余的隐藏实例，无泄漏。
