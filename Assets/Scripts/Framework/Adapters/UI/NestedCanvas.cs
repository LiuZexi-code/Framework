using UnityEngine;

namespace Framework.UI
{
    /// <summary>
    /// 嵌套 Canvas 的排序配置 —— **唯一入口**。
    /// <para>
    /// 之所以要收口：这里有两条「写错也不报错、只在 Play 里悄悄失效」的 Unity 行为，
    /// 本项目已经各踩过一次（层 Canvas 排序全丢、模态遮罩失效）。
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>必须先挂到最终父级，再配置 Canvas。</b>
    /// 实测：先 <c>overrideSorting = true; sortingOrder = 4010;</c> 再 <c>SetParent</c>，
    /// <c>SetParent</c> 会**静默重置** overrideSorting —— 读回 <c>override=false</c>、
    /// 且 <c>sortingOrder</c> 变成**父级的 order（4000）**；调换顺序才正确（<c>true / 4010</c>）。
    /// 所以「重挂父级」之后**必须**重新调用本类。
    /// </description></item>
    /// <item><description>
    /// <b>必须先写 <c>overrideSorting = true</c>，再写 <c>sortingOrder</c>。</b>
    /// 嵌套 Canvas 在 <c>overrideSorting == false</c> 时会**丢弃** sortingOrder（实测读回 0），
    /// 事后补开也不会恢复。
    /// </description></item>
    /// </list>
    /// <para>
    /// ⚠️ 这两个行为都**不抛异常、不打日志**，只能靠读回值验证 —— 改动本类或调用点后，
    /// 请务必跑一次 Play 验证（读回 <c>Canvas.overrideSorting</c> / <c>Canvas.sortingOrder</c>）。
    /// </para>
    /// </summary>
    internal static class NestedCanvas
    {
        /// <summary>
        /// 把 <paramref name="go"/> 挂到 <paramref name="parent"/> 下并配置排序。
        /// <paramref name="parent"/> 为 <c>null</c> 表示**根 Canvas**（此时不开 overrideSorting）。
        /// <para>把「定父级」和「配排序」合成一个方法，是为了让上面第 1 条规则**结构上无法写错**。</para>
        /// </summary>
        public static Canvas Configure(GameObject go, Transform parent, int sortingOrder)
        {
            if (go == null) return null;

            // ① 先定父级（SetParent 会重置 overrideSorting）
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // ② 后配排序：override 先、order 后
            canvas.overrideSorting = parent != null;
            canvas.sortingOrder = sortingOrder;

            return canvas;
        }

        /// <summary>
        /// 只改排序（父级不变）。⚠️ 只要中途发生过 <c>SetParent</c>，就必须调用本方法重写排序，
        /// 因为它会按当前父级把 <c>overrideSorting</c> 重新拉正。
        /// </summary>
        public static void SetOrder(Canvas canvas, int sortingOrder)
        {
            if (canvas == null) return;

            canvas.overrideSorting = canvas.transform.parent != null;
            canvas.sortingOrder = sortingOrder;
        }
    }
}
