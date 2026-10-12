using System.Collections.Generic;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 加载步骤提供者：由框架反射自动发现（和 <c>IServiceModule</c> / <c>IConfigFactory</c> 同一约定），
    /// 放在被扫描的程序集里即可生效，**不需要改引导代码**。
    /// <para>实现类需具备公开无参构造函数。步骤按发现顺序执行；若需要精确顺序，请在一个提供者里依次 <c>Add</c>。</para>
    /// </summary>
    public interface ILoadStepProvider
    {
        void Collect(IList<ILoadStep> steps, ILoadContext context);
    }

    /// <summary>启动阶段的额外加载步骤（在「预热配置地址 → 加载配置」之后执行）。</summary>
    public interface IStartupLoadStepProvider : ILoadStepProvider { }

    /// <summary>进入场景前的预加载步骤（在切换进度 5%→30% 的窗口内执行）。</summary>
    public interface ISceneLoadStepProvider : ILoadStepProvider { }
}
