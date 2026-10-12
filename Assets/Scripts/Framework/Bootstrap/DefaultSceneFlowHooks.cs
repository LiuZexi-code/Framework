
using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Loading;
using Framework.Abstractions.UI;
using Framework.Bootstrap.Modules;
using Framework.Core;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 框架默认场景钩子：Loading 面板显隐/进度、场景级服务注册、**场景资源预加载**。
    /// 需要不同行为时，实现 <see cref="ISceneFlowHooks"/> 并在引导流程里替换（见 <c>BootstrapRunner.CreateHooks</c>）。
    /// 注意：这里使用 ASCII 文案；默认的 LiberationSans SDF 不含中文字形，
    /// 如需中文请先给 Loading 面板的 TMP 文本指派包含中文字形的字体资产。
    /// </summary>
    public sealed class DefaultSceneFlowHooks : ISceneFlowHooks
    {
        private ILoadingPanel _loadingPanel;
        public IEnumerator RegisterSceneServices(ServiceCollection c, string sceneName)
        {
            // 按约定为每个场景注册场景级服务：实现 ISceneServiceModule 即可，无需改这里。
            ServiceModuleRegistry.RegisterSceneModules(c, sceneName);
            yield return null;
        }

        /// <summary>
        /// 场景资源预加载（切换进度 5%→30% 窗口）。
        /// 默认管线：① 按面板配置预加载（<c>loadPolicy == Preload</c> 且 <c>sceneTags</c> 匹配）
        /// ② 业务通过 <see cref="ISceneLoadStepProvider"/> 注册的步骤。
        /// </summary>
        public IEnumerator SceneResourcePreload(IServiceResolver r, string sceneName, Action<float> reportProgress)
        {
            var context = new LoadContext(r, sceneName);
            var steps = LoadStepCollector.CollectScene(context);
            yield return LoadPipeline.Run(steps, context, (stage, progress) => reportProgress?.Invoke(progress));
        }

        public void SceneResourceRelease(IServiceResolver r, string leavingScene, string enteringScene)
        {
            // 场景退出：销毁「不属于新场景」的面板实例（全局 + 标签命中新场景的实例保留）。
            // ⚠ 必须把**进入的场景**一并传下去：为进入场景预加载的面板不能被这一步销毁。
            if (r != null && r.TryResolve(out IUIService uiService))
                uiService.DisposeScenePanels(enteringScene);
        }

        public IEnumerator OnLoadingBegin(IServiceResolver r, string target)
        {
            if (r.TryResolve(out IUIService uiService))
            {
                var task = uiService.OpenAsync<ILoadingPanel>();
                while (!task.IsCompleted) yield return null;
                _loadingPanel = task.Result;
                if (_loadingPanel != null)
                    _loadingPanel.SetProgress(0, "加载中...");
            }
            yield return null;
        }

        public void OnLoadingProgress(IServiceResolver r, string target, float p)
        {
            if (_loadingPanel != null)
                _loadingPanel.SetProgress(p, "正在加载场景...");
        }

        public IEnumerator OnLoadingEnd(IServiceResolver r, string target)
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetProgress(0, "加载中...");
                _loadingPanel = null;
            }
            if (r.TryResolve(out IUIService uiService))
            {
                uiService.Close<ILoadingPanel>();
            }
            yield return null;
        }
    }
}
