# MyFramework · Unity 游戏框架（5 分钟接入）

> **一句话**：分层 + DI + 模块化的 Unity 框架 —— 新做一个游戏 demo 只需要**加文件**，不需要改框架或启动代码。

| 项 | 说明 |
|---|---|
| 引擎 | Unity 2022.3 LTS（URP / Addressables / Input System / TextMeshPro） |
| 分层 | `Core`（无引擎依赖）→ `Abstractions`（纯接口）→ `Adapters`（Unity 实现）→ `Bootstrap`（启动/场景流）→ `Game`（业务） |
| 扩展方式 | 反射自动发现 `IServiceModule` / `IConfigFactory` / `ILoadStepProvider` / `BootstrapRunner`，**新增功能零改框架代码** |
| 质量 | 30 例 EditMode 测试全绿；控制台启动无异常/无警告 |

---

## 0. 先跑起来（3 步）

1. 用 Unity **2022.3** 打开本工程；
2. 打开 `Assets/Scenes/SampleScene.unity`；
3. 按 **Play**（不用挂任何脚本 —— `FrameworkStartEntry` 会自动装配整个框架）。

**自检用的预期日志**：

```text
框架启动中...
[Bootstrap] 使用框架默认引导流程
[Modules] 扫描完成：全局模块 2 个，场景模块 1 个
[Modules] 已注册全局模块 CoreServiceModule（Order=-1000）
[Modules] 已注册全局模块 DemoGlobalModule（Order=100）
[Config] 发现配置 2 个：UIConfig(order=-100), DemoTuning(order=0)
[Bootstrap] 预热配置…
[Bootstrap] 加载配置…
[Bootstrap] 注册 UI 面板…
UI 配置已应用：参考分辨率 1920×1080，MatchWidthOrHeight=0.5
[UI] UIRoot 缩放：参考分辨率 1920×1080，MatchWidthOrHeight=0.5（0→按宽，1→按高）
已注册 UI 面板：2 个
加载 Loading 场景：LoadingScene
隐藏 Loading 场景
Loading 场景就绪（常驻，默认隐藏）
场景容器建立：SampleScene
启动完成
```

> 若从其它场景（例如 `LoadingScene`）运行，会看到真实切换流程，并额外出现
> `开始切换：<none> → SampleScene` / `显示 Loading 场景` / `[Load] 预加载面板：N 个（…）` / `切换完成：SampleScene`。

---

## 1. 接入到自己的工程（≈5 分钟）

### 1.1 拷贝框架程序集

把 `Assets/Scripts/Framework/` 整个目录拷到你的工程（含 `Core` / `Abstractions` / `Adapters` / `Bootstrap` 四个 asmdef）。

### 1.2 引导流程：框架已内置，业务不用写

拷贝框架后**什么都不用做**就能跑：`Framework/Bootstrap/FrameworkStartEntry` 会在游戏启动前
（`RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`）自动创建常驻根节点 `[GameFramework]`，
交由 `GameRoot` → `BootstrapRunner`（框架通用引导流程）完成装配：

```text
模块扫描注册 → 日志门面 → 启动加载管线（预热配置 → 加载配置 → 业务步骤）
→ 创建/装配 SceneFlow → 常驻 Loading 场景 → 进入口场景 → 启动完成
```

需要定制时按需选**一种**（都不需要改框架代码）：

| 想改什么 | 怎么做 |
|---|---|
| 入口场景 / 常驻 Loading 场景名 | 实现 `IBootstrapOptionsProvider`（示例 `Game/Bootstrap/GameBootstrapOptions.cs`） |
| 启动阶段额外加载内容 | 实现 `IStartupLoadStepProvider`（示例 `Game/Bootstrap/UIPanelSetupStep.cs`） |
| 进入场景前预加载内容 | 实现 `ISceneLoadStepProvider` |
| 想改引导流程本身 | 继承 `BootstrapRunner` 并覆写钩子（`CreateOptions` / `CollectStartupSteps` / `CreateHooks` / `OnStartupCompleted` / `OnStartupProgress`）；框架会**自动发现**它（最多只能有一个实现） |

> `GameRoot.CreateRunner()` 会反射查找业务自定义的 `IBootstrapRunner`，找不到就用框架默认实现。
> 入口场景名留空时，默认取 **Build Settings 的第一个场景**。

### 1.3 程序集与扫描规则（重要）

| 规则 | 说明 |
|---|---|
| 程序集名前缀 | 只有以 `Abstractions` / `Adapters` / `Bootstrap` / `Game` / `Assembly-CSharp` **开头**的程序集会被扫描 |
| 跳过 | 名字含 `-Editor` 的程序集（如 `Game-Editor`） |
| 约束 | 模块类 / `IConfigFactory` 类需要**公开无参构造函数** |
| 顺序 | 按 `Order` 升序注册；`Order` 相同则按扫描顺序（后注册覆盖先注册） |

> 业务代码建议直接放 `Assembly-CSharp`，或自己建一个 `Game` 程序集。若你的程序集名不满足上述前缀，可在启动时显式传前缀：`ServiceModuleRegistry.Scan(new[] { "MyGame" })`。

---

## 2. 四条日常接入路径（都不改框架）

### 2.1 新增一个服务（2 个类）

```csharp
public interface IAnalytics { void Track(string evt); }

internal sealed class Analytics : IAnalytics
{
    private readonly ILogger _logger;
    public Analytics(ILogger logger) => _logger = logger;   // 构造函数注入，自动解析
    public void Track(string evt) => _logger.Info($"[Analytics] {evt}");
}

public sealed class AnalyticsModule : IGlobalServiceModule
{
    public int Order => 100;   // 框架核心模块是 -1000；更大的值可用于覆盖默认实现
    public void RegisterGlobal(IServiceRegistry registry)
        => registry.Register<IAnalytics, Analytics>();
}
```

放到 `Game` 程序集 → 启动时自动注册。取用：

```csharp
var analytics = ServiceLocator.Get<IAnalytics>();        // 静态上下文
var analytics = resolver.Resolve<IAnalytics>();          // 已持有 resolver
resolver.TryResolve<IAnalytics>(out var a);               // 不想抛异常时
```

### 2.2 新增一个场景级服务（每个场景各一份）

```csharp
public sealed class LevelModule : ISceneServiceModule
{
    public int Order => 100;
    public void RegisterScene(IServiceRegistry registry, string sceneName)
        => registry.Register<ILevelContext>(new LevelContext(sceneName));
}
```

每个场景会建一个独立子容器；**场景卸载时容器被 `Dispose`**（其中的 `IDisposable` 服务会被释放）。

### 2.3 新增一个 UI 面板（4 步）

**① 脚本**（继承 `UIPanelBase` + `[UIPanel("key")]`）：

```csharp
[UIPanel("ShopPanel")]
public sealed class ShopPanel : UIPanelBase
{
    protected override void OnCreate(IEventBus bus, ILogger logger)
    {
        base.OnCreate(bus, logger);
        Subscribe<GoldChangedEvent>(e => UpdateGold(e.Value));   // 面板销毁时自动退订
    }

    protected override void OnShow() { /* 打开时刷新 */ }
    protected override void OnHide() { /* 关闭时收尾 */ }

    private void UpdateGold(int value) { /* ... */ }
}
```

**② 做预制体**：把脚本挂到预制体根上即可（**不需要自带 Canvas** —— 层级 Canvas 由框架运行时创建）。

**③ 加入 Addressables**：`Assets/Prefabs/UI/ShopPanel.prefab` → 加入组 `Default Local Group`，并把地址改成短地址 `UI/ShopPanel`（地址栏默认填的是资产路径，建议手工改短；**这个地址就是下面 `PrefabAddress` 要填的值**）。

**④ 在 `UIConfigAsset.asset` 的 `panels` 里加一条 `UIPanelConfigSO`**：

| 字段 | 示例 | 说明 |
|---|---|---|
| `panelKey` | `ShopPanel` | 必须与 `[UIPanel("...")]` 一致 |
| `PrefabAddress` | `UI/ShopPanel` | Addressables 地址 |
| `Layer` | `Popup` | 决定挂到哪层 Canvas（见下表） |
| `cacheOnClose` | `true` | 关闭时只隐藏、不销毁 |
| `isGlobal` | `false` | `false` = PerScene，场景退出时自动销毁 |
| `sceneTags` | 空 | 空 = 所有场景可用 |
| `loadPolicy` | — | 加载策略 |

打开面板：

```csharp
await ServiceLocator.Get<IUIService>().OpenAsync<ShopPanel>();
```

**层级与 sortingOrder**（`UIRootCreator` 自动创建）：

| 层 | Canvas 名 | sortingOrder | 用途 |
|---|---|---|---|
| `Background` | `Canvas_Background` | -100 | 静态背景 |
| `Normal` | `Canvas_Normal` | 0 | 低频 UI（主菜单、设置） |
| `HighFrequency` | `Canvas_HighFrequency` | 100 | 高频刷新（HUD、血条） |
| `Popup` | `Canvas_Popup` | 200 | 弹窗 |
| `Toast` | `Canvas_Toast` | 300 | 轻提示 |
| `Overlay` | `Canvas_Overlay` | 400 | 顶层遮罩/转场 |

**分辨率**由 `UIConfigAsset` 的 `referenceResolution` / `matchWidthOrHeight` 驱动（写入 `UISettings`），不用改代码。

**场景预加载**：把面板设为 `isGlobal = false` + `loadPolicy = Preload`（可选填 `sceneTags` 限定场景），
框架会在**进入该场景前**把它预加载到内存（隐藏态），进场景后 `Open<T>()` 即为瞬开。
日志形如：`[Load] 预加载面板：1 个（ShopPanel）`。

### 2.4 新增一个配置（0 行改动）

```csharp
// ① 配置本体（纯代码配置，不需要任何资产）
public sealed class DemoTuningConfig : GameConfigBase
{
    public DemoTuningConfig() : base("DemoTuning", loadOrder: 0) { }
    public int StartGold { get; private set; } = 100;
    public override void Load(IAssetService assetService) => IsLoaded = true;
}

// ② 工厂（被自动发现，无需在任何地方登记）
public sealed class DemoTuningConfigFactory : IConfigFactory
{
    public IGameConfig CreateConfig() => new DemoTuningConfig();
}
```

- 若配置需要读取 **ScriptableObject 资产**：用 `ConfigBySO<TMyConfigSO>("Assets/.../MyConfig.asset")`，并把资产加入 Addressables；
- 启动时会打印 `[Config] 发现配置 N 个：...`；
- 读取：`configService.Get<DemoTuningConfig>().StartGold` 或 `configService.TryGet<DemoTuningConfig>(out var cfg)`；
- `loadOrder` 决定 `LoadAll` 的顺序（升序）。

### 2.5 新增一个加载步骤（启动 / 场景预加载）

启动与「进入场景前的预加载」**共用同一套加载步骤模型**：

```csharp
// 一个步骤 = 一段可组合的异步加载逻辑（可上报进度）
public sealed class LoadRemoteTableStep : ILoadStep
{
    public string Name => "拉取远程配置表";

    public IEnumerator Run(ILoadContext ctx)
    {
        var assets = ctx.Services.Resolve<IAssetService>();
        var task = assets.LoadAsync<TextAsset>("remote/table.json");
        while (!task.IsCompleted) { ctx.Report(Name, 0.5f); yield return null; }
        // …解析、写入 IStorage / 注册配置…
        ctx.Report(Name, 1f);
    }
}

// 注册步骤：启动阶段 与/或 场景预加载阶段（放在被扫描的程序集即可，自动发现）
public sealed class RemoteTableSteps : IStartupLoadStepProvider, ISceneLoadStepProvider
{
    public void Collect(IList<ILoadStep> steps, ILoadContext ctx)
    {
        steps.Add(new LoadRemoteTableStep());

        // 场景专属资源已由配置驱动（SceneCatalog.asset 的 preloadAddresses），不用写在这。
        // 这里只放「配置表达不了」的步骤，例如按场景预热某个业务分类：
        if (ctx.SceneName == "Level2")
            steps.Add(new PreloadAddressesStep("Level2 BGM", new[] { "Audio/Boss_BGM" }));
    }
}
```

要点：

- **加类即接入**，不用改 `BootstrapRunner` / `SceneFlow`；
- 需要精确顺序时，在**同一个** provider 里依次 `steps.Add`；
- 默认管线：启动 = `预热配置 → 加载配置 → 业务步骤`；场景 = `按 loadPolicy 预加载面板 → 按场景配置预加载专属资源 → 业务步骤`；
- **场景专属资源优先用配置**（`SceneCatalog.asset` 的 `preloadAddresses`，见 2.9）—— provider 只留给配置表达不了的场景；
- 要彻底改流程，继承 `BootstrapRunner` 覆写 `CollectStartupSteps`；
- 单个步骤抛异常只记日志，**不会中断整条管线**。

### 2.6 切换场景

```csharp
ServiceLocator.Get<ISceneFlow>().SwitchScene("Level2");
```

- 目标场景**必须加入 Build Settings**（用 Addressables 版服务时还需标记为 Addressable、地址=场景名，见 2.7）；
- 若目标场景已在内存中（例如编辑器已打开它），框架会**复用现有实例**而不会重复 Additive 加载（避免出现两套相机 / AudioListener）；
- 切换进度刻度：进入 **5%→30%** 跑场景预加载（见 2.5），**30%→90%** 等场景激活，**90%→100%** 收尾；
- 不需要 Loading 场景时，`BootstrapOptions.LoadingSceneName` 传 `null`；
- ⚠️ **必须在启动完成后再调用**：启动未完成时调用会被拒绝并告警（否则会与启动流程并发跑两条切换协程）；
- 切换期间新旧场景共存，框架会在旧场景卸载前**自动静音其 `AudioListener`**、并**关闭其灯光**（否则两套主光叠加会“瞬间偏亮”），避免切换出戏；
- 本工程内置演示：`SampleScene` ↔ `Level2`（运行模式下菜单 `Tools/我的工具/场景切换/...` 即可验证，两侧各自有专属预加载）。

**场景生命周期钩子**（可选）：实现 `ISceneFlowHooks`：

```csharp
RegisterSceneServices(container, sceneName)       // 建场景容器时（默认转发给场景模块注册）
SceneResourcePreload(resolver, sceneName, report) // 切换进度 5%→30%；report(0..1) 上报局部进度
SceneResourceRelease(resolver, sceneName)         // 旧场景卸载后（默认会清理 PerScene 面板）
OnLoadingBegin / OnLoadingProgress(同步) / OnLoadingEnd   // Loading 遮罩显隐与进度
```

> 默认实现 `DefaultSceneFlowHooks` 已经做了：Loading 面板显隐、场景模块注册、场景预加载、PerScene 面板清理。
> 通常**不需要**自己写钩子；只想加预加载内容的话用 2.5 的 `ISceneLoadStepProvider`。

---

### 2.7 切换场景加载方式（Build Settings ↔ Addressables）

框架默认用 `UnitySceneService`（按 **Build Settings** 加载）。若场景需要**远端打包 / 热更**，可换成 `AddressablesSceneService`（按**地址**加载，地址默认 = 场景名）—— 只需一个模块覆盖默认实现：

```csharp
public sealed class GameSceneServiceModule : IGlobalServiceModule
{
    public int Order => 0;   // 大于框架核心模块(-1000) → 后注册者覆盖
    public void RegisterGlobal(IServiceRegistry registry)
        => registry.Register<ISceneService, AddressablesSceneService>();
}
```

- 示例见 `Game/Modules/GameSceneServiceModule.cs`（**本工程已启用**）；删除该类即回退到 Build Settings 方式；
- 前提：场景资产已标记为 Addressable，且**地址与场景名一致**（本工程的 `SampleScene` / `LoadingScene` 即如此）；
- 日志可确认走的是哪条路：`[Scene] Addressables 加载：SampleScene（Additive，activateOnLoad=false）`。

---

### 2.8 让面板只在某些场景可用（面板场景标签）

面板配置里的 `sceneTags` 决定「此面板属于哪些场景」：

| 配置 | 行为 |
|---|---|
| `sceneTags` 为空 | 任何场景都可打开 |
| `isGlobal = true` | 忽略 `sceneTags`（全局面板不属于任何场景） |
| `sceneTags = [SampleScene]` | 在 `SampleScene` 可开；在其他场景打开会被**拒绝并告警** |

```csharp
// 启动步骤里可关掉「强制拦截」，改成只告警（默认 true = 拦截）
services.Register(new UISettings { EnforceSceneTags = false });
```

同一个 `sceneTags` 还会和 `loadPolicy` 配合，决定**进入场景前要不要预加载**那个面板：

```text
loadPolicy = Preload + isGlobal = false + sceneTags 包含目标场景  →  进场景前预实例化并隐藏
```

> 小贴士：**预加载筛选看的是「目标场景」**（切换中 ActiveScene 还是旧场景），
> 而**打开时的校验看的是「当前活动场景」** —— 两者职责不同，框架分别处理。

---

### 2.9 把场景做成配置（与 UI 对称）

场景也可以像 UI 面板一样「只改配置、不改代码」：

```text
SceneCatalog.asset（总表：入口场景 / Loading 场景 / 切换策略 / 登记方式）
  └─ SceneConfigSO（单项：场景名 / 地址 / 加载模式 / 常驻 / 是否显示 Loading
                     / 专属预加载地址 / 标签 / 容器等待超时）
```

新增一个场景只需四步：

1. 建场景，在 `SceneCatalog.asset` 里加一条 `SceneConfigSO`（`sceneName` 有**下拉**可直接选）；
2. 点检视面板的 **「同步场景登记」** 按钮 → 自动写进 Addressables（或 Build Settings）；
3. 要专属预加载 → 直接在配置里填 `preloadAddresses`（**不用写代码**）；
4. 要专属面板 → 面板配置的 `sceneTags` 填该场景名。

运行期查询：

```csharp
var def = ServiceLocator.Get<ISceneFlow>().GetDefinition("Level2");
if (def != null && def.HasTag("level")) { /* ... */ }
```

要点：

- **零配置也能跑**：没建配置时，任何场景回退为「Additive / 不常驻 / 显示 Loading」，行为与以前完全一致；
- 启动期会校验配置并告警（入口未登记 / 重复场景名 / 地址缺失…），详见 `SCENE_CONFIG_DESIGN.md`；
- **`transitionPolicy`（全局，**一个旋钮**）**：把原来的「每条场景的 `loadMode`」与「全局 `swapPolicy`」**合并成一个枚举**，统一回答一个问题：**旧场景何时释放**；
- ⚠️ `Single*` 策略**不支持 `persistent` 场景**（引擎会摧毁它们）；框架检测到这种组合会**告警并自动改用 `Additive`**（不静默做错事）。

### 2.10 切场景的两种加载模式 / 两种时序（选读）

| `transitionPolicy` | 效果 | 内存峰值 | 共存窗口 |
|---|---|---|---|
| `SingleDirect`（**默认**） | 直接以 Single 加载目标（旧场景到目标就绪才关闭） | 旧 + 新 | 无 |
| `ThroughLoadingScene` | **A → Loading 场景 → B**（先 Single 加载 Loading 关旧，再 Single 加载目标） | **最低** | 无 |
| `Additive` | 以 Additive 加载目标，激活后卸旧 | 旧 + 新 | **有**（框架自动静音旧监听器/关旧灯光） |
| `AdditiveUnloadFirst` | 先卸旧，再以 Additive 加载目标 | 最低 | 无 |

- `Single*` 是**推荐模型**（无共存窗口 → 不需要音频/灯光补丁）；`Additive*` 是**逃生舱**（只有它们支持常驻场景 / 子场景 / 交叉淡化）。
- ⚠️ `ThroughLoadingScene` 与 `AdditiveUnloadFirst` 依赖「旧场景释放后的独立画面」，需要配 `loadingSceneName` 且该场景**自带相机**；缺失时框架会告警（后者还会**回退为 `Additive`**）。

判据（日志）：切换完成那行会列出当前已加载的场景，例如
`切换完成：Level2（已加载场景：LoadingScene、Level2）`。

## 3. 常用 API 速查

| 想做的事 | API |
|---|---|
| 取服务 | `ServiceLocator.Get<T>()`、`resolver.Resolve<T>()`、`resolver.TryResolve<T>(out T)` |
| 注册服务（模块内） | `registry.Register<TImpl>()`（单例，依赖自动注入）、`registry.Register(instance)`、`registry.RegisterFactory<T>(r => ...)` |
| 事件 | `IEventBus.Subscribe<T>(h)` / `Publish(evt)`；面板内用 `Subscribe<T>()`（自动回收） |
| UI | `IUIService.OpenAsync<T>()` / `Open<T>()` / `Close<T>()` / `CloseAll()` / `PreloadAsync<T>()` / `IsOpen<T>()` |
| 资源 | `IAssetService.Load<T>()`（同步）、`LoadAsync<T>()`（推荐）、`PreloadAsync()`、`ReleasePreloaded()` |
| 配置 | `IGameConfigService.Get<T>()` / `TryGet<T>(out T)` |
| 场景 | `ISceneFlow.SwitchScene(name)`、`CurrentSceneName`、`Current`（当前场景容器） |
| 存储 | `IStorage.Save<T>()` / `Load<T>()` / `Delete()` / `Clear(prefix)` |
| 日志 | 优先构造函数注入 `ILogger`；静态代码用 `Log.Info/Warn/Error`（需先 `Log.Init`） |
| 加载步骤 | `ILoadStep` / `ILoadContext`（`ctx.Services`、`ctx.SceneName`、`ctx.Report`）；提供者 `IStartupLoadStepProvider` / `ISceneLoadStepProvider` |
| 引导参数 | `IBootstrapOptionsProvider.CreateOptions()` → `EntrySceneName` / `LoadingSceneName` / `ScanPrefixes` |

> 命名冲突提醒：`Framework.Abstractions.Logging.ILogger` 与 `UnityEngine.ILogger` 同名，请用
> `using ILogger = Framework.Abstractions.Logging.ILogger;`

---

## 4. 目录与分层

```text
Assets/Scripts/
├── Framework/
│   ├── Core/           # 无引擎依赖内核：DI 容器 / 服务定位 / 描述符
│   ├── Abstractions/   # 纯接口与数据契约
│   │   ├── Assets/ Events/ GameConfig/ Loading/ Logging/ Modules/ Scenes/ Storage/ UI/
│   │   └── Loading/    # ILoadStep / ILoadContext / ILoadStepProvider / LoadPipeline + 内置步骤
│   ├── Adapters/       # Unity 实现（Addressables / 场景 / 存储 / UI / 日志）+ CoreServiceModule
│   └── Bootstrap/      # FrameworkStartEntry / GameRoot / BootstrapRunner / SceneFlow
│                       # + 自动发现：ServiceModuleRegistry / GameConfigRegistry / LoadStepCollector
├── Game/               # 业务层（示例，只有“提供者”类）
│   ├── Bootstrap/GameBootstrapOptions.cs    # 引导参数（场景名已交给配置，此处留空）
│   ├── Bootstrap/UIPanelSetupStep.cs        # 启动步骤：应用 UI 配置 + 注册面板
│   ├── Bootstrap/SceneCatalogSetupStep.cs   # 启动步骤：场景配置 → ISceneCatalog + 校验
│   ├── Scenes/Config/                       # 场景配置（SceneConfigSO / SceneConfigAssetSO / Factory / Validation）
│   ├── Editor/                              # 编辑器工具（Game-Editor）：同步登记 + 场景下拉 + 校验按钮
│   ├── Modules/DemoGlobalModule.cs          # 示例：全局 + 场景级服务
│   ├── Modules/GameSceneServiceModule.cs    # 示例：把场景服务切为 Addressables 版
│   ├── Config/DemoTuningConfig.cs           # 示例：纯代码配置 + IConfigFactory
│   └── UI/                                  # 面板（LoadingPanel / DemoPanel）+ UIConfigAsset
├── Fonts/              # 中文字体（方正小标宋简.TTF + 生成的 CNFont SDF.asset）
├── UI/Tools/           # LoadingDots / ProgressBar 小工具（命名空间 Game.UI.Tools）
└── Tests/              # EditMode 测试（61 例）
```

**依赖方向（不可逆）**：`Core` ← `Abstractions` ← `Adapters` ← `Bootstrap` ← `Game`。
`Core` 是 `noEngineReferences` 的，业务层不直接依赖 Addressables。

---

## 5. 实战踩坑清单

1. **场景必须加入 Build Settings**，否则 `LoadAsync` 抛异常。
2. **业务场景不要再自带 `EventSystem`** —— 框架会自动创建常驻的；两个会报 `There can be only one active Event System`。
3. 程序集名必须满足 1.3 的前缀规则，否则模块/配置**扫不到**（且不会有报错，只会静默少注册）。
4. 模块类 / `IConfigFactory` 需要**公开无参构造函数**。
5. 面板脚本必须实现 `IUIPanelLifecycle`（推荐继承 `UIPanelBase`），否则 `UIService` 会报「未实现 IUIPanelLifecycle」。
6. `UIRoot` 是**懒创建**的（首次实例化面板时），且位于 `DontDestroyOnLoad` 场景 → 场景层级面板/搜索工具**看不到它**，排查请用日志。
7. 非全局面板请设 `isGlobal = false`；否则切场景后不会随场景清理（默认由 `SceneResourceRelease → DisposeScenePanels()` 处理 PerScene 面板）。
8. 首次 Addressables 加载前框架已做预热，避免同步 `WaitForCompletion` 与初始化互锁；**新增配置请走 `IConfigFactory`**（会被一并预热），不要自己在别处同步加载。
9. `LoadingScene` 作为**常驻叠加层**使用时必须是纯 UI（**不要放 Camera / Directional Light**，也不需要 EventSystem）；框架会在加载完成后把它整体 `SetActive(false)`，只在切换期间显示。
10. 用 MCP/自动化驱动 Play 验证时，编辑器失焦会暂停播放循环（`Application.runInBackground` 默认 `false`）→ 先置 `true`，否则看到的是「启动卡死」假象。
11. Play 模式下不能用 `EditorSceneManager` 打开场景（自动化工具的 `manage_scene load` 会报错）。
12. `Log.Init(null)` 会被忽略（不会覆盖已有实现）；`Log.Clear()` 会复位以便重新注入。
13. **入口场景已经是当前场景时不会触发场景预加载**（走「复用」快路径）。想验证预加载，从其它场景（如 `LoadingScene`）运行，或调用 `SwitchScene`。
14. `IBootstrapRunner` **最多只能有一个**自定义实现；多个会报错并只用其中一个。
15. 加载步骤提供者 / 引导参数提供者都需要**公开无参构造函数**。
16. **中文字体已接入**：`Assets/Fonts/CNFont SDF.asset`（由 `方正小标宋简.TTF` 生成的 **Dynamic** TMP 字体，按需生成字形）已加入 `LiberationSans SDF` 的**回退字体表** → 用默认字体的 TMP 文本都能直接显示中文。若你换了默认字体，记得同样挂回退表。
17. **面板 `sceneTags` 不匹配会直接拒绝打开**（返回 null 并告警）：`DemoPanel` 若只在 `SampleScene` 下能开，换个场景打开就是“叫了没反应”。改配置或关掉 `UISettings.EnforceSceneTags`（见 2.8）。
18. **启动未完成时调 `SwitchScene` 会被拒**（告警“启动尚未完成”）：等启动日志出现 `启动完成` 后再调。
19. 用 MCP/自动化读控制台时，`read_console` 的 `count` 是「从缓冲区头部取 N 条」，且可能拿到**较早的快照** → 验证一条切换是否走完，**先 `clear` 再触发再读**最可靠（否则会误以为日志缺失）。
20. **从「非入口场景」按 Play 时，框架会把入口场景加载进来、并卸载你这个初始场景**（这是正确行为，否则它会带着自己的相机/灯光/AudioListener 常驻）。想在那个场景里调试，就把它设为入口场景（`GameBootstrapOptions.EntrySceneName`）。判据：启动日志应为 `开始切换：<初始场景> → <入口场景>`；若显示 `开始切换：<none> → …`，说明旧场景没有被识别（旧版本缺陷）。
21. **切换后光照变了不一定是 bug**：`RenderSettings`（环境光/天空盒/雾）和 Light 都是**场景自带数据**，切过去就用目标场景的配置。请在两个场景里把灯光参数对齐（本工程的 `Level2` 是新建场景默认的 `intensity=1`/暖白，而 `SampleScene` 是 `2`/纯白）。框架只负责**过渡期不叠加**（日志：`关闭旧场景灯光：…`），不负责统一各场景的光照。
22. 🔴 **Build Settings 与 Addressables 对场景是互斥的**：场景一旦标记为 Addressable，Unity 会**自动把它在 Build Settings 里禁用**（`enabled=false` + 控制台警告）。所以不要指望「两处都登记、两条路都能跑」—— 本工程走 Addressables（`AddressablesSceneService`），Build Settings 里 3 个场景全是 `enabled=false`。配置里用 `registration` 字段声明走哪条路，同步工具只会写一条。
23. **场景配置是可选的**：没建 `SceneCatalog.asset` 时一切照旧（所有场景按「Additive / 不常驻 / 显示 Loading」处理）。想从旧行为迁移，就先把现有场景登记进去、再逐步加 `preloadAddresses` 等字段。

---

## 6. 更多

- **完整工程文档**（架构、文件地图、问题清单、进度、命名约定）：[`PROJECT_GUIDE.md`](./PROJECT_GUIDE.md)
- **场景配置化设计与实施记录**：[`SCENE_CONFIG_DESIGN.md`](./SCENE_CONFIG_DESIGN.md)
- **跑测试**：Test Runner → **EditMode**（共 61 例：容器 18 / UIService 12 / SceneFlow 16 / SceneCatalog 5 / SceneConfig 9 / 示例 1）
- **接入成本实测**：见 `PROJECT_GUIDE.md` §12（新增服务 / 面板 / 配置三条路径的实测记录）
