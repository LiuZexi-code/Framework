namespace Framework.Abstractions.UI
{
    /// <summary>
    /// UI 排序空间：把「层级」映射为 <c>Canvas.sortingOrder</c> 的基址，并给层内预留 1000 的优先级跨度。
    /// <para>
    /// 规则：**层优先，优先级只在层内生效**。整段区间都 &gt; 场景 Canvas 的常见值 0，
    /// 保证运行时创建的 UI（<c>DontDestroyOnLoad</c>）始终盖在业务场景的 Canvas 之上。
    /// </para>
    /// <para>
    /// 取值落在 <c>1_000 ~ 6_999</c>，安全落在 <c>Canvas.sortingOrder</c> 的 int16 表达范围内。
    /// </para>
    /// </summary>
    public static class UILayerOrder
    {
        /// <summary>基址：留出 1000 的头部，避免与场景 Canvas 的 0 打平手。</summary>
        public const int Base = 1000;

        /// <summary>每层占用的跨度（= 层内优先级的取值范围）。</summary>
        public const int Step = 1000;

        /// <summary>层内允许的最大优先级（含）。留 1 个空位给「层 Canvas 基址」，让面板永远高于自己的层 Canvas。</summary>
        public const int MaxPriority = Step - 2;

        /// <summary>某个层级的基址（层 Canvas 用这个值）。</summary>
        public static int BaseOf(PanelLayer layer) => Base + (int)layer * Step;

        /// <summary>
        /// 层内某个优先级对应的面板 <c>Canvas.sortingOrder</c>。
        /// 固定比所在层 Canvas 基址高 1，避免与层 Canvas 同值打平手；优先级夹到 [0, <see cref="MaxPriority"/>]。
        /// </summary>
        public static int ForPanel(PanelLayer layer, int priority)
            => BaseOf(layer) + 1 + ClampPriority(priority);

        /// <summary>把优先级夹到合法区间（不依赖 UnityEngine.Mathf，便于纯逻辑测试）。</summary>
        public static int ClampPriority(int priority)
            => priority < 0 ? 0 : (priority > MaxPriority ? MaxPriority : priority);
    }
}
