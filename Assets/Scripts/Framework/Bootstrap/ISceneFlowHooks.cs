using System;
using System.Collections;
using System.Collections.Generic;
using Framework.Core;

namespace Framework.Bootstrap
{
    /// <summary>业务侧提供的场景生命周期钩子；框架给一个默认实现兜底。</summary>
    public interface ISceneFlowHooks
    {
        IEnumerator RegisterSceneServices(ServiceCollection container, string sceneName);

        /// <summary>
        /// 场景资源预加载（切换进度 5%→30% 的窗口）。
        /// 用 <paramref name="reportProgress"/> 上报局部进度 0..1，框架会换算成整体进度。
        /// </summary>
        IEnumerator SceneResourcePreload(IServiceResolver resolver, string sceneName, Action<float> reportProgress);

        void SceneResourceRelease(IServiceResolver resolver, string sceneName);
        // ↓ Loading 相关
        IEnumerator OnLoadingBegin(IServiceResolver resolver, string targetScene);

        /// <summary>进度上报（0..1）。同步方法，由加载流程按帧调用（不阻塞）。</summary>
        void OnLoadingProgress(IServiceResolver resolver, string targetScene, float progress);

        IEnumerator OnLoadingEnd(IServiceResolver resolver, string targetScene);
    }
}