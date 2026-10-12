using System;
using System.Collections;
using Framework.Abstractions.Scenes;
using Framework.Core;

namespace Framework.Bootstrap
{
    public interface ISceneFlow : IDisposable
    {
        string CurrentSceneName { get; }
        IServiceResolver Current { get; }
        event Action<IServiceResolver> ResolverChanged;

        /// <summary>订阅 ISceneService 的事件；由 GameRoot 在启动时调用一次。</summary>
        void Attach();

        /// <summary>fire-and-forget 切换（内部跑协程）。</summary>
        void SwitchScene(string sceneName);

        /// <summary>由启动流程 yield 的初始化入口场景。</summary>
        IEnumerator InitializeEntryScene(string entryScene);

        /// <summary>查询场景定义（来自场景目录 <see cref="ISceneCatalog"/>）；未登记返回 null。</summary>
        SceneDefinition GetDefinition(string sceneName);
    }
}