using System;
using System.Collections;
using Framework.Core;

namespace Framework.Bootstrap
{
    /// <summary>
    /// 引导流程。GameRoot 只认这一个接口，不认任何具体服务。
    /// 所有服务注册、资源预加载、SceneFlow 创建都在实现里完成。
    /// </summary>
    public interface IBootstrapRunner : IDisposable
    {
        IEnumerator Run(ServiceCollection root, ICoroutineRunner coroutines);
    }
}