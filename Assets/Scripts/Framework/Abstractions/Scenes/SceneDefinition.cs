using System.Collections.Generic;

namespace Framework.Abstractions.Scenes
{
    /// <summary>
    /// 场景切换策略（**全局**，一个项目一个值）。
    /// <para>
    /// 本枚举把原来的「每条场景的 <c>loadMode</c>」与「全局 <c>swapPolicy</c>」**合并成一个旋钮**，
    /// 统一回答一个问题：**旧场景何时释放**（以及因此是否需要经过中间 Loading 场景）。
    /// 等价于一个 2×2 矩阵：
    /// </para>
    /// <list type="bullet">
    /// <item><description>Single 系（无共存窗口）：<see cref="SingleDirect"/>（激活时释放）/ <see cref="ThroughLoadingScene"/>（加载前释放）</description></item>
    /// <item><description>Additive 系（有共存窗口，逃生舱）：<see cref="Additive"/>（激活后卸载）/ <see cref="AdditiveUnloadFirst"/>（加载前卸载）</description></item>
    /// </list>
    /// </summary>
    public enum SceneTransitionPolicy
    {
        /// <summary>
        /// **推荐默认**：直接以 Single 加载目标场景。
        /// <para>
        /// 引擎在**激活新场景那一刻**原子关闭旧场景 → **没有共存窗口**
        /// （因此不需要静音监听器 / 关闭灯光的过渡补丁）。
        /// 目标就绪前旧场景一直可见，过渡画面连续（Loading 面板叠在其上）。
        /// </para>
        /// <para>代价：内存峰值 ≈ 旧 + 新。⚠️ 不支持常驻场景 / 子场景（Single 会摧毁它们）。</para>
        /// </summary>
        SingleDirect = 0,

        /// <summary>
        /// **经过中间 Loading 场景**：先以 Single 加载 Loading 场景（原子关闭旧场景），
        /// 再以 Single 加载目标场景（自动关闭 Loading 场景）—— 即 A → Loading → B。
        /// <para>
        /// **内存峰值最低**（旧场景在目标加载前就被释放），且过渡期有独立的加载画面。
        /// </para>
        /// <para>⚠️ 需要 Loading 场景自带相机（过渡期它是唯一场景，负责清屏）；不支持常驻场景 / 子场景。</para>
        /// </summary>
        ThroughLoadingScene = 1,

        /// <summary>
        /// **以 Additive 加载目标场景**（与旧场景共存），激活后再手工卸载旧场景。
        /// <para>
        /// 这是**唯一支持「常驻场景 / 子场景 / 交叉淡化过渡」的模式**（逃生舱）；
        /// 也是升级到本枚举之前的兼容值。
        /// </para>
        /// <para>代价：存在共存窗口（新旧场景同帧渲染）→ 框架会自动静音旧场景监听器、关闭其灯光。</para>
        /// </summary>
        Additive = 2,

        /// <summary>
        /// 先卸载旧场景，再以 Additive 加载目标场景（共享存玩法 + 低峰值）。
        /// <para>⚠️ 需要 Loading 场景自带相机（旧场景卸载后不能没有相机渲染）；加载失败没有退路。</para>
        /// </summary>
        AdditiveUnloadFirst = 3,
    }

    /// <summary>
    /// 场景的运行期只读定义。由配置资产（业务的 <c>SceneConfigSO</c>）转换而来。
    /// <para><see cref="Bootstrap.SceneFlow"/> 只读本类型，不反向依赖配置层。</para>
    /// </summary>
    public sealed class SceneDefinition
    {
        private static readonly string[] Empty = new string[0];

        public SceneDefinition(
            string name,
            string address = null,
            bool persistent = false,
            bool showLoading = true,
            IReadOnlyList<string> preloadAddresses = null,
            IReadOnlyList<string> tags = null,
            float waitTimeoutSeconds = 10f)
        {
            Name = name;
            Address = string.IsNullOrEmpty(address) ? name : address;
            Persistent = persistent;
            ShowLoading = showLoading;
            PreloadAddresses = preloadAddresses ?? Empty;
            Tags = tags ?? Empty;
            WaitTimeoutSeconds = waitTimeoutSeconds;
        }

        /// <summary>场景逻辑名（同时也是 Build Settings 中的场景名）。</summary>
        public string Name { get; }

        /// <summary>Addressables 地址；配置未填时等于 <see cref="Name"/>。</summary>
        public string Address { get; }

        /// <summary>常驻：切到其它场景时不卸载本场景（**仅 Additive 系策略支持**）。</summary>
        public bool Persistent { get; }

        /// <summary>进入本场景时是否显示 Loading（常驻 Loading 场景 + Loading 遮罩面板）。</summary>
        public bool ShowLoading { get; }

        /// <summary>进入本场景前要预加载的 Addressables 地址。</summary>
        public IReadOnlyList<string> PreloadAddresses { get; }

        /// <summary>场景标签（供面板 <c>SceneTags</c> / 资源筛选对齐）。</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>等待场景容器建立的超时（秒）；&lt;= 0 表示不限。</summary>
        public float WaitTimeoutSeconds { get; }

        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;

            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] == tag) return true;
            }
            return false;
        }

        public override string ToString()
            => $"{Name}({(Persistent ? "常驻" : "普通")}{(ShowLoading ? string.Empty : ",无Loading")})";
    }
}
