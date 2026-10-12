using System.Collections;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 一个可组合的加载步骤。
    /// <para>
    /// 启动流程与场景预加载**共用同一套步骤模型**：新增加载逻辑（读 StreamingAssets、远程配置、
    /// 表格预热、场景专属资源…）只需实现本接口，并通过
    /// <see cref="IStartupLoadStepProvider"/> / <see cref="ISceneLoadStepProvider"/> 注册，
    /// 无需改动任何引导代码。
    /// </para>
    /// </summary>
    public interface ILoadStep
    {
        /// <summary>阶段名（用于日志与进度显示）。</summary>
        string Name { get; }

        /// <summary>执行加载；用 <see cref="ILoadContext.Report"/> 上报局部进度。</summary>
        IEnumerator Run(ILoadContext context);
    }
}
