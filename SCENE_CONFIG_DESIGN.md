# 场景配置化 · 设计稿

> 版本：v1.1（2026-10-12）· 状态：**PR1 配置化 + PR2 两个 bug 已实施并验收**（详见文末「实施记录」）
> 目标：把「场景本身 + 切换相关信息」做成与 UI 面板一致的**配置驱动**模式，并以此为基础把切换策略（卸载时序 / 加载模式 / 常驻与否）变成配置项而非代码分支。

---

## 0. TL;DR

| 项 | 结论 |
|---|---|
| 可行性 | **高**。UI 那条链路（SO → `ConfigBySO<T>` → `IConfigFactory` → `ConfigService` → `LoadStep` 转运行期定义 → 注册）已验证过，场景可完整照抄 |
| 启动时序 | **无需改动**（已读代码确认：配置加载在第 3 步，`SceneFlow` 创建在第 4 步） |
| 最大硬约束 | **配置里不能用 `SceneAsset` 引用场景**（`SceneAsset` 在 `UnityEditor`）→ 只能存字符串 + 编辑器侧校验 |
| 最大收益 | ① 每场景预加载地址从硬编码搬到配置；② Addressables 地址与场景名解耦；③ **编辑器一键同步 Build Settings / Addressables**（顺带消灭 README 踩坑第 1 条） |
| 明确不做 | 场景级服务/模块的配置化；`RenderSettings`/光照的运行时覆盖；场景参数化（`Level2@Boss`） |

---

## 1. 现状与差距

### 1.1 现状（硬编码点清单）

| 位置 | 硬编码内容 |
|---|---|
| `Game/Bootstrap/GameBootstrapOptions.cs` | 入口场景名、Loading 场景名（代码里写字符串） |
| `Game/Bootstrap/DemoScenePreloadStep.cs` | `if (context.SceneName == "SampleScene") … else if (…)` 的预加载分支 |
| `Adapters/Scenes/AddressablesSceneService.cs` | **隐式约定**：「Addressables 地址 = 场景名」 |
| `Bootstrap/BootstrapRunner.cs` | `ResolveEntryScene()` 回退到 `SceneUtility.GetScenePathByBuildIndex(0)` |
| `Bootstrap/SceneFlow.cs` | 加载模式固定 `Additive`；卸载时序固定「先载后卸」；无超时；无「常驻」概念 |
| 项目设置 | 场景必须**手工**加入 Build Settings 与 Addressables（两个地方都要，容易漏） |

### 1.2 UI 侧已跑通的范式（照抄对象）

```
UIConfigAssetSO (SO 总表)  +  UIPanelConfigSO (SO 单项)
        ↓  UIConfigFactory : IConfigFactory  { configId="UIConfig", loadOrder=-100 }
        ↓  GameConfigRegistry.Discover()      ← 反射自动发现，无需改启动代码
        ↓  ConfigBySO<UIConfigAssetSO>.Load() ← 启动管线预热地址后同步取 SO
        ↓  UIPanelSetupStep : IStartupLoadStepProvider
        ↓  PanelDefinition (运行期只读定义) → IUIService.Register()
```

**结论：场景侧 1:1 对应，不引入新机制。**

---

## 2. 硬约束（设计必须迁就的事实）

### C1 🔴 配置不能持有 `SceneAsset` 引用

`UnityEditor.SceneAsset` 属于 Editor 程序集。配置 SO 在运行时程序集 → 引用它会导致编译失败 / 出包报错（本项目已踩过该坑）。

**对策**：字段类型用 `string sceneName`；提供 **Editor-only 的 `PropertyDrawer`**（下拉选场景，内部写回字符串）。编辑器体验不损失，构建不受影响。

### C2 配置不能替代登记（⚠️ 且两条路**互斥**）

场景必须在 Addressables（`AddressablesSceneService` 路径）或 Build Settings（`UnitySceneService` 路径）里登记，配置只能**描述**它。

> 🔴 **实施中发现的重要事实**：Unity 强制二者**互斥** —— 场景一旦标记为 Addressable，Unity 会**自动把它在 Build Settings 中禁用**（保留条目但 `enabled = false`，并输出警告 `A scene from the EditorBuildScenes list has been marked as addressable…`）。
> 所以「双登记，两条路都能跑」是**错的**；v1 草案里的该描述已作废。
> 本工程实测：`SampleScene` / `LoadingScene` / `Level2` 在 Build Settings 里**均为 `enabled=false`**，实际一直走 Addressables 路径。
>
> 因此配置新增了 `SceneRegistration { Addressables | BuildSettings }`（默认 `Addressables`），同步工具按它**只写一条路**，且 V6 校验只在 `BuildSettings` 模式下才执行（否则会产生误导性告警）。

**对策（同时也是最大收益）**：编辑器同步工具，见 §7。

### C3 场景级服务/模块不能配置化

那是代码。**不做**「按类型名字符串启用模块」（类型安全差、重构即碎）。现有 `ISceneServiceModule` 自动扫描 + `sceneName` 判定已足够。

### C4 `RenderSettings` / 光照不可运行时覆盖

环境光 / 天空盒 / 雾跟随 **ActiveScene**，与 GameObject active 状态无关。要统一必须落在场景资产上。**不做**运行时钉值（会引入「编辑器所见 ≠ 运行时」）。

### C5 时序约束：配置必须先于 `SceneFlow` 可用

已确认满足 ✅（`BootstrapRunner.Run`：3) 启动加载管线 → 4) `new SceneFlow(...)`）。
**本设计不得破坏该顺序。**

---

## 3. 数据模型

### 3.1 `SceneDefinition`（运行期只读，Abstractions 层）

```csharp
namespace Framework.Abstractions.Scenes
{
    /// <summary>场景的运行期只读定义。由配置资产转换而来，SceneFlow 只读它，不反向依赖配置。</summary>
    public sealed class SceneDefinition
    {
        public SceneDefinition(
            string name,
            string address,
            SceneLoadMode loadMode = SceneLoadMode.Additive,
            bool persistent = false,
            bool showLoading = true,
            IReadOnlyList<string> preloadAddresses = null,
            IReadOnlyList<string> tags = null,
            float waitTimeoutSeconds = 10f);

        public string Name { get; }                      // 逻辑名（Build Settings 场景名）
        public string Address { get; }                   // Addressables 地址（默认 == Name）
        public SceneLoadMode LoadMode { get; }           // Single / Additive
        public bool Persistent { get; }                  // 常驻：切走时不卸载
        public bool ShowLoading { get; }                 // 是否显示 Loading 遮罩
        public IReadOnlyList<string> PreloadAddresses { get; }  // 进入本场景前预加载
        public IReadOnlyList<string> Tags { get; }       // 场景标签（与面板 sceneTags 对齐）
        public float WaitTimeoutSeconds { get; }         // 等待场景容器建立的超时（0 = 不限）
    }

    public enum SceneSwapPolicy
    {
        LoadThenUnload = 0,   // 默认：先加载新场景，成功后卸载旧场景（失败可保住旧场景）
        UnloadThenLoad = 1,   // 先卸/禁旧场景，再加载新场景（内存峰值最低；要求 Loading 场景自带相机）
    }
}
```

### 3.2 `ISceneCatalog`（运行期查询，Abstractions 层）

对称于 `IUIService.Register(PanelDefinition)`：

```csharp
public interface ISceneCatalog
{
    string EntrySceneName { get; }
    string LoadingSceneName { get; }
    SceneSwapPolicy SwapPolicy { get; }
    bool Strict { get; }                      // 未登记场景是否视为错误（见 §6 规则 V3）

    void Register(SceneDefinition definition);

    bool TryGet(string sceneName, out SceneDefinition definition);
    SceneDefinition Get(string sceneName);    // 未登记时按 Strict 决定是抛异常还是返回 null
    IReadOnlyList<SceneDefinition> All { get; }
}
```

**实现 `SceneCatalog` 放 `Adapters/Scenes/`**，由 `CoreServiceModule` 注册为单例（对称于 `UIService`）；由业务的 setup step 填充。

### 3.3 `SceneConfigSO`（单项，Game 层，Editor 可配）

```csharp
[CreateAssetMenu(menuName = "Game/Config/Scene Config", fileName = "SceneConfig")]
public sealed class SceneConfigSO : ScriptableObject
{
    [Tooltip("场景逻辑名（= Build Settings 中的场景名）")]
    public string sceneName;

    [Tooltip("地址；留空表示与 sceneName 相同")]
    public string address;

    // 注：原 `loadMode` 字段已在 §12.5 合并进全局 `transitionPolicy`，本项已删除

    [Tooltip("常驻：切到其它场景时不卸载本场景")]
    public bool persistent;

    [Tooltip("进入本场景时是否显示 Loading 遮罩")]
    public bool showLoading = true;

    [Tooltip("进入本场景前预加载的 Addressables 地址")]
    public List<string> preloadAddresses = new();

    [Tooltip("场景标签（供面板 SceneTags / 资源筛选对齐）")]
    public List<string> tags = new();

    [Tooltip("等待场景容器建立的超时（秒）；<=0 表示不限")]
    public float waitTimeoutSeconds = 10f;
}
```

### 3.4 `SceneConfigAssetSO`（总表，Game 层）

```csharp
[CreateAssetMenu(menuName = "Game/Config/Scene Catalog", fileName = "SceneCatalog")]
public sealed class SceneConfigAssetSO : ScriptableObject
{
    [Tooltip("入口场景（启动后进入）")]
    public string entrySceneName;

    [Tooltip("常驻 Loading 场景；留空表示不使用 Loading 场景")]
    public string loadingSceneName;

    [Tooltip("切换策略（全局，见 §12.5）")]
    public SceneTransitionPolicy transitionPolicy = SceneTransitionPolicy.SingleDirect;

    [Tooltip("未登记场景是否视为错误（推荐勾选；迁移期可先不勾）")]
    public bool strict = false;

    public List<SceneConfigSO> scenes = new();
}
```

---

## 4. 文件清单与分层

| 文件 | 程序集 | 说明 |
|---|---|---|
| `Abstractions/Scenes/SceneDefinition.cs` | Abstractions | 运行期只读定义 + `SceneTransitionPolicy`（§12.5） |
| `Abstractions/Scenes/ISceneCatalog.cs` | Abstractions | 查询接口 |
| `Adapters/Scenes/SceneCatalog.cs` | Adapters | 实现；`CoreServiceModule` 注册单例 |
| `Game/Scenes/Config/SceneConfigSO.cs` | Game | 单项配置资产 |
| `Game/Scenes/Config/SceneConfigAssetSO.cs` | Game | 总表资产 |
| `Game/Scenes/Config/SceneConfigFactory.cs` | Game | `IConfigFactory`（`configId="Scenes"`, `loadOrder=-200`） |
| `Game/Scenes/Config/SceneCatalogSetupStep.cs` | Game | `IStartupLoadStepProvider`：SO → `SceneDefinition` → 注册 |
| `Game/Scenes/Config/SceneCatalog.asset` | 资产 | 新增；登记 `LoadingScene` / `SampleScene` / `Level2`；加入 Addressables |
| `Framework/.../Editor/SceneConfigSynchronizer.cs` | Editor | 校验 + 同步 Build Settings / Addressables |
| `Framework/.../Editor/SceneNameDrawer.cs` | Editor | 场景名下拉（内部仍写字符串，绕开 C1） |

> `CoreServiceModule`（`Adapters/Modules`）需增加一行注册 `SceneCatalog`。

---

## 5. `SceneFlow` 改造点

### 5.1 构造与依赖

```csharp
public SceneFlow(
    IServiceResolver globalResolver,
    ISceneService sceneService,
    ISceneFlowHooks hooks,
    Func<IEnumerator, Coroutine> coroutineRunner,
    string loadingSceneName = null,
    ISceneCatalog catalog = null);          // 新增；为 null 时用 BootstrapOptions 构造的默认目录
```

**兼容策略**：`catalog == null` 时内部构造 `DefaultSceneCatalog(loadingSceneName, entrySceneName)`，其 `Get()` 对任何名字都返回「Additive + 不常驻 + 显示 Loading + 无预加载 + 默认超时」——即**完全等价于当前行为**。零配置项目不受影响。

### 5.2 策略读取点

| 位置 | 现在 | 改后 |
|---|---|---|
| `InitializeLoadingScene()` | `_loadingSceneName` | `catalog.LoadingSceneName` |
| `SwitchSceneRoutine` 步骤 1 | 无条件显示 Loading | `if (target.ShowLoading)` |
| 步骤 2 预加载 | 由 hooks 自行决定 | 框架内置步骤读 `target.PreloadAddresses`（见 §5.3） |
| 步骤 4 加载 | `SceneLoadMode.Additive` 固定 | 由全局 `transitionPolicy` 决定（§12.5） |
| 步骤 6 等待容器 | `while (!_containerReady) yield return null;` **无超时** | 加 `target.WaitTimeoutSeconds` 超时（见 §5.4） |
| 步骤 8 卸载判定 | 只看 `prevScene != _loadingSceneName` | **再加 `!prevDefinition.Persistent`** |

> ⚠️ **常驻判定要看「旧场景」的定义**，不是新场景的。`Level2` 是常驻的话，切到 `SampleScene` 时 `Level2` 要留在内存里。

### 5.3 新增框架内置步骤

`LoadStepCollector` 的**场景**内置步骤增加：

```
[内置] PreloadPanelsStep            （现有）
[内置] PreloadSceneAddressesStep    （新增）→ ctx.SceneName → catalog.Get() → 预加载 PreloadAddresses
[业务] ISceneLoadStepProvider       （现有）
```

实现落点：`Abstractions/Loading/PreloadSceneAddressesStep.cs`，与 `PreloadAddressesStep` 复用同一套「异步预热 + 进度 + 超时兜底」逻辑。

**迁移**：`DemoScenePreloadStep` 的预加载内容搬进配置后删除该类。
`README` 保留「如何用 `ISceneLoadStepProvider` 追加步骤」的示例，改用别的场景（如「进 Level2 预热 BGM」），避免示例与配置重复。

### 5.4 顺带修掉的两个真缺陷（独立于配置化，但正好被配置承载）

| # | 缺陷 | 修法 |
|---|---|---|
| **B1** | `SetActiveScene` 在**容器建立之后**（步骤 7 晚于 6）→ 该窗口内新建的**无父物体**归属旧场景，随后被卸载时**静默销毁** | 把 `SetActiveScene(sceneName)` 提前到 `Activate()` 完成之后、等待容器之前 |
| **B2** | `while (!_containerReady)` **无超时** → 永久挂死，且此时音频已静音、灯光已关闭、遮罩已显示，全都停在改动后的状态 | 加超时（读 `definition.WaitTimeoutSeconds`）；超时走失败路径：恢复音频/灯光、收起 Loading、报错、`_isSwitching = false` |

### 5.5 `SwapPolicy` → `SceneTransitionPolicy`

PR1 只**读取 + 校验 + 打日志**；PR3 实现真正的分支；**§12.5 把 `loadMode` 与 `swapPolicy` 合并成了单一枚举 `SceneTransitionPolicy`**（推荐先看 §12.5）。

| 值 | 旧组合 | 行为 |
|---|---|---|
| `SingleDirect` | （无旧对应；新默认） | 直接 Single 加载 |
| `ThroughLoadingScene` | `loadMode=Single` | 先 Single 加载 Loading 场景，再加载目标（A → Loading → B） |
| `Additive` | `loadMode=Additive` + `swapPolicy=LoadThenUnload` | 先载新、激活后卸旧 |
| `AdditiveUnloadFirst` | `loadMode=Additive` + `swapPolicy=UnloadThenLoad` | 先卸旧、再载新 |

---

## 6. 启动期校验规则

新增 `SceneCatalogSetupStep` 内的一次性校验，结果汇总为**一行摘要 + 逐条明细**。

| # | 规则 | 级别 | 行为 |
|---|---|---|---|
| V1 | `entrySceneName` 为空 | 警告 | 回退 Build Settings[0]（现状行为） |
| V2 | `entrySceneName` 未在 `scenes` 中登记 | 警告 | 用默认策略继续，并提示补配置 |
| V3 | 切换到一个未登记的场景 | 由 `strict` 决定 | `strict=true` → **报错并拒绝切换**；`false` → 警告 + 默认策略 |
| V4 | `loadingSceneName` 非空但未登记 | 警告 | 回退为「不使用 Loading 场景」 |
| V5 | 重复的 `sceneName` | 错误 | 取第一个 + 报错列出重复项 |
| V6 | 场景名在 Build Settings 中找不到 | 警告 | 提示运行同步工具（`UnitySceneService` 路径下会真实失败） |
| V7 | `address` 在 Addressables 中找不到 | 警告 | 同上（`AddressablesSceneService` 路径下会真实失败） |
| V8 | 选了 `ThroughLoadingScene` / `AdditiveUnloadFirst` 但 **`loadingSceneName` 为空** | 警告 | 这两种策略依赖「旧场景释放后的独立画面」，必须配 Loading 场景 |
| V9 | `transitionPolicy` 为 Single 系但存在 `persistent` 场景 | 警告 | Single 会摧毁常驻场景；SceneFlow 还会**自动改用 `Additive`** 以免静默做错事 |
| V10 | Loading 场景**无 Camera**（选 `ThroughLoadingScene` / `AdditiveUnloadFirst` 时） | 警告 | **运行时检查**（`SceneFlow.WarnIfLoadingSceneHasNoCamera`）

> V6/V7 在**编辑器同步工具**里升级为「一键修复」；在运行时只做提示（运行时不改项目设置）。

---

## 7. 编辑器侧

### 7.1 场景名下拉（绕开 C1）

`SceneNameDrawer : PropertyDrawer`，仅用于标了 `[SceneName]` 特性的 `string` 字段：

- 下拉列出 `EditorBuildSettings.scenes` 中的场景 + Addressables 中的场景
- 选中的值写回**字符串**
- 若当前值不在任何一处登记 → 显示为红色 + 提示「未登记」

> 目的：保留「选场景」的体验，同时不把 `SceneAsset` 带进运行时程序集。

### 7.2 同步工具（最大收益）

`SceneConfigSynchronizer`，从 `SceneConfigAssetSO` 的 CustomEditor 上一个按钮触发：

| 动作 | 实现 |
|---|---|
| 把配置里的场景补进 Build Settings | `EditorBuildSettings.scenes`（幂等：已存在则跳过，不重排既有顺序） |
| 把配置里的场景标为 Addressable 并设地址 | `AddressableAssetSettingsDefaultObject.Settings.CreateOrMoveEntry(guid, group)` + `entry.address = address` |
| 把配置资产自身加入 Addressables | 同上（框架已依赖它来加载配置） |
| 反向检查 | 扫描 Build Settings / Addressables 中「未被任何配置项引用」的场景 → 警告 |
| 校验 | 跑一遍 §6 的 V1–V7，在 Console 里输出结果 |

> 幂等性要求：连续点两次不得产生重复条目或改变既有分组/地址。

---

## 8. 兼容与迁移

| 项 | 策略 |
|---|---|
| `BootstrapOptions.EntrySceneName` / `LoadingSceneName` | **保留**。优先级：**catalog 有值 → 以 catalog 为准**；catalog 未提供 → 用 `BootstrapOptions`。两者都空 → 现状回退（Build Settings[0] / 无 Loading 场景） |
| `GameBootstrapOptions.cs` | 配置落地后**清空其场景名字段**（保留类作为示例），验证回退链路仍工作 |
| `MuteAudioListeners` / `DisableLightsOf` | **保留**（仅 `Additive` 系需要）；Single 系策略下**自动旁路**（§12.5） |
| `DemoScenePreloadStep.cs` | PR1 内删除（内容搬进配置）；README 示例改用别的用途 |

---

## 9. 测试计划

### 9.1 EditMode 单测（新增）

| 测试类 | 覆盖 |
|---|---|
| `SceneCatalogTests` | Register / TryGet / Get / 同名覆盖 / `All` / `strict` 下的未登记行为 |
| `SceneCatalogSetupStepTests` | SO → `SceneDefinition` 映射；`address` 留空回退 `sceneName`；`preloadAddresses`/`tags` 转换；空 SO 防护 |
| `SceneConfigValidationTests` | V1–V7 逐条（重复名、入口未登记、Loading 未登记…） |
| `SceneFlowTests` 扩展 | ① 常驻场景（`Persistent=true`）不被卸载；② `ShowLoading=false` 时不显示 Loading；③ 等待容器**超时**后走失败路径且音频/灯光被恢复；④ 未登记场景在 `strict` 下被拒绝 |

> 现有 39 例必须保持全绿（`catalog == null` 的回退路径保证这一点）。

### 9.2 编辑器验证

- 同步工具**幂等性**：连续执行两次，Build Settings 条目数与 Addressables 条目数不变
- 同步工具**修复能力**：手工删掉一个 Build Settings 条目 → 同步后恢复

### 9.3 Play 验收清单（逐条勾选）

- [ ] 启动日志出现配置摘要（`[Scenes] 场景目录：3 个（入口=SampleScene，Loading=LoadingScene）`）
- [ ] `Level2` 的预加载由配置驱动（日志 `[Load] Level2 专属资源：1 个地址（Cube）` 不变），且 `DemoScenePreloadStep` 已删除
- [ ] `SampleScene ↔ Level2` 双向切换行为与改造前一致
- [ ] 把一个场景临时设为 `persistent=true` → 切走后该场景仍在内存（`SceneManager.sceneCount` 验证）
- [ ] 把一个场景临时设为 `showLoading=false` → 该次切换无 `显示 Loading 场景` 日志
- [ ] 把 `waitTimeoutSeconds` 设为 `0.001` → 出现超时日志，且**灯光/音频被恢复**、无残留改动状态
- [ ] 未登记场景：`strict=false` 时警告并继续；`strict=true` 时被拒绝

---

## 10. 分阶段实施

| 阶段 | 范围 | 验收标准 | 可回退 |
|---|---|---|---|
| **PR1 配置化** | §3 / §4 / §5.1–5.3 / §6 / §7 / §8 | §9.3 前三条 + §9.1 前三个测试类；现有 39 例全绿 | 是（删配置资产即回退到 `BootstrapOptions` 路径） |
| **PR2 修两个 bug** | §5.4（B1、B2） | 新增超时/归属测试；Play 无回归 | 是（改动集中在两处） |
| **PR3 Single 模式** | §5.5 + Loading 场景加相机 +「按需 Single 加载 Loading 场景」过渡 | 过渡为 **A → Loading 场景 → B**；`SceneManager.sceneCount == 1` 全程成立 | 是（默认 `loadMode = Additive`，不改行为） |

> PR1 与 PR2 相互独立，可并行；PR3 依赖 PR1（需要配置承载策略开关）。

---

## 11. 未决问题（已拍板）

| # | 问题 | 结论 |
|---|---|---|
| Q1 | 未登记场景默认严格还是宽松？ | **宽松**（`strict = false`）：警告 + 回退「Additive / 不常驻 / 显示 Loading」，保证零配置项目行为不变。稳定后再考虑翻 `strict` |
| Q2 | 切换策略全局还是每场景？ | **全局**（已由单一枚举 `SceneTransitionPolicy` 承载，见 §12.5） |
| Q3 | 是否支持「场景参数」（`Level2@Boss`）？ | **本期不做** |
| Q4 | Single 模式本期实现？ | **已实现**（PR3 + §12.5）：`transitionPolicy` 的 `SingleDirect` / `ThroughLoadingScene` 两个值 |
| Q5 | 是否暴露定义查询给业务？ | **已暴露**：`ISceneFlow.GetDefinition(name)` + `ISceneCatalog.TryGet/Get` |

---

## 12. 实施记录（2026-10-12）

### 12.1 已落地文件

| 文件 | 说明 |
|---|---|
| `Framework/Abstractions/Scenes/SceneDefinition.cs` | 运行期只读定义 + `SceneTransitionPolicy`（§12.5） |
| `Framework/Abstractions/Scenes/ISceneCatalog.cs` | 目录接口（`TryGet`/`Get`/`Register`/`All` + 端点与策略） |
| `Framework/Abstractions/Scenes/SceneNameAttribute.cs` | `[SceneName]` 标记（避免运行期引用 `SceneAsset`） |
| `Framework/Adapters/Scenes/SceneCatalog.cs` | 目录实现（`CoreServiceModule` 注册） |
| `Framework/Abstractions/Loading/PreloadSceneAddressesStep.cs` | 内置场景步骤：按配置预加载 |
| `Game/Scenes/Config/SceneConfigSO.cs` | 单项配置 |
| `Game/Scenes/Config/SceneConfigAssetSO.cs` | 总表 + `SceneRegistration` 枚举 |
| `Game/Scenes/Config/SceneConfigFactory.cs` | `IConfigFactory`（`configId="Scenes"`, `order=-200`） |
| `Game/Scenes/Config/SceneConfigValidation.cs` | 启动期校验（V1–V7） |
| `Game/Bootstrap/SceneCatalogSetupStep.cs` | SO → 运行期目录 + 校验 |
| `Game/Editor/Game-Editor.asmdef` | Editor-only 程序集（名字以 `-Editor` 结尾，被模块扫描器跳过） |
| `Game/Editor/SceneNameUtility.cs` | 已知场景名收集（Build Settings ∪ Addressables） |
| `Game/Editor/SceneNameDrawer.cs` | `[SceneName]` 的下拉绘制器 |
| `Game/Editor/SceneConfigSynchronizer.cs` | 同步工具（幂等）+ 校验入口 |
| `Game/Editor/SceneConfigAssetEditor.cs` | 总表检视面板按钮 |

**删除**：`Game/Bootstrap/DemoScenePreloadStep.cs`（硬编码预加载 → 搬进配置）。
**配置资产**：`SceneCatalog.asset` + `SceneConfig_LoadingScene/SampleScene/Level2.asset`（已加入 Addressables）。

### 12.2 相对 v1 草案的偏差（重要）

| 项 | 草案 | 实际 | 原因 |
|---|---|---|---|
| 登记 | 「双登记，两条路都能跑」 | **互斥**，配置新增 `registration` 字段，同步工具只写一条路 | Unity 会把 addressable 场景在 Build Settings 自动禁用（实测） |
| B1 修复位置 | 「`SetActiveScene` 提前到 `Activate()` 之后」 | **移到 `CreateSceneContainerRoutine` 内部**（建容器之前） | 实测发现容器是在 `Activate()` **内部**由 `sceneLoaded` 触发的 —— 放在协程后半段**完全无效**。这是被新写的回归测试抓出来的 |
| `ISceneCatalog.Get` | 「未登记时按 Strict 抛异常」 | **返回 null**（不抛） | 协程里抛异常体验差；改为 `SceneFlow.ResolveDefinition` 统一处理（strict → 记错误并中止；宽松 → 警告 + 默认策略） |
| 等待超时实现 | 累加 `deltaTime` | `Time.realtimeSinceStartup` 比对绝对时间 | 累加方式在 EditMode 单测里 `deltaTime` 为 0 → 无法测试（会死循环） |
| 恢复逻辑 | 只恢复灯光 | 灯光 **+ 音频监听器** | 超时中止时若不恢复监听器会**永久静音**；补上 `_mutedListeners` |

### 12.3 验证结果

- **EditMode 测试：61/61 全绿**（原 39 例 → 新增 22 例：`SceneCatalogTests` 5 / `SceneConfigTests` 9 / `SceneFlowTests` +8）
- **Play 实测**：启动日志 `[Config] 发现配置 3 个：Scenes(order=-200)…` + `[Scenes] 场景目录：3 个（入口=SampleScene，Loading=LoadingScene，swap=LoadThenUnload，strict=False）` + `[Scenes] 校验完成：错误 0 条，警告 0 条`
- **配置驱动生效**：删除 `DemoScenePreloadStep` 后，`[Load] SampleScene 专属资源：3 个地址（Enemy_Red, Enemy_Blue, Enemy_Green）` 与 `[Load] Level2 专属资源：1 个地址（Cube）` 改由配置产出 ✅
- **同步工具幂等**：连续执行两次，第二次为「Addressables 新增 0 个」✅
- 编辑器类型正确加载：`SceneConfigSynchronizer` / `SceneNameDrawer` / `SceneConfigAssetEditor` 均在 `Game-Editor` 程序集 ✅，且**不被**启动扫描器误扫（程序集名含 `-Editor`）

### 12.4 PR3 已实施（2026-10-12）：`loadMode = Single` 与 `swapPolicy = UnloadThenLoad`

| 项 | 实现 |
|---|---|
| `loadMode = Single` | **过渡走 A → Loading 场景 → B**：先把 Loading 场景以 `Single` **按需加载**（原子关闭旧场景），再以 `Single` 加载目标场景（自动关闭 Loading 场景）—— 全程只有 1 个场景。项目含 Single 场景时 `InitializeLoadingScene()` **不预热常驻 Loading 场景**（否则会被第一个 Single 加载干掉 / 产生句柄冲突）；Additive 路径改为 `ShowLoadingSceneRoutine()` **按需 Additive 加载**（兼容混合项目） |
| `swapPolicy = UnloadThenLoad` | 在「显示 Loading 之后、**预加载之前**」卸载旧场景；随后 `prevScene = null` → 音频/灯光补丁自动旁路。⚠️ 只对 Additive 目标生效（Single 天生「先关旧场景」，不再叠加） |
| 前置校验（fail-safe） | `LoadingSceneProvidesCamera()`：UnloadThenLoad 缺相机时**报错并回退** `LoadThenUnload`；Single 过渡缺相机时**告警**（屏幕不会被清空） |
| 句柄泄漏修复 | Single 下旧场景已被引擎关闭，但**仍要调用** `UnloadAsync`；**过渡用的 Loading 场景**被目标场景的 Single 加载关闭后也要显式 `UnloadAsync`；`AddressablesSceneService` 区分「场景还在 → `UnloadSceneAsync`」与「已被关闭 → `Release(handle)`」 |
| Loading 场景资源 | `LoadingScene` 新增 **`LoadingCamera`**（ClearFlags=SolidColor 黑、orthographic、depth=100、`cullingMask=0`、无 AudioListener）—— Single 过渡期间它是唯一场景，负责清屏 |
| 进度 UI | 仍由 `DontDestroyOnLoad` 里的 UI Loading 面板承担（UIRoot 是 DDOL，不受 Single 影响；面板在 `Canvas_HighFrequency`=100，盖在 LoadingScene 的 Canvas=0 之上） |
| 新增诊断 | `ISceneService.LoadedSceneNames` + `切换完成：X（已加载场景：…）` 日志（让共存状态可观测）；两个实现与测试替身均已同步 |

**实施中自查出的两个错误（无测试可跑时靠推演发现，已修正）**：

1. 最初把「先卸后载」放在预加载**之后** —— 那样内存峰值仍是「旧场景 + 新资源」，策略**完全失效**。已移到预加载之前。
2. 最初给步骤 7 加了 `IsSceneLoaded(prevScene)` 守卫去跳过 Single 下的卸载 —— 那会导致 **Addressables 句柄泄漏**。已改为照常调用、由服务端区分语义。

**代价（已知并接受）**：`UnloadThenLoad` 下加载失败**没有回退**（旧场景已卸），失败时会打两条错误日志明确说明。

**验证状态（2026-10-12 补跑）**：

- ✅ 编译**零错误**
- ✅ **68 例 EditMode 全绿**（原 64 → 新增 4：Single 经 Loading 场景 / 已加载 Loading 先释放 / 启动不常驻 / Additive 仍常驻）
- ✅ **Play 实测**：`SampleScene →(Single)→ Level2` 日志 `[SceneFlow] Single 过渡：加载 Loading 场景（关闭旧场景 SampleScene）→ [Scene] Addressables 加载：LoadingScene（Single，activateOnLoad=false）→ [Scene] Addressables 加载：Level2（Single，activateOnLoad=false）→ 切换完成：Level2（已加载场景：Level2）`；`Level2 →(Additive)→ SampleScene` 也正常（含「按需加载 Loading 场景」）。无 error/warning。

### 12.5 策略收敛（2026-10-12，用户提案）：`loadMode` + `swapPolicy` → `SceneTransitionPolicy`

**动机**：Loading UI 面板本来就是 **DDOL 的 UIManager 面板**（`isGlobal=1`，不受任何场景卸载影响，见 `LoadingPanelConfig` 与 `UIRootCreator` 的 `DontDestroyOnLoad`）。因此「是否显示加载画面」与加载模式**无关**；两种模式真正的差异只有「**旧场景何时释放**」。原设计用「每条场景的 `loadMode`」×「全局 `swapPolicy`」两个旋钮表达它，既耦合又表达不全（缺了「Single + 经中间场景」）。

**新模型**：单一全局枚举 `SceneTransitionPolicy`，等价于一个 2×2 矩阵：

| | 激活新场景时释放旧 | 加载新场景前释放旧 |
|---|---|---|
| **Single**（无共存窗口） | `SingleDirect`（默认） | `ThroughLoadingScene`（A→Loading→B） |
| **Additive**（有共存窗口） | `Additive`（逃生舱） | `AdditiveUnloadFirst`（逃生舱） |

**落地**：

- `SceneDefinition.LoadMode` 与 `SceneConfigSO.loadMode` **删除**；`ISceneCatalog.SwapPolicy` → `TransitionPolicy`；`SceneConfigAssetSO.swapPolicy` → `transitionPolicy`。
- `SceneFlow.SwitchSceneRoutine` 按策略分支：`targetMode = singleBased ? Single : Additive`；**Single 系自动跳过共存补丁**（`MuteAudioListeners` / `DisableLightsOf`）—— 因为引擎在激活时已原子关闭旧场景。
- **安全阀**：策略为 Single 系但目录里存在 `persistent` 场景 → `EffectiveTransitionPolicy()` 告警并**自动改用 `Additive`**（Single 会摧毁常驻场景，不能静默失败）。
- 校验：V8 改为「选了依赖中间画面的策略却没配 `loadingSceneName`」；新增 V9（Single + persistent）与 V10（运行时无相机）。
- 样例：`SceneCatalog.asset` 用 `transitionPolicy: 1`（`ThroughLoadingScene`）；`SceneConfig_LoadingScene.persistent` 改为 `0`（Single 系不支持常驻）。

**验证**：**70 例 EditMode 全绿**；Play 启动 + 双向切换日志均显示 `policy=ThroughLoadingScene` → `过渡：加载 Loading 场景（关闭旧场景 …）` → 目标以 `Single` 加载 → `已加载场景：<目标>`（全程 1 个场景），无 error。

