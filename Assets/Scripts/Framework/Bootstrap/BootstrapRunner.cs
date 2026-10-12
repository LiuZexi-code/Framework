using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Loading;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Logging;
using Framework.Bootstrap.Modules;
using Framework.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 框架**通用引导流程**（业务通常不需要写这个类）。
    /// <para>
    /// 流程：模块扫描注册 → 日志门面 → 启动加载管线（配置预热/加载 + 业务步骤）
    /// → 创建并装配 <see cref="SceneFlow"/> → 常驻 Loading 场景 → 进入口场景。
    /// </para>
    /// <para>
    /// 定制方式（按需选一种，都不需要改框架代码）：
    /// ① 只改场景名 → 实现 <see cref="IBootstrapOptionsProvider"/>；
    /// ② 加启动/场景预加载步骤 → 实现 <see cref="IStartupLoadStepProvider"/> / <see cref="ISceneLoadStepProvider"/>；
    /// ③ 想改流程本身 → 继承本类并覆写钩子（框架会自动发现，见 <see cref="CreateDefault"/>）。
    /// </para>
    /// </summary>
    public class BootstrapRunner : IBootstrapRunner
    {
        private const string DefaultEntrySceneLog = "[Bootstrap] 未指定入口场景且 Build Settings 无可用场景，跳过入口场景初始化";

        private SceneFlow _sceneFlow;
        private BootstrapOptions _options;
        private string _lastReportedStage;

        // ---------- 主流程 ----------

        public IEnumerator Run(ServiceCollection root, ICoroutineRunner coroutines)
        {
            _options = CreateOptions() ?? new BootstrapOptions();

            // 1) 扫描并注册所有服务模块（框架内置 CoreServiceModule + 业务模块）
            ServiceModuleRegistry.Scan(_options.ScanPrefixes);
            ServiceModuleRegistry.RegisterGlobalModules(root);

            // 2) 静态日志门面（先 Clear 再 Init：确保每次启动都重新绑定，避免残留上次会话的日志实例）
            Log.Clear();
            if (root.TryResolve<ILogger>(out var logger)) Log.Init(logger);
            ServiceCollection.DisposeErrorHandler = e => Log.Error(e.ToString());

            // 3) 启动加载管线：内置「预热配置 → 加载配置」+ 业务注册的启动步骤
            var context = new LoadContext(root);
            var steps = new List<ILoadStep>();
            CollectStartupSteps(root, context, steps);
            yield return LoadPipeline.Run(steps, context, OnStartupProgress);

            // 4) 场景流（场景目录已由业务的启动步骤按配置填充）
            var catalog = root.TryResolve<ISceneCatalog>(out var resolvedCatalog) ? resolvedCatalog : null;

            // 零配置项目兼容：目录未提供 Loading 场景名时回退到 BootstrapOptions
            if (catalog != null && string.IsNullOrEmpty(catalog.LoadingSceneName))
                catalog.LoadingSceneName = _options.LoadingSceneName;

            _sceneFlow = new SceneFlow(
                globalResolver: root,
                sceneService: root.Resolve<ISceneService>(),
                hooks: CreateHooks(),
                coroutineRunner: coroutines.Run,
                loadingSceneName: _options.LoadingSceneName,
                catalog: catalog);

            _sceneFlow.Attach();
            root.Register<ISceneFlow>(_sceneFlow);

            // 5) 常驻 Loading 场景（常驻但默认隐藏）
            yield return _sceneFlow.InitializeLoadingScene();

            // 6) 入口场景（场景目录优先 → BootstrapOptions → Build Settings 第 0 个）
            var configuredEntry = catalog != null && !string.IsNullOrEmpty(catalog.EntrySceneName)
                ? catalog.EntrySceneName
                : _options.EntrySceneName;

            var entryScene = ResolveEntryScene(configuredEntry);
            if (string.IsNullOrEmpty(entryScene)) Log.Warn(DefaultEntrySceneLog);
            else yield return _sceneFlow.InitializeEntryScene(entryScene);

            yield return OnStartupCompleted(root);
            Log.Info("启动完成");
        }

        // ---------- 可覆写钩子 ----------

        /// <summary>引导参数：优先用业务实现的 <see cref="IBootstrapOptionsProvider"/>，否则用默认值。</summary>
        protected virtual BootstrapOptions CreateOptions()
            => DiscoverOptionsProvider() ?? new BootstrapOptions();

        /// <summary>
        /// 组装启动阶段的加载步骤。默认 = <see cref="LoadStepCollector.CollectStartup"/>；
        /// 覆写时建议先调用基类再增删。
        /// </summary>
        protected virtual void CollectStartupSteps(ServiceCollection root, ILoadContext context, List<ILoadStep> steps)
            => steps.AddRange(LoadStepCollector.CollectStartup(context, ScanPrefixes));

        /// <summary>场景生命周期钩子；默认实现负责 Loading 面板、场景模块注册与场景预加载。</summary>
        protected virtual ISceneFlowHooks CreateHooks() => new DefaultSceneFlowHooks();

        /// <summary>启动全部完成后的回调（业务可在此打开首个界面等）。</summary>
        protected virtual IEnumerator OnStartupCompleted(ServiceCollection root) { yield break; }

        /// <summary>启动进度回调（默认每个阶段只打一行日志，避免刷屏）。</summary>
        protected virtual void OnStartupProgress(string stage, float progress)
        {
            if (string.IsNullOrEmpty(stage) || stage == _lastReportedStage) return;
            _lastReportedStage = stage;
            Log.Info($"[Bootstrap] {stage}…");
        }

        public virtual void Dispose() => _sceneFlow?.Dispose();

        // ---------- 内部 ----------

        /// <summary>本次生效的引导参数（仅在 Run 期间有效）。</summary>
        protected string[] ScanPrefixes => _options?.ScanPrefixes;

        /// <summary>入口场景：业务指定 → 否则取 Build Settings 第一个场景。</summary>
        protected static string ResolveEntryScene(string configured)
        {
            if (!string.IsNullOrEmpty(configured)) return configured;

            var path = SceneUtility.GetScenePathByBuildIndex(0);
            return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        private static BootstrapOptions DiscoverOptionsProvider()
        {
            Type found = null;
            foreach (var type in ServiceModuleRegistry.EnumerateTypes())
            {
                if (!typeof(IBootstrapOptionsProvider).IsAssignableFrom(type)) continue;
                found = type;
                break;
            }

            if (found == null) return null;

            try
            {
                var ctor = found.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    Debug.LogError($"[Bootstrap] {found.Name} 需要公开无参构造函数，已忽略");
                    return null;
                }
                return ((IBootstrapOptionsProvider)ctor.Invoke(null)).CreateOptions();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bootstrap] {found.Name} 提供引导参数失败：{e}");
                return null;
            }
        }

        /// <summary>
        /// 反射发现业务自定义的 <see cref="IBootstrapRunner"/>（含 <see cref="BootstrapRunner"/> 的子类）；
        /// 没有则用框架默认实现。
        /// </summary>
        public static IBootstrapRunner CreateDefault(string[] scanPrefixes = null)
        {
            Type found = null;
            int count = 0;

            foreach (var type in ServiceModuleRegistry.EnumerateTypes(scanPrefixes))
            {
                if (type == typeof(BootstrapRunner)) continue;
                if (!typeof(IBootstrapRunner).IsAssignableFrom(type)) continue;

                count++;
                if (found == null) found = type;
            }

            if (found == null)
            {
                Debug.Log("[Bootstrap] 使用框架默认引导流程");
                return new BootstrapRunner();
            }

            if (count > 1)
                Debug.LogError($"[Bootstrap] 发现 {count} 个 IBootstrapRunner 实现，将使用 {found.Name}；请只保留一个");

            var ctor = found.GetConstructor(Type.EmptyTypes);
            if (ctor == null)
            {
                Debug.LogError($"[Bootstrap] {found.Name} 需要公开无参构造函数，改用默认引导流程");
                return new BootstrapRunner();
            }

            Debug.Log($"[Bootstrap] 使用自定义引导流程：{found.Name}");
            return (IBootstrapRunner)ctor.Invoke(null);
        }
    }
}
