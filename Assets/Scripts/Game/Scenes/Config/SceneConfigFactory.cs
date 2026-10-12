using Framework.Abstractions.GameConfig;
using Framework.Adapters.Config;

namespace Game.Scenes.Config
{
    /// <summary>
    /// SceneCatalog 的注册工厂：启动时被 <c>GameConfigRegistry</c> 自动发现，
    /// 因此新增/修改场景配置不需要改动任何启动代码。
    /// <para>loadOrder 取 -200（早于 UI 的 -100）：入口/Loading 场景名是启动流程最早需要的信息。</para>
    /// </summary>
    public sealed class SceneConfigFactory : IConfigFactory
    {
        public IGameConfig CreateConfig()
        {
            return new ConfigBySO<SceneConfigAssetSO>(
                "Assets/Scripts/Game/Scenes/Config/SceneCatalog.asset",
                configId: "Scenes",
                loadOrder: -200);
        }
    }
}
