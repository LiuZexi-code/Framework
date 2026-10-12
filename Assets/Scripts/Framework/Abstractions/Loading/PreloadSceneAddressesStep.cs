using System.Collections;
using Framework.Abstractions.Scenes;

namespace Framework.Abstractions.Loading
{
    /// <summary>
    /// 内置的**场景专属预加载**步骤：读取 <see cref="ISceneCatalog"/> 中目标场景的
    /// <see cref="SceneDefinition.PreloadAddresses"/> 并异步预热。
    /// <para>
    /// 目的是把「场景专属预加载」从业务硬编码（原先写在 <c>ISceneLoadStepProvider</c> 里按场景名分支）
    /// 搬到配置里。业务仍可用 <see cref="ISceneLoadStepProvider"/> 追加额外步骤。
    /// </para>
    /// <para>日志形如：<c>[Load] Level2 专属资源：1 个地址（Cube）</c>。</para>
    /// </summary>
    public sealed class PreloadSceneAddressesStep : ILoadStep
    {
        public string Name => "场景专属资源";

        public IEnumerator Run(ILoadContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.SceneName)) yield break;
            if (!context.Services.TryResolve<ISceneCatalog>(out var catalog) || catalog == null) yield break;
            if (!catalog.TryGet(context.SceneName, out var definition) || definition == null) yield break;

            var addresses = definition.PreloadAddresses;
            if (addresses == null || addresses.Count == 0) yield break;

            yield return new PreloadAddressesStep($"{context.SceneName} 专属资源", addresses).Run(context);
        }
    }
}
