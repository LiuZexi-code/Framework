using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.GameConfig;

namespace Framework.Abstractions.Loading
{
    /// <summary>注册并加载一批配置（对应原来的 <c>Register</c> + <c>LoadAll</c>）。</summary>
    public sealed class LoadConfigsStep : ILoadStep
    {
        private readonly List<IGameConfig> _configs;

        public string Name => "加载配置";
        public int Count => _configs.Count;

        public LoadConfigsStep(IEnumerable<IGameConfig> configs)
        {
            _configs = new List<IGameConfig>();
            if (configs == null) return;

            foreach (var config in configs)
                if (config != null) _configs.Add(config);
        }

        public IEnumerator Run(ILoadContext context)
        {
            var configService = context.Services.Resolve<IGameConfigService>();
            if (configService == null) yield break;

            foreach (var config in _configs)
                configService.Register(config);

            configService.LoadAll();
            context.Report(Name, 1f);
        }
    }
}
