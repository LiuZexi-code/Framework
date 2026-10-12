using System;
using System.Collections.Generic;
using Framework.Abstractions.Assets;
using Framework.Abstractions.GameConfig;
using UnityEngine;
using ILogger = Framework.Abstractions.Logging.ILogger;

namespace Framework.Adapters
{
    public class ConfigService : IGameConfigService
    {
        private readonly ILogger _logger;
        private readonly IAssetService _assetService;

        private readonly Dictionary<Type, IGameConfig> _configs = new();
        private readonly List<IGameConfig> _ordered = new();

        public event Action<IGameConfig> OnConfigLoaded;

        public ConfigService(ILogger logger,IAssetService assetService)
        {
            _logger = logger;
            _assetService = assetService;
        }
        //注册config
        //传入IGameConfig可获取运行时类型，而泛型是编译时类型如果传入IGameConfig无法获取真实类型
        public void Register(IGameConfig config)
        {
            if (config == null) return;
            var type = config.GetType();
            if (_configs.ContainsKey(type)) return;

            _configs[type] = config;
            _ordered.Add(config);
            _ordered.Sort((a, b) => a.LoadOrder.CompareTo(b.LoadOrder));
        }

        public T Get<T>() where T : class, IGameConfig
        {
            if (_configs.TryGetValue(typeof(T), out var config))
                return config as T;

            throw new Exception($"Config not registered: {typeof(T).Name}");
        }

        public bool TryGet<T>(out T config) where T : class, IGameConfig
        {
            if (_configs.TryGetValue(typeof(T), out var value) && value is T t)
            {
                config = t;
                return true;
            }

            config = null;
            return false;
        }

        //为config加载数据
        public void LoadAll()
        {
            foreach (var config in _ordered)
            {
                try
                {
                    config.Load(_assetService);
                    OnConfigLoaded?.Invoke(config);
                }
                catch (Exception e)
                {
                    _logger.Error($"Load config failed: {config.GetType().Name + config.ConfigId}\n{e}");
                }
            }
        }
    }
}