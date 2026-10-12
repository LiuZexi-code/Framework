using Framework.Bootstrap;

namespace Game.Bootstrap
{
    /// <summary>
    /// 业务引导参数（框架自动发现）。
    /// <para>
    /// ⚠ 注意：**场景相关信息现在由 <c>SceneCatalog</c> 配置提供**（见 <c>Game/Scenes/Config</c>），
    /// 且配置的优先级高于这里 —— 所以本类的场景名字段留空即可。
    /// 保留它主要是作为「如何提供引导参数」的示例（例如 <c>ScanPrefixes</c> 仍只能在这里配置）。
    /// </para>
    /// </summary>
    public sealed class GameBootstrapOptions : IBootstrapOptionsProvider
    {
        public BootstrapOptions CreateOptions() => new BootstrapOptions
        {
            // EntrySceneName / LoadingSceneName 留空：由 SceneCatalog 配置提供
            EntrySceneName = null,
            LoadingSceneName = null,
            // ScanPrefixes = null   // null = 用框架默认前缀（Abstractions/Adapters/Bootstrap/Game/Assembly-CSharp）
        };
    }
}
