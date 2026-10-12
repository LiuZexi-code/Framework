using Framework.Abstractions.Assets;
using Framework.Abstractions.GameConfig;

namespace Game.Config
{
    /// <summary>
    /// 示例：纯代码配置（不需要任何资产，也不需要 Addressables 条目）。
    /// <para>
    /// 「新增配置」的接入成本 = <b>只加这一个文件</b>；启动代码零改动 ——
    /// <see cref="DemoTuningConfigFactory"/> 会被 <c>GameConfigRegistry</c> 自动发现，
    /// 启动流程自动完成「预热 → 注册 → 加载」。
    /// </para>
    /// </summary>
    public sealed class DemoTuningConfig : GameConfigBase
    {
        public DemoTuningConfig() : base("DemoTuning", loadOrder: 0) { }

        /// <summary>示例数值：初始金币。</summary>
        public int StartGold { get; private set; } = 100;

        public override void Load(IAssetService assetService)
        {
            // 无外部资产；如需外部数据，可在这里用 assetService / IStorage 覆盖默认值。
            IsLoaded = true;
        }
    }

    /// <summary>示例工厂：被自动发现，无需在启动代码中登记。</summary>
    public sealed class DemoTuningConfigFactory : IConfigFactory
    {
        public IGameConfig CreateConfig() => new DemoTuningConfig();
    }
}
