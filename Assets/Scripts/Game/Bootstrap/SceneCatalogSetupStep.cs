using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.GameConfig;
using Framework.Abstractions.Loading;
using Framework.Abstractions.Scenes;
using Framework.Adapters.Config;
using Framework.Adapters.Logging;
using Game.Scenes.Config;

namespace Game.Bootstrap
{
    /// <summary>
    /// 业务启动步骤：把 <see cref="SceneConfigAssetSO"/> 转换为运行期场景目录（<see cref="ISceneCatalog"/>）。
    /// <para>
    /// 与 <c>UIPanelSetupStep</c> 对称：框架自动发现本提供者（<see cref="IStartupLoadStepProvider"/>），
    /// 业务层不需要写任何引导代码。配置缺失时只告警、不阻断启动
    /// （此时目录为空，切换会回退为「SingleDirect + 不常驻 + 显示 Loading」的默认策略）。
    /// </para>
    /// </summary>
    public sealed class SceneCatalogSetupStep : IStartupLoadStepProvider
    {
        public void Collect(IList<ILoadStep> steps, ILoadContext context)
            => steps.Add(new BuildSceneCatalogStep());

        private sealed class BuildSceneCatalogStep : ILoadStep
        {
            public string Name => "场景目录";

            public IEnumerator Run(ILoadContext context)
            {
                if (!context.Services.TryResolve<ISceneCatalog>(out var catalog) || catalog == null)
                {
                    Log.Warn("[Scenes] 未注册 ISceneCatalog，跳过场景配置");
                    yield break;
                }

                if (!context.Services.TryResolve<IGameConfigService>(out var configService) ||
                    !configService.TryGet<ConfigBySO<SceneConfigAssetSO>>(out var config) ||
                    config == null || config.Data == null)
                {
                    Log.Warn("[Scenes] 未找到 SceneCatalog 配置，场景目录为空（切换将回退为默认策略）");
                    yield break;
                }

                var data = config.Data;
                catalog.EntrySceneName = data.entrySceneName;
                catalog.LoadingSceneName = data.loadingSceneName;
                catalog.TransitionPolicy = data.transitionPolicy;
                catalog.Strict = data.strict;

                int registered = 0;
                foreach (var item in data.scenes)
                {
                    if (item == null) continue;

                    var definition = item.ToDefinition();
                    if (definition == null)
                    {
                        Log.Warn("[Scenes] 存在 sceneName 为空的场景配置项，已跳过");
                        continue;
                    }

                    catalog.Register(definition);
                    registered++;
                }

                Log.Info($"[Scenes] 场景目录：{registered} 个" +
                         $"（入口={Display(catalog.EntrySceneName)}，Loading={Display(catalog.LoadingSceneName)}，" +
                         $"policy={catalog.TransitionPolicy}，strict={catalog.Strict}）");

                SceneConfigValidation.Validate(catalog, data);
                context.Report(Name, 1f);
            }

            private static string Display(string value) => string.IsNullOrEmpty(value) ? "<无>" : value;
        }
    }
}
