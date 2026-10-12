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

        /// <summary>
        /// 退出 <paramref name="leavingScene"/> 时的资源释放钩子。
        /// <para>
        /// <paramref name="enteringScene"/> 是**即将进入**的场景名，必须传给需要按场景归属做清理的实现。
        /// 典型例子：面板清理的判据是「**不属于新场景**」——为进入场景预加载的面板必须保留，
        /// 只看 <paramref name="leavingScene"/> 会把刚预加载的东西误删（此缺陷已实测复现）。
        /// </para>
        /// </summary>
        void SceneResourceRelease(IServiceResolver resolver, string leavingScene, string enteringScene);
        // ↓ Loading 相关
        IEnumerator OnLoadingBegin(IServiceResolver resolver, string targetScene);

        /// <summary>进度上报（0..1）。同步方法，由加载流程按帧调用（不阻塞）。</summary>
        void OnLoadingProgress(IServiceResolver resolver, string targetScene, float progress);

        IEnumerator OnLoadingEnd(IServiceResolver resolver, string targetScene);
    }
}