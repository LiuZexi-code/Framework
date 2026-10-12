using System.Collections.Generic;

namespace Framework.Abstractions.Scenes
{
    /// <summary>
    /// 场景目录：运行期查询「场景定义」。对称于 <c>IUIService.Register(PanelDefinition)</c>。
    /// <para>
    /// 由框架核心模块注册为空实现（无任何定义），业务在启动步骤里按配置填充。
    /// 一个定义都没登记时，查询会回退到「宽松默认」（Additive / 不常驻 / 显示 Loading），
    /// 因此**零配置项目的行为与引入本目录之前完全一致**。
    /// </para>
    /// </summary>
    public interface ISceneCatalog
    {
        /// <summary>入口场景名（可为空，表示由启动流程自行决定）。</summary>
        string EntrySceneName { get; set; }

        /// <summary>常驻 Loading 场景名（可为空，表示不使用 Loading 场景）。</summary>
        string LoadingSceneName { get; set; }

        /// <summary>切换策略（全局；统一决定旧场景何时释放 / 是否经过中间 Loading 场景）。</summary>
        SceneTransitionPolicy TransitionPolicy { get; set; }

        /// <summary>未登记场景是否视为错误（true = 记录错误并让切换中止）。</summary>
        bool Strict { get; set; }

        /// <summary>已登记的全部定义（按登记顺序）。</summary>
        IReadOnlyList<SceneDefinition> All { get; }

        /// <summary>登记（或覆盖同名的）场景定义。</summary>
        void Register(SceneDefinition definition);

        /// <summary>查询场景定义；未登记返回 false。</summary>
        bool TryGet(string sceneName, out SceneDefinition definition);

        /// <summary>查询场景定义；未登记返回 null（不抛异常）。</summary>
        SceneDefinition Get(string sceneName);
    }
}
