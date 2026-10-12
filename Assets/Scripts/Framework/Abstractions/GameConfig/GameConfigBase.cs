using System.Collections;
using System.Collections.Generic;
using Framework.Abstractions.Assets;
using Framework.Core;

namespace Framework.Abstractions.GameConfig
{
    public abstract class GameConfigBase : IGameConfig
    {
        /// <param name="configId">配置标识；为空时取实现类型名。</param>
        /// <param name="loadOrder">加载顺序（升序，小者先加载）。</param>
        protected GameConfigBase(string configId = null, int loadOrder = 0)
        {
            ConfigId = string.IsNullOrWhiteSpace(configId) ? GetType().Name : configId;
            LoadOrder = loadOrder;
        }

        public string ConfigId { get; }
        public string LoadAddress { get; protected set; }
        public int LoadOrder { get; }
        public virtual bool IsPersistent => false;
        public bool IsLoaded { get; protected set; }
        public abstract void Load(IAssetService assetService);
    }
}
