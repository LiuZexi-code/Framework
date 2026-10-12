using Framework.Core;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 一次加载过程的上下文：既用于「启动阶段」，也用于「进入某个场景前的预加载」。
    /// </summary>
    public interface ILoadContext
    {
        /// <summary>服务解析器（用来取 <c>IAssetService</c> / <c>IGameConfigService</c> / <c>IUIService</c> …）。</summary>
        IServiceResolver Services { get; }

        /// <summary>目标场景名；启动阶段为 <c>null</c>。</summary>
        string SceneName { get; }

        /// <summary>
        /// 上报当前阶段的局部进度（0..1）。加载管线会把它换算成整体进度。
        /// </summary>
        void Report(string stage, float localProgress);
    }
}
