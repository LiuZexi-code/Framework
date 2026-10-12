namespace Framework.Bootstrap
{
    /// <summary>
    /// 引导参数。业务可通过实现 <see cref="IBootstrapOptionsProvider"/> 提供，
    /// 或继承 <see cref="BootstrapRunner"/> 覆写 <c>CreateOptions()</c>，都**不需要改框架代码**。
    /// </summary>
    public sealed class BootstrapOptions
    {
        /// <summary>入口场景名；为 null/空时取 Build Settings 的第一个场景。</summary>
        public string EntrySceneName;

        /// <summary>常驻 Loading 场景名；为 null/空表示不使用 Loading 场景。</summary>
        public string LoadingSceneName;

        /// <summary>模块 / 配置 / 加载步骤扫描的程序集名前缀；为 null 用框架默认值。</summary>
        public string[] ScanPrefixes;
    }

    /// <summary>
    /// 业务提供引导参数（反射自动发现，最多一个；实现类需公开无参构造函数）。
    /// 好处：只改一个场景名也不需要继承引导流程。
    /// </summary>
    public interface IBootstrapOptionsProvider
    {
        BootstrapOptions CreateOptions();
    }
}
