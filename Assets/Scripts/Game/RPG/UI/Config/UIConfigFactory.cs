using Framework.Abstractions.GameConfig;
using Framework.Adapters.Config;
using Framework.Core;

namespace Game.RPG.UI.Config
{
    /// <summary>
    /// UIConfigAsset 的注册工厂：启动时被 <c>GameConfigRegistry</c> 自动发现，
    /// 因此新增/修改 UI 配置不需要改动任何启动代码。
    /// </summary>
    public class UIConfigFactory : IConfigFactory
    {
        public IGameConfig CreateConfig()
        {
            return new ConfigBySO<UIConfigAssetSO>(
                "Assets/Scripts/Game/RPG/UI/Config/UIConfigAsset.asset",
                configId: "UIConfig",
                loadOrder: -100);   // UI 配置优先加载（面板注册依赖它）
        }
    }
}
