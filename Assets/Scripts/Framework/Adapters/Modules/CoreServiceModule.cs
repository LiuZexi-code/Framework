using Framework.Abstractions.Assets;
using Framework.Abstractions.Events;
using Framework.Abstractions.GameConfig;
using Framework.Abstractions.Logging;
using Framework.Abstractions.Modules;
using Framework.Abstractions.Scenes;
using Framework.Abstractions.Storage;
using Framework.Abstractions.UI;
using Framework.Adapters.Assets;
using Framework.Adapters.Config;
using Framework.Adapters.Events;
using Framework.Adapters.Logging;
using Framework.Adapters.Scenes;
using Framework.Adapters.Storage;
using Framework.Core;
using Framework.UI;

namespace Framework.Adapters.Modules
{
    /// <summary>
    /// 框架内置核心服务模块：注册日志 / 事件 / 资源 / 存储 / 配置 / 场景 / UI。
    /// Order 取小值；业务模块用更大的 Order 即可覆盖默认实现。
    /// </summary>
    public sealed class CoreServiceModule : IGlobalServiceModule
    {
        public int Order => -1000;

        public void RegisterGlobal(IServiceRegistry registry)
        {
            // 容器已支持构造函数注入（2026-10-11）：直接按类型注册，依赖自动解析。
            registry.Register<ILogger, UnityLogger>();
            registry.Register<IEventBus, EventBus>();
            registry.Register<ISerializer, NewtonsoftJsonSerializer>();
            registry.Register<IStorage, FileStorage>();
            registry.Register<IAssetService, AddressablesAssetService>();
            registry.Register<ISceneService, UnitySceneService>();
            registry.Register<ISceneCatalog, SceneCatalog>();   // 场景目录：默认为空，业务在启动步骤里按配置填充
            registry.Register<IGameConfigService, ConfigService>();
            registry.Register(new UISettings());   // UI 缩放默认值，业务可在启动时按配置覆盖
            registry.Register<IPanelFactory, PanelFactory>();
            registry.Register<IUIService, UIService>();
        }
    }
}
