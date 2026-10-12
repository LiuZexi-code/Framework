using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Abstractions.Events;
using Framework.Abstractions.Logging;
using Framework.Abstractions.Scenes;
using Framework.Abstractions.UI;
using Framework.Adapters.UI;
using Framework.Adapters.Scenes;
using Framework.Bootstrap;
using Framework.Core;
using Framework.UI;
using NUnit.Framework;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

/// <summary>
/// SceneFlow 场景级集成测试（用假的 ISceneService / ISceneFlowHooks 驱动协程，不加载真实场景），
/// 以及「场景退出时清理 PerScene 面板」的联动测试。
/// </summary>
public class SceneFlowTests
{
    /// <summary>单次 Drain 允许的最大步数：把「协程死循环」变成断言失败，而不是挂死测试进程。</summary>
    private const int MaxSteps = 500;

    /// <summary>测试中动态创建的 GameObject（灯光等），统一在 TearDown 清理，避免污染编辑器场景。</summary>
    private readonly List<GameObject> _spawned = new();

    [TearDown]
    public void CleanupSpawnedObjects()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null) UnityEngine.Object.DestroyImmediate(_spawned[i]);
        }
        _spawned.Clear();
    }

    /// <summary>造一个带 <see cref="Light"/> 的根物体（模拟业务场景里的主光），返回其 Light 组件。</summary>
    private Light SpawnLight(string name)
    {
        var go = new GameObject(name);
        _spawned.Add(go);
        return go.AddComponent<Light>();
    }

    /// <summary>造一个带 <see cref="AudioListener"/> 的根物体（模拟业务场景里的监听器），返回其 AudioListener 组件。</summary>
    private AudioListener SpawnListener(string name)
    {
        var go = new GameObject(name);
        _spawned.Add(go);
        return go.AddComponent<AudioListener>();
    }

    /// <summary>造一个带 <see cref="Camera"/> 的根物体（模拟 Loading 场景里的「兜底画面」相机）。</summary>
    private Camera SpawnCamera(string name)
    {
        var go = new GameObject(name);
        _spawned.Add(go);
        return go.AddComponent<Camera>();
    }

    /// <summary>
    /// 测试用的协程运行器：同步把整条协程跑完。
    /// 注意：Unity 的运行器会自动展开嵌套的 <c>yield return IEnumerator</c>，
    /// 手动驱动时必须自己用栈展开，否则内层协程根本不会执行。
    /// </summary>
    private static Coroutine Drain(IEnumerator routine)
    {
        if (routine == null) return null;

        var stack = new Stack<IEnumerator>();
        stack.Push(routine);

        int steps = 0;
        while (stack.Count > 0)
        {
            var current = stack.Peek();
            if (current.MoveNext())
            {
                if (++steps > MaxSteps)
                    Assert.Fail($"协程未在 {MaxSteps} 步内结束（疑似死循环）");

                if (current.Current is IEnumerator nested)
                    stack.Push(nested);
            }
            else
            {
                stack.Pop();
            }
        }
        return null;
    }

    private static SceneFlow CreateFlow(FakeSceneService scene, RecordingHooks hooks, ISceneCatalog catalog = null)
        => new SceneFlow(new ServiceCollection(), scene, hooks, Drain, loadingSceneName: null, catalog: catalog);

    // ---------------- 测试替身 ----------------

    private sealed class FakeLoadOperation : ISceneLoadOperation
    {
        private readonly Action _onActivate;
        public FakeLoadOperation(Action onActivate) => _onActivate = onActivate;

        public float Progress => 1f;
        public bool IsPrepared => true;
        public bool IsDone { get; private set; }

        public void Activate()
        {
            _onActivate?.Invoke();   // 模拟 Unity 的 sceneLoaded 时机
            IsDone = true;
        }
    }

    private sealed class FakeSceneService : ISceneService
    {
        private readonly HashSet<string> _loaded = new();
        private readonly Dictionary<string, List<GameObject>> _roots = new();

        public readonly List<string> LoadRequests = new();
        public readonly List<string> UnloadRequests = new();

        /// <summary>卸载某场景的**瞬间**，该场景各灯光是否处于启用状态（用于断言「过渡期已关闭旧场景灯光」）。</summary>
        public readonly Dictionary<string, bool[]> LightEnabledAtUnload = new();

        /// <summary>按发生顺序记录 <c>load:xx</c> / <c>unload:xx</c>，用于断言时序（如「先卸后载」）。</summary>
        public readonly List<string> Sequence = new();

        /// <summary>最近一次加载请求的模式（用于断言 Single 是否透传）。</summary>
        public SceneLoadMode LastLoadMode { get; private set; }

        /// <summary>按发生顺序记录每次加载请求的模式（用于断言「Loading 场景以 Single 加载」）。</summary>
        public readonly List<SceneLoadMode> LoadModes = new();

        /// <summary>当前已加载的场景名（用于断言 Single 之后只剩一个场景）。</summary>
        public IReadOnlyList<string> LoadedSceneNames
        {
            get
            {
                var names = new List<string>();
                foreach (var name in _loaded) names.Add(name);
                return names;
            }
        }

        /// <summary>抑制 sceneLoaded 事件 → 模拟「激活成功但场景容器永远建不起来」（用于超时测试）。</summary>
        public bool SuppressSceneLoadedEvent;

        /// <summary>SetActiveScene 被调用时的回调（用于断言调用顺序，见 B1 回归测试）。</summary>
        public System.Action<string> SetActiveSceneCallback;

        public string ActiveSceneName { get; private set; } = string.Empty;

        public event Action<SceneLoadedEvent> SceneLoaded;
        public event Action<SceneUnloadedEvent> SceneUnloaded;

        /// <summary>模拟「该场景已在内存中」（例如编辑器已打开入口场景）。</summary>
        public void MarkLoaded(string sceneName) => _loaded.Add(sceneName);

        /// <summary>模拟「编辑器里直接 Play 的场景」：已加载且是当前活动场景。</summary>
        public void SimulateEditorStartupScene(string sceneName)
        {
            _loaded.Add(sceneName);
            ActiveSceneName = sceneName;
        }

        /// <summary>注册「该场景的根物体」，供 <see cref="GetRootGameObjects"/> 返回（灯光/监听器相关断言用）。</summary>
        public void RegisterRoots(string sceneName, params GameObject[] roots)
            => _roots[sceneName] = new List<GameObject>(roots);

        public bool IsSceneLoaded(string sceneName) => _loaded.Contains(sceneName);

        public ISceneLoadOperation LoadAsync(string sceneName, SceneLoadMode mode)
        {
            LoadRequests.Add(sceneName);
            LoadModes.Add(mode);
            LastLoadMode = mode;
            Sequence.Add("load:" + sceneName);
            _loaded.Add(sceneName);

            return new FakeLoadOperation(() =>
            {
                // 引擎行为：Single 在**激活**时关闭其它所有场景
                if (mode == SceneLoadMode.Single)
                {
                    _loaded.Clear();
                    _loaded.Add(sceneName);
                }

                ActiveSceneName = sceneName;
                if (!SuppressSceneLoadedEvent)
                    SceneLoaded?.Invoke(new SceneLoadedEvent(sceneName, mode));
            });
        }

        public IEnumerator UnloadAsync(string sceneName)
        {
            UnloadRequests.Add(sceneName);
            Sequence.Add("unload:" + sceneName);
            LightEnabledAtUnload[sceneName] = CaptureLightStates(sceneName);
            _loaded.Remove(sceneName);
            SceneUnloaded?.Invoke(new SceneUnloadedEvent(sceneName));
            yield break;
        }

        private bool[] CaptureLightStates(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !_roots.TryGetValue(sceneName, out var roots))
                return Array.Empty<bool>();

            var states = new List<bool>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] == null) continue;
                var lights = roots[i].GetComponentsInChildren<Light>(true);
                for (int j = 0; j < lights.Length; j++) states.Add(lights[j].enabled);
            }
            return states.ToArray();
        }

        public bool SetActiveScene(string sceneName)
        {
            if (!_loaded.Contains(sceneName)) return false;
            ActiveSceneName = sceneName;
            SetActiveSceneCallback?.Invoke(sceneName);
            return true;
        }

        public GameObject[] GetRootGameObjects(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || !_roots.TryGetValue(sceneName, out var roots))
                return Array.Empty<GameObject>();
            return roots.ToArray();
        }

        public void Dispose() { }
    }

    private sealed class RecordingHooks : ISceneFlowHooks
    {
        public readonly List<string> Registered = new();
        public readonly List<string> Released = new();
        public readonly List<string> Preloaded = new();
        public readonly List<float> Progress = new();
        public readonly List<string> LoadingBegan = new();
        public readonly List<string> LoadingEnded = new();

        /// <summary>全部钩子调用的发生顺序（用于断言「先卸后载」这类跨钩子/跨服务的时序）。</summary>
        public readonly List<string> Sequence = new();

        /// <summary>容器刚建好时的回调（用于往场景容器里塞探针服务）。</summary>
        public Action<ServiceCollection, string> OnRegister;

        public IEnumerator RegisterSceneServices(ServiceCollection container, string sceneName)
        {
            Registered.Add(sceneName);
            Sequence.Add("register:" + sceneName);
            OnRegister?.Invoke(container, sceneName);
            yield return null;
        }

        public IEnumerator SceneResourcePreload(IServiceResolver resolver, string sceneName, Action<float> reportProgress)
        {
            Preloaded.Add(sceneName);
            Sequence.Add("preload:" + sceneName);
            reportProgress?.Invoke(1f);
            yield return null;
        }

        public void SceneResourceRelease(IServiceResolver resolver, string sceneName)
        {
            Released.Add(sceneName);
            Sequence.Add("release:" + sceneName);
        }

        public IEnumerator OnLoadingBegin(IServiceResolver resolver, string targetScene)
        {
            LoadingBegan.Add(targetScene);
            Sequence.Add("begin:" + targetScene);
            yield return null;
        }

        public void OnLoadingProgress(IServiceResolver resolver, string targetScene, float progress) => Progress.Add(progress);

        public IEnumerator OnLoadingEnd(IServiceResolver resolver, string targetScene)
        {
            LoadingEnded.Add(targetScene);
            Sequence.Add("end:" + targetScene);
            yield return null;
        }

        private static IEnumerator None() { yield break; }
    }

    private sealed class SpyDisposable : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    // ---------------- 场景流 ----------------

    [Test]
    public void SwitchScene_ToNewScene_BuildsContainerAndReleasesPreviousScene()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);

        // 每个场景容器里放一个可释放探针，用来观察容器是否被正确释放
        var spies = new Dictionary<string, SpyDisposable>();
        hooks.OnRegister = (container, name) =>
        {
            var spy = new SpyDisposable();
            spies[name] = spy;
            container.Register(spy);
            container.Resolve<SpyDisposable>();   // 解析一次，实例才进入容器的释放列表
        };

        flow.Attach();

        // 进入入口场景 A
        Drain(flow.InitializeEntryScene("A"));
        Assert.AreEqual("A", flow.CurrentSceneName);
        Assert.IsNotNull(flow.Current, "入口场景应建立场景容器");

        // A → B
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName, "切换后 CurrentSceneName 应指向新场景");
        CollectionAssert.AreEqual(new[] { "A", "B" }, hooks.Registered, "两个场景都应建立容器");
        CollectionAssert.Contains(hooks.Released, "A", "旧场景应触发资源释放钩子");
        CollectionAssert.Contains(scene.UnloadRequests, "A", "旧场景应被卸载");
        Assert.IsTrue(spies["A"].Disposed, "旧场景容器应被释放（其中 IDisposable 服务应被 Dispose）");
        Assert.IsFalse(spies["B"].Disposed, "当前场景的容器不应被释放");

        CollectionAssert.Contains(hooks.Preloaded, "B", "切换时应触发目标场景的预加载钩子");
        Assert.IsNotEmpty(hooks.Progress, "应上报切换进度（含 5%→30% 预加载区间）");
        Assert.AreEqual(1f, hooks.Progress[hooks.Progress.Count - 1], 0.0001f, "最后一次进度应为 1");

        flow.Dispose();
    }

    [Test]
    public void SwitchScene_ToAlreadyLoadedScene_ReusesInstanceInsteadOfLoadingAgain()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));
        scene.MarkLoaded("B");   // B 已在内存中（例如编辑器已打开该场景）

        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.AreEqual("B", scene.ActiveSceneName, "复用路径应把目标场景设为 ActiveScene");
        CollectionAssert.DoesNotContain(scene.LoadRequests, "B",
            "目标场景已加载时不应再 Additive 加载第二份（否则会出现重复相机/AudioListener）");
        CollectionAssert.Contains(scene.UnloadRequests, "A", "旧场景仍应被卸载");
        CollectionAssert.Contains(hooks.Registered, "B", "复用路径也应建立场景容器");
        CollectionAssert.DoesNotContain(hooks.Preloaded, "B", "复用已加载场景无需重复预加载");

        flow.Dispose();
    }

    [Test]
    public void SwitchScene_ToCurrentActiveScene_IsIgnored()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));

        flow.SwitchScene("A");

        Assert.AreEqual("A", flow.CurrentSceneName);
        Assert.AreEqual(1, scene.LoadRequests.Count, "切到当前场景不应产生额外加载");
        Assert.AreEqual(0, scene.UnloadRequests.Count, "切到当前场景不应产生卸载");
        CollectionAssert.AreEqual(new[] { "A" }, hooks.Registered, "不应重复建立容器");

        flow.Dispose();
    }

    // ---------------- 启动时旧场景解析（回归：从非入口场景 Play 的残留场景） ----------------

    /// <summary>
    /// 回归：从「非入口场景」Play 时（例如编辑器里打开的是 Level2，而入口是 SampleScene），
    /// 启动那次切换必须把编辑器初始场景当作旧场景卸掉，否则它会永久残留
    /// （连带自己的相机 / 灯光 / AudioListener）。
    /// 缺陷成因：<c>SwitchSceneRoutine</c> 直接取 <c>CurrentSceneName</c> 作旧场景，
    /// 而首次切换时它还是 null → 卸载判定被整体跳过。
    /// </summary>
    [Test]
    public void InitializeEntryScene_WhenEditorStartsOnAnotherScene_UnloadsThatInitialScene()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);
        flow.Attach();

        scene.SimulateEditorStartupScene("EditorScene");   // 编辑器里打开的初始场景 ≠ 入口场景

        Drain(flow.InitializeEntryScene("A"));

        Assert.AreEqual("A", flow.CurrentSceneName);
        CollectionAssert.Contains(scene.UnloadRequests, "EditorScene",
            "编辑器初始场景不是入口场景 → 必须被卸载，否则残留相机/灯光/AudioListener");
        CollectionAssert.Contains(hooks.Released, "EditorScene", "初始场景也应触发资源释放钩子");

        flow.Dispose();
    }

    /// <summary>对称情形：编辑器里打开的就是入口场景 → 复用，不加载也不卸载。</summary>
    [Test]
    public void InitializeEntryScene_WhenAlreadyTheActiveScene_DoesNotLoadOrUnload()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);
        flow.Attach();

        scene.SimulateEditorStartupScene("A");   // 编辑器里打开的就是入口场景

        Drain(flow.InitializeEntryScene("A"));

        Assert.AreEqual("A", flow.CurrentSceneName);
        Assert.AreEqual(0, scene.LoadRequests.Count, "入口场景已是当前场景，不应重复 Additive 加载");
        Assert.AreEqual(0, scene.UnloadRequests.Count, "也不应卸载任何场景");

        flow.Dispose();
    }

    // ---------------- 过渡期灯光一致性（Additive 共存窗口） ----------------

    /// <summary>
    /// 过渡期旧场景的灯光必须被关闭：新旧场景共存时两套主光会叠加，表现为「切换瞬间偏亮」。
    /// 断言点选在**卸载旧场景的那一刻**——那正是共存窗口的末尾，此时旧场景灯光应已关闭。
    /// </summary>
    [Test]
    public void SwitchScene_DisablesPreviousSceneLightsDuringTransition()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        // 「共存窗口」只存在于 Additive 系策略（Single 系下旧场景会被引擎原子关闭）
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.Additive };
        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        var previousLight = SpawnLight("PreviousScene_Light");
        var targetLight = SpawnLight("TargetScene_Light");
        scene.RegisterRoots("A", previousLight.gameObject);
        scene.RegisterRoots("B", targetLight.gameObject);

        Drain(flow.InitializeEntryScene("A"));
        flow.SwitchScene("B");

        Assert.IsTrue(scene.LightEnabledAtUnload.ContainsKey("A"), "切 B 时应卸载旧场景 A");
        CollectionAssert.AreEqual(new[] { false }, scene.LightEnabledAtUnload["A"],
            "卸载旧场景的瞬间，它的灯光必须已关闭（否则过渡窗口内两套主光叠加 → 画面瞬间偏亮）");
        Assert.IsTrue(targetLight.enabled, "目标场景的灯光绝不能被关闭");

        flow.Dispose();
    }

    /// <summary>
    /// 关闭是**临时**的：旧场景未被卸载时（此处为 Loading 场景：框架永远不卸载它）其灯光必须保持开启，
    /// 否则常驻叠加层会被改坏。
    /// </summary>
    [Test]
    public void SwitchScene_WhenPreviousSceneIsLoadingScene_LeavesItsLightsUntouched()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.Additive };
        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        var loadingLight = SpawnLight("LoadingScene_Light");
        scene.RegisterRoots("Loading", loadingLight.gameObject);
        scene.SimulateEditorStartupScene("Loading");   // 编辑器里打开的正是常驻 Loading 场景

        Drain(flow.InitializeEntryScene("A"));

        Assert.AreEqual(0, scene.UnloadRequests.Count, "Loading 场景永不被卸载");
        Assert.IsTrue(loadingLight.enabled, "常驻场景不参与过渡期灯光关闭，其灯光应保持开启");

        flow.Dispose();
    }

    // ---------------- 场景目录驱动的切换策略 ----------------

    /// <summary>
    /// 声明为常驻（<c>Persistent</c>）的场景切走后**不应**被卸载；
    /// 并且它的音频/灯光必须恢复 —— 过渡期会临时静音/关灯，若它不卸载就必须还原，否则会永久静音/变暗。
    /// </summary>
    [Test]
    public void SwitchScene_WhenPreviousSceneIsPersistent_KeepsItLoadedAndRestoresOverrides()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        // 常驻场景只能靠 Additive 系策略实现
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.Additive };
        catalog.Register(new SceneDefinition("A", persistent: true));
        catalog.Register(new SceneDefinition("B"));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        var lightA = SpawnLight("A_Light");
        var listenerA = SpawnListener("A_Listener");
        scene.RegisterRoots("A", lightA.gameObject, listenerA.gameObject);

        Drain(flow.InitializeEntryScene("A"));
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        CollectionAssert.DoesNotContain(scene.UnloadRequests, "A", "声明为常驻的场景不应被卸载");
        CollectionAssert.DoesNotContain(hooks.Released, "A", "常驻场景不应触发资源释放钩子");
        Assert.IsTrue(lightA.enabled, "常驻场景未被卸载 → 其灯光必须恢复");
        Assert.IsTrue(listenerA.enabled, "常驻场景未被卸载 → 其监听器必须恢复");

        flow.Dispose();
    }

    /// <summary>showLoading=false 的场景：不触发 Loading 显隐钩子，但预加载与进度上报不受影响。</summary>
    [Test]
    public void SwitchScene_WithShowLoadingFalse_SkipsLoadingHooks()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog();
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B", showLoading: false));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));

        int began = hooks.LoadingBegan.Count;
        int ended = hooks.LoadingEnded.Count;

        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.AreEqual(began, hooks.LoadingBegan.Count, "showLoading=false 时不应显示 Loading");
        Assert.AreEqual(ended, hooks.LoadingEnded.Count, "showLoading=false 时也不应收起 Loading");
        CollectionAssert.Contains(hooks.Preloaded, "B", "预加载不受 showLoading 影响");
        Assert.AreEqual(1f, hooks.Progress[hooks.Progress.Count - 1], 0.0001f);

        flow.Dispose();
    }

    /// <summary>
    /// 回归 B2：等待场景容器建立**必须有超时兜底**。
    /// 否则协程永久挂起，且此时音频已被静音、灯光已被关闭、Loading 已显示
    /// —— 运行状态会全部停在改动后的中间态。
    /// </summary>
    [Test]
    public void SwitchScene_WhenContainerNeverReady_TimesOutAndRestoresTransitionOverrides()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        // 音频/灯光补丁只存在于 Additive 系策略
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.Additive };
        catalog.Register(new SceneDefinition("A"));
        // 超时值取极小：同步 Drain 下能很快越过该阈值（同时保证在步数上限内结束）
        catalog.Register(new SceneDefinition("B", waitTimeoutSeconds: 0.000001f));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        var lightA = SpawnLight("A_Light");
        var listenerA = SpawnListener("A_Listener");
        scene.RegisterRoots("A", lightA.gameObject, listenerA.gameObject);

        Drain(flow.InitializeEntryScene("A"));
        Assert.AreEqual("A", flow.CurrentSceneName);

        scene.SuppressSceneLoadedEvent = true;   // 激活之后场景容器永远不会建立
        flow.SwitchScene("B");

        Assert.AreEqual("A", flow.CurrentSceneName, "超时中止后当前场景不应改变");
        Assert.IsTrue(lightA.enabled, "中止后旧场景灯光必须恢复（否则永久变暗）");
        Assert.IsTrue(listenerA.enabled, "中止后旧场景监听器必须恢复（否则永久静音）");
        CollectionAssert.DoesNotContain(scene.UnloadRequests, "A", "中止发生在卸载之前，旧场景不应被卸载");
        CollectionAssert.Contains(hooks.LoadingEnded, "B", "中止时应收起 Loading");

        // 中止必须复位切换状态，否则后续切换会被「已有切换正在进行」永久拒绝
        scene.SuppressSceneLoadedEvent = false;
        catalog.Register(new SceneDefinition("C"));
        flow.SwitchScene("C");
        Assert.AreEqual("C", flow.CurrentSceneName, "中止后应能再次正常切换（切换状态已复位）");

        flow.Dispose();
    }

    /// <summary>strict 模式下未登记的场景应被拒绝（尽早发现拼写错误）。</summary>
    [Test]
    public void SwitchScene_WhenSceneNotRegisteredAndStrict_IsRejected()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { Strict = true };
        catalog.Register(new SceneDefinition("A"));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));
        flow.SwitchScene("Typo_Scene");

        Assert.AreEqual("A", flow.CurrentSceneName, "strict 下未登记场景不应被切换");
        CollectionAssert.DoesNotContain(scene.LoadRequests, "Typo_Scene");
        CollectionAssert.DoesNotContain(hooks.LoadingBegan, "Typo_Scene");

        flow.Dispose();
    }

    /// <summary>非 strict（默认）下未登记场景回退为「宽松默认」，行为与引入场景目录之前一致。</summary>
    [Test]
    public void SwitchScene_WhenSceneNotRegisteredAndNotStrict_UsesDefaultPolicy()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog();
        catalog.Register(new SceneDefinition("A"));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName, "非 strict 下应回退为默认策略并完成切换");
        CollectionAssert.Contains(scene.LoadRequests, "B");
        CollectionAssert.Contains(scene.UnloadRequests, "A");
        CollectionAssert.Contains(hooks.LoadingBegan, "B", "默认策略包含显示 Loading");

        flow.Dispose();
    }

    /// <summary>
    /// 回归 B1：<c>SetActiveScene</c> 必须在**建立场景容器之前**调用。
    /// <para>
    /// 否则「激活 → 建容器」这个窗口内新建的**无父物体**会归属于旧场景，
    /// 随后被「卸载旧场景」一起静默销毁（场景模块里 Instantiate 出来的东西会莫名消失）。
    /// </para>
    /// </summary>
    [Test]
    public void SwitchScene_SetsActiveSceneBeforeBuildingSceneContainer()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var flow = CreateFlow(scene, hooks);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));

        // 记录「调用 SetActiveScene(B) 时，已建立多少个场景容器」
        int registeredWhenSetActive = -1;
        scene.SetActiveSceneCallback = _ => registeredWhenSetActive = hooks.Registered.Count;

        int beforeSwitch = hooks.Registered.Count;   // 此时只有 A 的容器
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.AreEqual(beforeSwitch, registeredWhenSetActive,
            "SetActiveScene 必须早于「建立 B 的场景容器」：晚于它会让该窗口内新建的无父物体落到旧场景并被卸载销毁");
        Assert.Greater(hooks.Registered.Count, beforeSwitch, "B 的容器最终仍应建立");

        flow.Dispose();
    }

    // ---------------- PR3：加载模式与「先卸后载」策略 ----------------

    /// <summary>SingleDirect 策略：必须把 Single 透传给场景服务（引擎会自己关闭旧场景，不需要我们手工卸）。</summary>
    [Test]
    public void SwitchScene_WithSingleDirectPolicy_RequestsSingleLoadAndSkipsManualUnload()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.SingleDirect };
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));

        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.AreEqual(SceneLoadMode.Single, scene.LastLoadMode, "Single 模式必须透传给 ISceneService");
        CollectionAssert.Contains(hooks.Released, "A", "旧场景的资源/面板释放钩子仍须触发");
        Assert.AreEqual(1, scene.LoadedSceneNames.Count, "Single 之后应只剩目标场景");
        Assert.AreEqual("B", scene.LoadedSceneNames[0]);
        // 注：仍会调用 UnloadAsync —— 由场景服务区分「真卸载」与「只释放句柄」，
        // 否则 Single 已被引擎关闭的场景会让 Addressables 句柄泄漏。
        CollectionAssert.Contains(scene.UnloadRequests, "A");

        flow.Dispose();
    }

    /// <summary>
    /// ThroughLoadingScene 策略：必须**经过 Loading 场景**：A → Loading（Single，关闭 A）→ B（Single，自动关闭 Loading）。
    /// 断言：① Loading 场景以 Single 加载；② load:Loading 早于 load:B；③ 结束后只剩目标场景；
    /// ④ Loading 场景的句柄被显式释放（否则 Addressables 泄漏）。
    /// </summary>
    [Test]
    public void SwitchScene_WithThroughLoadingScenePolicy_GoesThroughLoadingScene()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.ThroughLoadingScene };
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));

        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        // Loading 场景自带相机（Single 过渡期间它是唯一场景，需要它清屏）
        scene.RegisterRoots("Loading", SpawnCamera("Loading_Camera").gameObject);

        Drain(flow.InitializeEntryScene("A"));
        scene.Sequence.Clear();
        scene.LoadRequests.Clear();
        scene.LoadModes.Clear();

        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);

        // ① + ②：Loading 场景以 Single 加载，且早于目标场景
        int idxLoading = scene.Sequence.IndexOf("load:Loading");
        int idxB = scene.Sequence.IndexOf("load:B");
        Assert.GreaterOrEqual(idxLoading, 0, "Single 过渡必须加载 Loading 场景（A → Loading → B）");
        Assert.Less(idxLoading, idxB, "必须先加载 Loading 场景（关闭旧场景），再加载目标场景");
        Assert.AreEqual(SceneLoadMode.Single, scene.LoadModes[scene.LoadRequests.IndexOf("Loading")],
            "Loading 场景必须以 Single 加载，才能原子地关闭旧场景");

        // ③：Single 之后只剩目标场景
        Assert.AreEqual(1, scene.LoadedSceneNames.Count, "Single 过渡完成后应只剩目标场景");
        Assert.AreEqual("B", scene.LoadedSceneNames[0]);

        // ④：Loading 场景的句柄必须被显式释放（它已被 B 的 Single 加载关闭）
        //    注：用 LastIndexOf —— 前面「先释放已常驻加载的 Loading 场景」也会记一次 unload:Loading
        int idxUnloadLoading = scene.Sequence.LastIndexOf("unload:Loading");
        Assert.Greater(idxUnloadLoading, idxB, "目标场景加载后必须释放 Loading 场景的句柄（否则 Addressables 泄漏）");

        // 旧场景 A 的句柄同样要释放
        CollectionAssert.Contains(scene.UnloadRequests, "A");
        CollectionAssert.Contains(hooks.Released, "A");

        flow.Dispose();
    }

    /// <summary>
    /// 混用场景：过渡前若 Loading 场景已被加载（例如先跑过一次 Additive 路径），必须先释放它，
    /// 否则「Single 加载一个已加载场景」会覆盖 Addressables 句柄。
    /// </summary>
    [Test]
    public void SwitchScene_WithThroughLoadingSceneAndAlreadyLoadedLoadingScene_ReleasesItFirst()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.ThroughLoadingScene };
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));

        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        Drain(flow.InitializeEntryScene("A"));
        scene.MarkLoaded("Loading");   // 模拟 Loading 场景已被常驻加载

        scene.Sequence.Clear();
        flow.SwitchScene("B");

        int idxUnload = scene.Sequence.IndexOf("unload:Loading");
        int idxLoad = scene.Sequence.IndexOf("load:Loading");
        Assert.GreaterOrEqual(idxUnload, 0, "应先释放已加载的 Loading 场景");
        Assert.Less(idxUnload, idxLoad, "释放必须发生在重新以 Single 加载之前");

        flow.Dispose();
    }

    /// <summary>策略为 Single 系时，启动**不预热**常驻 Loading 场景（它会被第一次 Single 加载关闭）。</summary>
    [Test]
    public void InitializeLoadingScene_WhenPolicyIsSingleBased_SkipsPersistentPreload()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.SingleDirect };
        catalog.Register(new SceneDefinition("Level2"));

        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        Drain(flow.InitializeLoadingScene());

        Assert.AreEqual(0, scene.LoadRequests.Count, "Single 系策略下不应在启动时预热常驻 Loading 场景");
        flow.Dispose();
    }

    /// <summary>Additive 系策略保持原行为：启动时以 Additive 预热常驻 Loading 场景（回归）。</summary>
    [Test]
    public void InitializeLoadingScene_WhenPolicyIsAdditive_PreloadsPersistently()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.Additive };
        catalog.Register(new SceneDefinition("A"));

        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        Drain(flow.InitializeLoadingScene());

        CollectionAssert.Contains(scene.LoadRequests, "Loading");
        Assert.AreEqual(SceneLoadMode.Additive, scene.LoadModes[0], "Additive 项目仍以 Additive 常驻预热");

        flow.Dispose();
    }

    /// <summary>
    /// UnloadThenLoad 但 Loading 场景**没有相机**：必须 fail-safe 回退为「先载后卸」——
    /// 否则旧场景卸载后屏幕没有任何相机在渲染，画面未定义（花屏/残帧）。
    /// </summary>
    [Test]
    public void SwitchScene_WithAdditiveUnloadFirstButNoLoadingCamera_FallsBackToAdditive()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.AdditiveUnloadFirst };
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));

        // loadingSceneName 为 null → 必然没有可用的兜底相机
        var flow = CreateFlow(scene, hooks, catalog);
        flow.Attach();

        var lightA = SpawnLight("A_Light");
        scene.RegisterRoots("A", lightA.gameObject);

        Drain(flow.InitializeEntryScene("A"));
        scene.Sequence.Clear();
        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.Less(scene.Sequence.IndexOf("load:B"), scene.Sequence.IndexOf("unload:A"),
            "回退后应恢复「先加载后卸载」的顺序");
        CollectionAssert.AreEqual(new[] { false }, scene.LightEnabledAtUnload["A"],
            "回退到 LoadThenUnload → 过渡期仍会临时关闭旧场景灯光");

        flow.Dispose();
    }

    /// <summary>
    /// UnloadThenLoad + Loading 场景自带相机：必须「先卸旧场景」，且卸载要发生在**预加载之前**
    /// —— 否则预加载的新资源会与旧场景叠加，内存峰值不变，这个策略就白做了。
    /// </summary>
    [Test]
    public void SwitchScene_WithAdditiveUnloadFirstAndLoadingCamera_UnloadsBeforePreloadingAndLoading()
    {
        var scene = new FakeSceneService();
        var hooks = new RecordingHooks();
        var catalog = new SceneCatalog { TransitionPolicy = SceneTransitionPolicy.AdditiveUnloadFirst };
        catalog.Register(new SceneDefinition("A"));
        catalog.Register(new SceneDefinition("B"));

        var flow = new SceneFlow(new ServiceCollection(), scene, hooks, Drain, "Loading", catalog);
        flow.Attach();

        scene.RegisterRoots("Loading", SpawnCamera("Loading_Camera").gameObject);
        var lightA = SpawnLight("A_Light");
        scene.RegisterRoots("A", lightA.gameObject);

        Drain(flow.InitializeEntryScene("A"));
        scene.Sequence.Clear();
        hooks.Sequence.Clear();

        flow.SwitchScene("B");

        Assert.AreEqual("B", flow.CurrentSceneName);
        Assert.Less(scene.Sequence.IndexOf("unload:A"), scene.Sequence.IndexOf("load:B"),
            "先卸后载：旧场景卸载必须发生在新场景加载之前");
        Assert.Less(hooks.Sequence.IndexOf("release:A"), hooks.Sequence.IndexOf("preload:B"),
            "旧场景释放必须发生在目标资源预加载之前（否则内存峰值不变，策略失去意义）");
        CollectionAssert.AreEqual(new[] { true }, scene.LightEnabledAtUnload["A"],
            "先卸后载下不存在共存窗口 → 不应再关闭旧场景灯光（音频/灯光补丁被旁路）");

        flow.Dispose();
    }

    // ---------------- 场景退出 → 清理 PerScene 面板 ----------------

    private interface IScenePanel : IUIPanel { }

    private sealed class ScenePanel : IScenePanel, IUIPanelLifecycle
    {
        public void Create(IEventBus bus, ILogger logger) { }
        public void Show() { }
        public void Hide() { }
        public void Destroy() { }
    }

    private sealed class GlobalPanel : IUIPanel, IUIPanelLifecycle
    {
        public void Create(IEventBus bus, ILogger logger) { }
        public void Show() { }
        public void Hide() { }
        public void Destroy() { }
    }

    private sealed class NoopPanelFactory : IPanelFactory
    {
        public IUIPanel Create(PanelDefinition definition) => NewPanel(definition);

        public Task<IUIPanel> CreateAsync(PanelDefinition definition)
            => Task.FromResult(NewPanel(definition));

        public void Destroy(IUIPanel panel) { }

        private static IUIPanel NewPanel(PanelDefinition definition)
            => definition != null && definition.PanelType != null
                ? (IUIPanel)Activator.CreateInstance(definition.PanelType)
                : null;
    }

    [Test]
    public void SceneResourceRelease_DisposesPerScenePanels_ButKeepsGlobalPanels()
    {
        var container = new ServiceCollection();
        var ui = new UIService(new NoopPanelFactory(), null, null);
        container.Register<IUIService>(ui);

        ui.Register(new PanelDefinition(typeof(ScenePanel), "addr", PanelLayer.Normal, true, false, null));
        ui.Register(new PanelDefinition(typeof(GlobalPanel), "addr2", PanelLayer.Normal, true, true, null));

        ui.Open<ScenePanel>();
        ui.Open<GlobalPanel>();
        Assert.IsTrue(ui.IsCached<ScenePanel>());
        Assert.IsTrue(ui.IsCached<GlobalPanel>());

        // 场景退出：DefaultSceneFlowHooks 应清理 PerScene 面板
        new DefaultSceneFlowHooks().SceneResourceRelease(container, "A");

        Assert.IsFalse(ui.IsCached<ScenePanel>(), "PerScene 面板应在场景释放时被销毁");
        Assert.IsTrue(ui.IsCached<GlobalPanel>(), "全局面板不应被场景释放影响");
    }
}
