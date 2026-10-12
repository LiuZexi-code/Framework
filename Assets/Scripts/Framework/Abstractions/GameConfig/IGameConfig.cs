using Framework.Abstractions.Assets;

namespace Framework.Abstractions.GameConfig
{
    //配置接口
    public interface IGameConfig
    {
        /// <summary>
        /// 配置ID
        /// </summary>
        string ConfigId { get; }
        string LoadAddress { get; }
        /// <summary>
        /// 加载优先级
        /// </summary>
        int LoadOrder { get; }
        /// <summary>
        /// 配置是否需要持久化
        /// </summary>
        bool IsPersistent { get; }
        /// <summary>
        /// 配置是否已加载
        /// </summary>
        bool IsLoaded { get; }

        void Load(IAssetService assetService);
    }
}
