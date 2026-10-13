using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Logging;
using Framework.Adapters.Scenes;
using Framework.Core;
using UnityEngine;

namespace Framework.Bootstrap
{
    public sealed class SceneFlow : ISceneFlow
    {
        private readonly IServiceResolver _globalResolver;
        private readonly ISceneService _sceneService;
        private readonly ISceneFlowHooks _hooks;
        private readonly Func<IEnumerator, Coroutine> _run;
        private readonly ISceneCatalog _catalog;     // 场景目录；永不为 null（零配置时为空目录，查询走宽松默认）
        private readonly string _loadingSceneName;   // null 表示不用 Loading 场景

        private readonly List<SceneEntry> _scenes = new();
        private bool _isSwitching;
        private bool _disposed;
        private bool _attached;

        private string _waitingScene;
        private bool _containerReady;

        private readonly List<GameObject> _loadingSceneRoots = new();
        private readonly List<Light> _disabledLights = new();           // 过渡期临时关闭的旧场景灯光（供恢复）
        private readonly List<AudioListener> _mutedListeners = new();   // 过渡期临时静音的旧场景监听器（供恢复）
        private bool _loadingSceneVisible;
        private bool _startupCompleted;
        private SceneTransitionPolicy? _effectivePolicy;   // 生效策略（含「常驻场景 → 自动升级 Additive」安全阀），缓存

        public string CurrentSceneName { get; private set; }
        public IServiceResolver Current { get; private set; }
        public event Action<IServiceResolver> ResolverChanged;

        private sealed class SceneEntry
        {
            public string SceneName;
            public ServiceCollection Container;
        }

        public SceneFlow(
            IServiceResolver globalResolver,
            ISceneService sceneService,
            ISceneFlowHooks hooks,
            Func<IEnumerator, Coroutine> coroutineRunner,
            string loadingSceneName = null,
            ISceneCatalog catalog = null)
        {
            _globalResolver = globalResolver;
            _sceneService = sceneService;
            _hooks = hooks ?? new EmptySceneFlowHooks();
            _run = coroutineRunner;

            // 场景目录：未注入时用空目录（查询会回退到「宽松默认」，行为与引入目录之前一致）
            _catalog = catalog ?? new SceneCatalog();

            // Loading 场景名：场景目录优先，其次构造参数（兼容零配置项目）
            _loadingSceneName = string.IsNullOrEmpty(_catalog.LoadingSceneName)
                ? loadingSceneName
                : _catalog.LoadingSceneName;

            Current = globalResolver;
        }

        public void Attach()
        {
            if (_attached) return;
            _attached = true;
            _sceneService.SceneLoaded   += HandleSceneLoaded;
            _sceneService.SceneUnloaded += HandleSceneUnloaded;
            SetCurrent(_globalResolver);
        }

        /// <summary>查询场景定义（来自场景目录）；未登记返回 null。</summary>
        public SceneDefinition GetDefinition(string sceneName)
            => _catalog.TryGet(sceneName, out var definition) ? definition : null;

        // ---------- Loading 场景：启动时 Additive 加载一次 ----------

        public IEnumerator InitializeLoadingScene()
        {
            if (string.IsNullOrEmpty(_loadingSceneName)) yield break;

            // Single 系策略下，常驻的 Loading 场景会被第一次 Single 加载直接关闭，预热它没有意义
            //（还会与后续「Single 加载一个已加载场景」产生句柄冲突）→ 改为按需加载。
            if (PolicyIsSingleBased())
            {
                Log.Info($"[SceneFlow] 切换策略为 {EffectiveTransitionPolicy()} → Loading 场景「{_loadingSceneName}」改为按需加载（不常驻）");
                yield break;
            }

            // 若 Loading 已是当前活动场景（例如直接从 LoadingScene 运行），无需重复加载
            if (_sceneService.ActiveSceneName == _loadingSceneName)
            {
                Log.Info("Loading 场景已处于活动状态，跳过重复加载");
                HideLoadingSceneAfterLoad();
                yield break;
            }

            Log.Info($"加载 Loading 场景：{_loadingSceneName}");
            var op = TryLoadScene(_loadingSceneName, SceneLoadMode.Additive, out var error);
            if (op == null)
            {
                Log.Error($"Loading 场景加载失败：{error}（已跳过，继续启动）");
                yield break;
            }
            while (!op.IsPrepared) yield return null;
            op.Activate();
            while (!op.IsDone) yield return null;

            // 常驻但默认隐藏：否则 Loading 场景的全屏界面会一直盖住入口场景
            HideLoadingSceneAfterLoad();
            Log.Info("Loading 场景就绪（常驻，默认隐藏）");
        }

        // ---------- 入口场景 ----------

        public IEnumerator InitializeEntryScene(string entryScene)
        {
            if (string.IsNullOrEmpty(entryScene)) yield break;

            // 入口场景若已是当前活动场景（例如直接从入口场景运行），则复用它，
            // 避免 Additive 重复加载同一场景。
            if (CurrentSceneName == entryScene || _sceneService.ActiveSceneName == entryScene)
            {
                CurrentSceneName = entryScene;
                yield return CreateSceneContainerRoutine(entryScene);
            }
            else
            {
                yield return SwitchSceneRoutine(entryScene);
            }

            // 启动流程到此结束：之前的外部切换请求都会被拒绝，避免与启动切换并发执行
            _startupCompleted = true;
        }

        public void SwitchScene(string sceneName)
        {
            if (_disposed) return;

            if (!_startupCompleted)
            {
                Log.Warn($"启动尚未完成，已忽略切换请求：{sceneName}（请在启动完成后调用，或直接用 InitializeEntryScene）");
                return;
            }

            if (_isSwitching)
            {
                Log.Warn($"已有切换正在进行，已忽略重复请求：{sceneName}");
                return;
            }

            _run(SwitchSceneRoutine(sceneName));
        }

        // ---------- 核心：场景切换（策略见 SceneTransitionPolicy）----------

        private IEnumerator SwitchSceneRoutine(string sceneName)
        {
            _isSwitching = true;

            var target = ResolveDefinition(sceneName);
            if (target == null)   // 未登记且 strict：ResolveDefinition 已报错
            {
                _isSwitching = false;
                yield break;
            }

            // 目标场景若已在内存中，绝不能再 Additive 加载第二份：
            // 否则会出现重复的相机/AudioListener/UI 等对象（曾表现为「2 audio listeners」警告）。
            if (_sceneService.IsSceneLoaded(sceneName))
            {
                yield return ReuseLoadedSceneRoutine(sceneName);
                _isSwitching = false;
                Log.Info($"切换完成（复用已加载场景）：{sceneName}");
                yield break;
            }

            var prevScene = ResolvePreviousSceneName();
            Log.Info($"开始切换：{prevScene ?? "<none>"} → {sceneName}");

            var policy = EffectiveTransitionPolicy();
            bool showLoading = target.ShowLoading;
            bool hasLoadingScene = !string.IsNullOrEmpty(_loadingSceneName);

            // 策略矩阵（见 SceneTransitionPolicy）：
            //   Single 系：目标以 Single 加载 → 引擎在激活时原子关闭旧场景（无共存窗口）
            //   Additive 系：目标以 Additive 加载 → 与旧场景共存（需要音频/灯光补丁）
            bool singleBased = policy == SceneTransitionPolicy.SingleDirect
                            || policy == SceneTransitionPolicy.ThroughLoadingScene;
            bool throughLoadingScene = policy == SceneTransitionPolicy.ThroughLoadingScene;
            var targetMode = singleBased ? SceneLoadMode.Single : SceneLoadMode.Additive;

            // AdditiveUnloadFirst：先卸旧场景、再加载目标（内存峰值最低）。
            // ⚠ 前置条件：旧场景卸载后必须有相机在渲染（由 Loading 场景提供）—— 缺失时 fail-safe 回退。
            bool unloadOldFirst = policy == SceneTransitionPolicy.AdditiveUnloadFirst;
            if (unloadOldFirst && !LoadingSceneProvidesCamera())
            {
                Log.Error("[SceneFlow] 策略 AdditiveUnloadFirst 需要 Loading 场景自带相机" +
                          "（否则旧场景卸载后没有相机渲染）→ 本次回退为 Additive");
                unloadOldFirst = false;
            }

            // 1. 显示 Loading
            //    - Single 系：Loading 场景由 1.6 以 Single **按需加载**（或不经过中间场景）；
            //      进度恒由位于 DontDestroyOnLoad 的 UI 面板承担（它不受 Single 影响，切换全程存活）。
            //    - Additive 系：显示常驻/按需的 Loading 场景（黑底背景板）。
            if (showLoading)
            {
                ForgetLoadingSceneRootsIfDestroyed();
                if (!singleBased) yield return ShowLoadingSceneRoutine();
                yield return _hooks.OnLoadingBegin(Current, sceneName);
            }

            // 1.5 「先卸后载」：在预加载与加载**之前**就把旧场景卸掉，这才是内存峰值最低的时机。
            //     ⚠ 必须在预加载之前 —— 否则预加载的新资源会与旧场景叠在一起，峰值不变，白做。
            //     卸完之后 prevScene 置空：后续的静音/关灯/卸载都不再需要（不存在共存窗口）。
            bool releasedOldFirst = false;
            if (unloadOldFirst && ShouldRelease(prevScene, sceneName))
            {
                Log.Info($"[SceneFlow] 先卸载旧场景（AdditiveUnloadFirst）：{prevScene}");
                yield return _sceneService.UnloadAsync(prevScene);
                ReleaseScene(prevScene, sceneName);
                prevScene = null;
                releasedOldFirst = true;
            }

            // 1.6 ThroughLoadingScene：把 Loading 场景当作**过渡场景**按需 Single 加载 —— A → Loading → B。
            //     ① 这一步会原子地关闭旧场景（A 不再残留）；
            //     ② 之后以 Single 加载目标场景时会再关闭 Loading 场景（不需要手工卸载）。
            //     未配置 Loading 场景时跳过（旧场景由目标场景的 Single 加载关闭）。
            bool usedSingleLoadingScene = false;
            if (throughLoadingScene && showLoading && hasLoadingScene)
            {
                // 混用（先跑过 Additive 路径）的项目里 Loading 场景可能已被加载 → 先释放，避免 Single 覆盖句柄
                if (_sceneService.IsSceneLoaded(_loadingSceneName))
                {
                    Log.Info($"[SceneFlow] 过渡前先释放已加载的 Loading 场景：{_loadingSceneName}");
                    yield return _sceneService.UnloadAsync(_loadingSceneName);
                }

                Log.Info($"[SceneFlow] 过渡：加载 Loading 场景（关闭旧场景 {prevScene ?? "<none>"}）：{_loadingSceneName}");
                var loadingOp = TryLoadScene(_loadingSceneName, SceneLoadMode.Single, out var loadingError);
                if (loadingOp == null)
                {
                    Log.Error($"[SceneFlow] Loading 场景加载失败：{loadingError}（本次直接以 Single 加载目标场景）");
                }
                else
                {
                    while (!loadingOp.IsPrepared) yield return null;
                    loadingOp.Activate();
                    while (!loadingOp.IsDone) yield return null;
                    usedSingleLoadingScene = true;
                    _hooks.OnLoadingProgress(Current, sceneName, 0.05f);
                    WarnIfLoadingSceneHasNoCamera();
                }
            }

            // 2. 预加载目标资源（5% → 30% 区间；由钩子按需预加载并通过回调上报局部进度）
            yield return _hooks.SceneResourcePreload(Current, sceneName,
                p => _hooks.OnLoadingProgress(Current, sceneName, Mathf.Lerp(0.05f, 0.3f, Mathf.Clamp01(p))));
            _hooks.OnLoadingProgress(Current, sceneName, 0.3f);

            // 3. 标记等待中的场景（须在加载前设置，供 sceneLoaded 回调匹配）
            _waitingScene = sceneName;
            _containerReady = false;

            // 4. 加载目标场景（Single / Additive 由**全局策略**决定，见 SceneTransitionPolicy）
            var op = TryLoadScene(sceneName, targetMode, out var error);
            if (op == null)
            {
                Log.Error($"加载失败：{sceneName}，{error}");

                // 「先卸后载」的代价：旧场景已经不在，失败后没有可回退的场景（只能停在 Loading 画面）
                if (releasedOldFirst)
                    Log.Error("[SceneFlow] 旧场景已在「先卸后载」阶段被卸载，本次加载失败后没有可回退的场景" +
                              "（LoadThenUnload 才有退路）");

                _waitingScene = null;

                // 失败收尾：把「已被关闭/卸载」的旧场景与 Loading 场景的句柄释放掉，避免 Addressables 泄漏。
                if (usedSingleLoadingScene)
                {
                    if (ShouldRelease(prevScene, sceneName))
                    {
                        yield return _sceneService.UnloadAsync(prevScene);
                        ReleaseScene(prevScene, sceneName);
                    }
                    yield return _sceneService.UnloadAsync(_loadingSceneName);
                }

                if (showLoading)
                {
                    yield return _hooks.OnLoadingEnd(Current, sceneName);
                    HideLoadingScene();
                }
                _isSwitching = false;
                yield break;
            }

            // 5. 等待加载就绪（progress 到 0.9），同时上报进度 30% → 90%
            while (!op.IsPrepared)
            {
                var p = Mathf.Lerp(0.3f, 0.9f, Mathf.Clamp01(op.Progress / 0.9f));
                _hooks.OnLoadingProgress(Current, sceneName, p);
                yield return null;   // 进度上报是同步的，这里必须显式等一帧（否则同帧空转）
            }

            // 5.1 激活目标场景（内容实例化）
            //     仅 Additive 系需要：新旧共存期间先静音旧场景的监听器，
            //     避免 Unity 每次切场景都刷「There are 2 audio listeners」警告（Single 系无共存窗口）。
            if (!singleBased) MuteAudioListeners(prevScene);
            op.Activate();
            while (!op.IsDone) yield return null;

            // 5.2 ActiveScene 的设置统一放在 CreateSceneContainerRoutine 里（它由 sceneLoaded 事件
            //     在 Activate() 内部触发，早于本协程后续步骤）—— 保证「建容器之前 ActiveScene 已是新场景」。
            //     这里不再重复调用，避免出现两个设置点导致顺序语义不清。

            // 5.3 关掉旧场景的灯光：仅 Additive 系（共存窗口内两套主光叠加会让画面「瞬间偏亮」）。
            //     必须放在激活**之后** —— 若在 Activate() 之前关，画面里仍是旧场景，会看到「暗闪」。
            if (!singleBased) DisableLightsOf(prevScene);

            // 6. 等场景容器建立（带超时兜底：否则会永久挂起，且音频/灯光/遮罩都停在改动后的状态）
            float deadline = target.WaitTimeoutSeconds > 0f
                ? Time.realtimeSinceStartup + target.WaitTimeoutSeconds
                : 0f;

            while (!_containerReady)
            {
                if (deadline > 0f && Time.realtimeSinceStartup >= deadline)
                {
                    Log.Error($"[SceneFlow] 等待场景容器超时（{target.WaitTimeoutSeconds:0.######}s）：{sceneName}，已中止本次切换");
                    _waitingScene = null;
                    yield return AbortSwitch(prevScene, sceneName, showLoading, usedSingleLoadingScene);
                    yield break;
                }

                _hooks.OnLoadingProgress(Current, sceneName, 0.9f);
                yield return null;
            }
            _waitingScene = null;

            // 7. 释放旧场景（Loading 场景与声明为常驻的场景都保留）
            if (ShouldRelease(prevScene, sceneName))
            {
                // Single 模式下旧场景已被引擎关闭，但**仍需调用** UnloadAsync：
                // 由场景服务负责区分「真卸载」与「只释放句柄」（否则 Addressables 会泄漏）。
                yield return _sceneService.UnloadAsync(prevScene);
                ReleaseScene(prevScene, sceneName);
            }

            // 7.1 Single 过渡用的 Loading 场景已被目标场景的 Single 加载自动关闭 ——
            //     必须显式 UnloadAsync 释放其句柄（否则 Addressables 引用计数不降 → 泄漏）。
            if (usedSingleLoadingScene)
            {
                yield return _sceneService.UnloadAsync(_loadingSceneName);
            }

            // 兜底：旧场景未被卸载（或卸载失败）时恢复其音频/灯光。已卸载时引用已销毁，恢复是空操作。
            RestoreTransitionOverrides();

            // 8. 隐藏 Loading
            _hooks.OnLoadingProgress(Current, sceneName, 1f);
            if (showLoading)
            {
                yield return _hooks.OnLoadingEnd(Current, sceneName);
                HideLoadingScene();
            }

            CurrentSceneName = sceneName;
            _isSwitching = false;
            Log.Info($"切换完成：{sceneName}（已加载场景：{string.Join("、", _sceneService.LoadedSceneNames)}）");
        }

        /// <summary>
        /// 目标场景已在内存中：直接复用（必要时激活 + 建容器 + 卸旧场景），不再重复加载。
        /// 切换是瞬时的，因此不显示 Loading。
        /// </summary>
        private IEnumerator ReuseLoadedSceneRoutine(string sceneName)
        {
            var prevScene = ResolvePreviousSceneName();
            bool alreadyActive = _sceneService.ActiveSceneName == sceneName;

            if (alreadyActive)
            {
                Log.Warn($"目标场景已是当前活动场景，忽略切换：{sceneName}");
            }
            else
            {
                Log.Warn($"目标场景已加载（非活动），复用现有实例而非重复加载：{sceneName}");
                if (!PolicyIsSingleBased())
                {
                    MuteAudioListeners(prevScene);   // 旧场景即将卸载，先静音以免双 AudioListener 警告
                }
                _sceneService.SetActiveScene(sceneName);
                if (!PolicyIsSingleBased())
                {
                    DisableLightsOf(prevScene);      // 旧场景灯光随即关闭，避免与新场景叠加
                }
            }

            yield return CreateSceneContainerRoutine(sceneName);

            // 旧场景仍然释放（自身 / Loading 场景 / 声明为常驻的场景除外）
            if (!alreadyActive && ShouldRelease(prevScene, sceneName))
            {
                yield return _sceneService.UnloadAsync(prevScene);
                ReleaseScene(prevScene, sceneName);
            }

            RestoreTransitionOverrides();   // 兜底：旧场景未被卸载时恢复其音频/灯光
            CurrentSceneName = sceneName;
        }

        private ISceneLoadOperation TryLoadScene(string sceneName, SceneLoadMode mode, out string error)
        {
            error = null;
            try
            {
                return _sceneService.LoadAsync(sceneName, mode);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>
        /// 解析「旧场景名」。
        /// <para>
        /// 首次切换时 <see cref="CurrentSceneName"/> 仍为空（启动流程跑 <see cref="InitializeEntryScene"/> 时
        /// 还没有任何「框架已知的当前场景」），此时旧场景应为 Unity 当前的活动场景
        /// —— 典型情况是「在编辑器里直接 Play 的那个场景」。若直接取 <see cref="CurrentSceneName"/>（null），
        /// 卸载判定会被整体跳过，导致该场景永久残留在内存中（残留相机/灯光/AudioListener）。
        /// </para>
        /// </summary>
        private string ResolvePreviousSceneName()
            => string.IsNullOrEmpty(CurrentSceneName) ? _sceneService.ActiveSceneName : CurrentSceneName;

        // ---------- Loading 场景显隐（常驻，但仅加载期间可见）----------

        /// <summary>加载完成后：记录根物体并默认隐藏，避免全屏 Loading 界面常驻遮挡入口场景。</summary>
        private void HideLoadingSceneAfterLoad()
        {
            CaptureLoadingSceneRoots();
            if (_loadingSceneRoots.Count == 0) return;

            _loadingSceneVisible = true;   // 刚加载完成/已是活动场景，当前确实可见
            HideLoadingScene();
        }

        private void CaptureLoadingSceneRoots()
        {
            _loadingSceneRoots.Clear();

            var roots = _sceneService.GetRootGameObjects(_loadingSceneName);
            if (roots != null && roots.Length > 0)
            {
                _loadingSceneRoots.AddRange(roots);
                return;
            }

            Log.Warn($"Loading 场景根物体获取失败：{_loadingSceneName}");
        }

        /// <summary>
        /// 显示 Loading 场景（Additive 路径）。
        /// <para>
        /// 若它尚未加载（例如项目以 Single 为主，启动时**没有**预热），这里按需 Additive 加载一次
        /// —— 兼容「混合使用 Additive / Single」的项目；之后它会像常驻场景一样被复用。
        /// </para>
        /// </summary>
        private IEnumerator ShowLoadingSceneRoutine()
        {
            if (string.IsNullOrEmpty(_loadingSceneName)) yield break;

            if (!_sceneService.IsSceneLoaded(_loadingSceneName))
            {
                Log.Info($"[SceneFlow] 按需加载 Loading 场景：{_loadingSceneName}");
                var op = TryLoadScene(_loadingSceneName, SceneLoadMode.Additive, out var error);
                if (op == null)
                {
                    Log.Error($"[SceneFlow] Loading 场景加载失败：{error}（本次过渡无场景背景板）");
                    yield break;
                }

                while (!op.IsPrepared) yield return null;
                op.Activate();
                while (!op.IsDone) yield return null;
                CaptureLoadingSceneRoots();
            }
            else if (_loadingSceneRoots.Count == 0)
            {
                CaptureLoadingSceneRoots();
            }

            ShowLoadingScene();
        }

        private void ShowLoadingScene()
        {
            if (_loadingSceneVisible || _loadingSceneRoots.Count == 0) return;
            SetLoadingSceneRootsActive(true);
            _loadingSceneVisible = true;
            Log.Info("显示 Loading 场景");
        }

        private void HideLoadingScene()
        {
            if (!_loadingSceneVisible || _loadingSceneRoots.Count == 0) return;
            SetLoadingSceneRootsActive(false);
            _loadingSceneVisible = false;
            Log.Info("隐藏 Loading 场景");
        }

        private void SetLoadingSceneRootsActive(bool active)
        {
            for (int i = 0; i < _loadingSceneRoots.Count; i++)
            {
                var root = _loadingSceneRoots[i];
                if (root != null) root.SetActive(active);
            }
        }

        /// <summary>
        /// 静音指定场景里所有 <see cref="AudioListener"/>。
        /// 用于 Additive 切换的过渡期：新旧场景共存时两边各有一个监听器，Unity 会报警告。
        /// 只处理**即将被卸载**的场景（Loading 场景与自身除外）；被静音的监听器会记入
        /// <c>_mutedListeners</c>，由 <see cref="RestoreTransitionOverrides"/> 在未卸载/中止时恢复。
        /// </summary>
        private void MuteAudioListeners(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || sceneName == _loadingSceneName) return;

            var roots = _sceneService.GetRootGameObjects(sceneName);
            if (roots == null) return;

            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null) continue;

                var listeners = root.GetComponentsInChildren<AudioListener>(true);
                for (int j = 0; j < listeners.Length; j++)
                {
                    var listener = listeners[j];
                    if (listener == null || !listener.enabled) continue;

                    listener.enabled = false;
                    _mutedListeners.Add(listener);
                }
            }
        }

        /// <summary>
        /// 恢复过渡期对旧场景做的**全部**临时改动（静音监听器 + 关闭灯光）并清空记录。
        /// <para>旧场景已卸载时引用已销毁 —— 靠 Unity 的 <c>== null</c> 重载跳过（切勿先访问成员）。</para>
        /// <para>调用点：卸载旧场景之后（兜底）与中止切换时（必需，否则会留下被静音/被关灯的中间态）。</para>
        /// </summary>
        private void RestoreTransitionOverrides()
        {
            RestoreDisabledLights();
            RestoreMutedListeners();
        }

        private void RestoreMutedListeners()
        {
            if (_mutedListeners.Count == 0) return;

            for (int i = 0; i < _mutedListeners.Count; i++)
            {
                var listener = _mutedListeners[i];
                if (listener != null) listener.enabled = true;
            }

            _mutedListeners.Clear();
        }

        // ---------- 策略 ----------

        /// <summary>
        /// 解析场景定义。未登记时：<c>Strict</c> 记录错误并返回 null（调用方中止切换）；
        /// 否则记警告并回退到「宽松默认」（Additive / 不常驻 / 显示 Loading）
        /// —— 与引入场景目录之前的行为完全一致。
        /// </summary>
        private SceneDefinition ResolveDefinition(string sceneName)
        {
            if (_catalog.TryGet(sceneName, out var definition) && definition != null) return definition;

            if (_catalog.Strict)
            {
                Log.Error($"[Scenes] 场景未登记：{sceneName}（请在 SceneCatalog 配置中登记，或关闭 strict）");
                return null;
            }

            Log.Warn($"[Scenes] 场景未登记，使用默认策略：{sceneName}（Additive / 不常驻 / 显示 Loading）");
            return new SceneDefinition(sceneName);
        }

        /// <summary>是否应「放手」旧场景：自身 / Loading 场景 / 声明为常驻的场景都保留。</summary>
        private bool ShouldRelease(string prevScene, string targetScene)
        {
            if (string.IsNullOrEmpty(prevScene)) return false;
            if (prevScene == _loadingSceneName) return false;
            if (prevScene == targetScene) return false;

            // 常驻判定看的是**旧场景**的定义（新场景声不声明常驻与此无关）
            return !(_catalog.TryGet(prevScene, out var definition) && definition.Persistent);
        }

        /// <summary>
        /// 触发旧场景的资源/面板释放钩子（卸载与释放是两件事：Single 下场景已被引擎关闭，仍须释放）。
        /// <para>
        /// <paramref name="enteringScene"/> 必须一并传下去：释放的判据是「**不属于新场景**」
        /// （例如为进入场景预加载的面板要保留），只看离开的场景会误删刚预加载的东西。
        /// </para>
        /// </summary>
        private void ReleaseScene(string leavingScene, string enteringScene)
        {
            try { _hooks.SceneResourceRelease(_globalResolver, leavingScene, enteringScene); }
            catch (Exception e) { Debug.LogError($"[SceneFlow] 释放 {leavingScene} 失败: {e}"); }
        }

        /// <summary>
        /// 「先卸后载」的前置条件：旧场景卸载后必须有相机在渲染，否则画面未定义。
        /// 这里只认**Loading 场景** —— 它是该模式下约定的「兜底画面」提供者（详见设计稿 §六）。
        /// </summary>
        private bool LoadingSceneProvidesCamera()
        {
            if (string.IsNullOrEmpty(_loadingSceneName)) return false;

            var roots = _sceneService.GetRootGameObjects(_loadingSceneName);
            if (roots == null) return false;

            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null) continue;

                var cameras = root.GetComponentsInChildren<Camera>(true);
                for (int j = 0; j < cameras.Length; j++)
                {
                    if (cameras[j] != null) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 生效的切换策略（缓存）。
        /// <para>
        /// **安全阀**：目录里存在「常驻场景」但策略是 Single 系时，自动升级为 <see cref="SceneTransitionPolicy.Additive"/> ——
        /// Single 加载会摧毁其它所有场景，常驻场景会直接消失。告警说明后改用 Additive，而不是静默地做错事。
        /// </para>
        /// </summary>
        private SceneTransitionPolicy EffectiveTransitionPolicy()
        {
            if (_effectivePolicy.HasValue) return _effectivePolicy.Value;

            var policy = _catalog.TransitionPolicy;
            if ((policy == SceneTransitionPolicy.SingleDirect ||
                 policy == SceneTransitionPolicy.ThroughLoadingScene) && HasPersistentScenes())
            {
                Log.Warn($"[SceneFlow] 策略 {policy} 不支持常驻场景（Single 会摧毁其它所有场景）→ 已自动改用 Additive" +
                         "（要显式用 Single，请去掉场景的 persistent 标记）");
                policy = SceneTransitionPolicy.Additive;
            }

            _effectivePolicy = policy;
            return policy;
        }

        private bool HasPersistentScenes()
        {
            var all = _catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Persistent) return true;
            }
            return false;
        }

        /// <summary>当前生效策略是否属于 Single 系（无共存窗口）。</summary>
        private bool PolicyIsSingleBased()
        {
            var policy = EffectiveTransitionPolicy();
            return policy == SceneTransitionPolicy.SingleDirect
                || policy == SceneTransitionPolicy.ThroughLoadingScene;
        }

        /// <summary>Single 过渡期间 Loading 场景是唯一场景 —— 缺相机时屏幕不会被清空（给出警告）。</summary>
        private void WarnIfLoadingSceneHasNoCamera()
        {
            if (LoadingSceneProvidesCamera()) return;

            Log.Warn($"[SceneFlow] Loading 场景「{_loadingSceneName}」没有相机：Single 过渡期间屏幕不会被相机清空" +
                     "（建议给 Loading 场景加一个 Solid Color = 黑的相机）");
        }

        /// <summary>
        /// Single 加载会关闭其它所有场景 —— 包括常驻的 Loading 场景。
        /// 此处检测缓存的根物体是否已全部销毁，是则清理缓存（后续过渡只靠 UI Loading 面板）。
        /// </summary>
        private void ForgetLoadingSceneRootsIfDestroyed()
        {
            if (_loadingSceneRoots.Count == 0) return;

            for (int i = 0; i < _loadingSceneRoots.Count; i++)
            {
                if (_loadingSceneRoots[i] != null) return;   // 还有活着的根物体
            }

            _loadingSceneRoots.Clear();
            _loadingSceneVisible = false;
            Log.Info("[SceneFlow] Loading 场景已被关闭（Single 加载会关闭其它所有场景）→ 清理失效缓存（需要时再按需加载）");
        }

        /// <summary>
        /// 中止一次切换：恢复过渡期的临时改动（音频/灯光）并收起 Loading，
        /// 保证「超时/挂起」不会把运行状态留在改动后的中间态。
        /// </summary>
        private IEnumerator AbortSwitch(string prevScene, string targetScene, bool showLoading, bool usedSingleLoadingScene = false)
        {
            RestoreTransitionOverrides();

            // Single 过渡用的 Loading 场景已被目标场景的 Single 加载关闭 → 释放其句柄（否则泄漏）
            if (usedSingleLoadingScene)
                yield return _sceneService.UnloadAsync(_loadingSceneName);

            if (showLoading)
            {
                yield return _hooks.OnLoadingEnd(Current, targetScene);
                HideLoadingScene();
            }

            _isSwitching = false;
            Log.Warn($"[SceneFlow] 已中止切换：{prevScene ?? "<none>"} → {targetScene}（保持当前场景 {CurrentSceneName ?? "<none>"}）");
        }

        // ---------- 过渡期一致性：旧场景的灯光不要与新场景叠加 ----------

        /// <summary>
        /// 临时关闭指定场景的所有 <see cref="Light"/>。
        /// <para>
        /// Additive 切换期间新旧场景会**共存**：目标场景激活后，它的灯光会与旧场景的灯光同时照亮画面，
        /// 表现为「切换瞬间偏亮」（旧场景随后才被卸载）。这里把旧场景的灯光关掉，让过渡期只受目标场景光照控制。
        /// </para>
        /// <para>
        /// ⚠️ 调用时机：必须在 <c>Activate()</c> **完成之后**。若在激活之前关闭，画面里还是旧场景，会出现「暗闪」。
        /// （音频不存在这个问题，所以 <see cref="MuteAudioListeners"/> 可以放在激活之前 —— 两者调用点不同是有意为之。）
        /// </para>
        /// <para>被关掉的灯光记入 <c>_disabledLights</c>，供 <see cref="RestoreDisabledLights"/> 兜底恢复（见其说明）。</para>
        /// </summary>
        private void DisableLightsOf(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || sceneName == _loadingSceneName) return;

            var roots = _sceneService.GetRootGameObjects(sceneName);
            if (roots == null) return;

            int disabled = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null) continue;

                var lights = root.GetComponentsInChildren<Light>(true);
                for (int j = 0; j < lights.Length; j++)
                {
                    var light = lights[j];
                    if (light == null || !light.enabled) continue;

                    light.enabled = false;
                    _disabledLights.Add(light);
                    disabled++;
                }
            }

            if (disabled > 0) Log.Info($"关闭旧场景灯光：{sceneName}（{disabled} 盏，避免与新场景叠加）");
        }

        /// <summary>
        /// 恢复被 <see cref="DisableLightsOf"/> 临时关闭的灯光。
        /// <para>
        /// 旧场景已卸载时其灯光已销毁 —— 这里靠 Unity 的 <c>== null</c> 重载跳过
        /// （切勿先访问成员，对已销毁对象会抛 <c>MissingReferenceException</c>）。
        /// </para>
        /// <para>
        /// 说明：当前流程里「已禁用灯光」必然伴随「旧场景被卸载」（两条路径都如此），
        /// 因此这是个**兵底**而非必经路径 —— 它保证将来若新增「跳过卸载」的分支，不会把场景永久留在变暗状态。
        /// </para>
        /// </summary>
        private void RestoreDisabledLights()
        {
            if (_disabledLights.Count == 0) return;

            for (int i = 0; i < _disabledLights.Count; i++)
            {
                var light = _disabledLights[i];
                if (light != null) light.enabled = true;
            }

            _disabledLights.Clear();
        }

        // ---------- 场景事件 ----------

        private void HandleSceneLoaded(SceneLoadedEvent evt)
        {
            // Loading 场景不建容器
            if (evt.SceneName == _loadingSceneName) return;
            _run(CreateSceneContainerRoutine(evt.SceneName));
        }

        private void HandleSceneUnloaded(SceneUnloadedEvent evt)
        {
            DestroySceneContainer(evt.SceneName);
        }

        private IEnumerator CreateSceneContainerRoutine(string sceneName)
        {
            if (_scenes.Any(e => e.SceneName == sceneName))
            {
                if (_waitingScene == sceneName) _containerReady = true;
                yield break;
            }

            // ⚠ 关键顺序：必须在建容器**之前**把新场景设为 ActiveScene。
            // 原因：本方法是在 op.Activate() 内部由 sceneLoaded 事件触发的（早于切换协程的后续步骤），
            // 而场景模块（RegisterSceneServices）里新建的**无父物体**会归属当前 ActiveScene ——
            // 若此刻还是旧场景，它们会被随后的「卸载旧场景」一起静默销毁。
            // （把 SetActiveScene 放在切换协程的后半段是没用的：那时容器早已建完。）
            _sceneService.SetActiveScene(sceneName);

            var container = new ServiceCollection(_globalResolver);
            yield return _hooks.RegisterSceneServices(container, sceneName);

            _scenes.Add(new SceneEntry { SceneName = sceneName, Container = container });
            SetCurrent(container);
            if (string.IsNullOrEmpty(CurrentSceneName)) CurrentSceneName = sceneName;
            Log.Info($"场景容器建立：{sceneName}");

            if (_waitingScene == sceneName) _containerReady = true;
        }

        private void DestroySceneContainer(string sceneName)
        {
            var entry = _scenes.FirstOrDefault(e => e.SceneName == sceneName);
            if (entry == null) return;

            _scenes.Remove(entry);
            try { entry.Container.Dispose(); }
            catch (Exception e) { Debug.LogError(e); }

            var last = _scenes.LastOrDefault();
            SetCurrent(last?.Container ?? _globalResolver);
        }

        private void SetCurrent(IServiceResolver resolver)
        {
            Current = resolver;
            ServiceLocator.Set(resolver);
            ResolverChanged?.Invoke(resolver);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_attached)
            {
                _sceneService.SceneLoaded   -= HandleSceneLoaded;
                _sceneService.SceneUnloaded -= HandleSceneUnloaded;
            }
            for (int i = _scenes.Count - 1; i >= 0; i--)
            {
                try { _scenes[i].Container.Dispose(); } catch { }
            }
            _scenes.Clear();

            // 与 SetCurrent 的 ServiceLocator.Set 对称：容器已释放，不能再让别人解析到已销毁的服务。
            // （关闭域重载时它是静态字段，且不会随 Play 退出自动清零 → 下一局启动早期会解析到上一局的服务）
            ServiceLocator.Clear();
        }
    }
}