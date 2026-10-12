using System;
using System.Collections;
using System.Collections.Generic;

namespace Framework.Abstractions.GameConfig
{
    //配置加载服务
    public interface IGameConfigService
    {
        event Action<IGameConfig> OnConfigLoaded;

        void Register(IGameConfig config);

        T Get<T>() where T : class, IGameConfig;

        /// <summary>按**已注册的配置类型**查找（未注册返回 false，不抛异常）。</summary>
        bool TryGet<T>(out T config) where T : class, IGameConfig;

        void LoadAll();
    }
}
