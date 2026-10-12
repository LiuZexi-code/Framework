using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.GameConfig;
using Framework.Abstractions.Loading;
using Framework.Abstractions.UI;
using Framework.Adapters.Config;
using Framework.Adapters.Logging;
using Framework.UI;
using Game.UI.Config;

namespace Game.Bootstrap
{
    /// <summary>
    /// 业务启动步骤：应用 UI 配置（参考分辨率/匹配模式）并注册全部 UI 面板。
    /// <para>
    /// 框架会自动发现本提供者（<see cref="IStartupLoadStepProvider"/>），
    /// 因此**业务层不需要再写任何引导代码**（原先这段逻辑写在 <c>MyBootstrapRunner</c> 里）。
    /// </para>
    /// </summary>
    public sealed class UIPanelSetupStep : IStartupLoadStepProvider
    {
        public void Collect(IList<ILoadStep> steps, ILoadContext context)
            => steps.Add(new RegisterPanelsStep());

        private sealed class RegisterPanelsStep : ILoadStep
        {
            public string Name => "注册 UI 面板";

            public IEnumerator Run(ILoadContext context)
            {
                var configService = context.Services.Resolve<IGameConfigService>();
                if (!configService.TryGet<ConfigBySO<UIConfigAssetSO>>(out var uiConfig) || uiConfig.Data == null)
                {
                    Log.Warn("[UI] 未找到 UIConfigAsset 配置，跳过 UI 面板注册");
                    yield break;
                }

                // UI 缩放参数：必须在首次解析 IUIService 之前写入（UIRoot 就在那一刻创建）
                var settings = context.Services.Resolve<UISettings>();
                settings.ReferenceResolution = uiConfig.Data.referenceResolution;
                settings.MatchWidthOrHeight = uiConfig.Data.matchWidthOrHeight;
                Log.Info($"UI 配置已应用：参考分辨率 {settings.ReferenceResolution.x}×{settings.ReferenceResolution.y}，" +
                         $"MatchWidthOrHeight={settings.MatchWidthOrHeight}");

                // 面板注册：panelKey → 面板脚本（[UIPanel]），PrefabAddress → 预制体地址
                PanelTypeRegistry.Scan();
                var uiService = context.Services.Resolve<IUIService>();

                int registered = 0;
                foreach (var panelSO in uiConfig.Data.panels)
                {
                    if (panelSO == null) continue;

                    var panelType = PanelTypeRegistry.Get(panelSO.panelKey);
                    if (panelType == null)
                    {
                        Log.Warn($"[UI] 未找到面板类型：key={panelSO.panelKey}");
                        continue;
                    }

                    uiService.Register(new PanelDefinition(
                        panelType,
                        panelSO.PrefabAddress,
                        panelSO.Layer,
                        panelSO.cacheOnClose,
                        panelSO.isGlobal,
                        panelSO.sceneTags,
                        panelSO.loadPolicy,
                        panelSO.priority,
                        panelSO.isModal));

                    registered++;
                }

                Log.Info($"已注册 UI 面板：{registered} 个");
                context.Report(Name, 1f);
            }
        }
    }
}
