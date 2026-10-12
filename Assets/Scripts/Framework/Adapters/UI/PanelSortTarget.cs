using UnityEngine;

namespace Framework.UI
{
    /// <summary>
    /// 标记「由 <see cref="PanelFactory"/> 挂在**面板实例根**上的排序 Canvas」。
    /// <para>
    /// 存在的意义：<see cref="UIService"/> 要能**无歧义**地找到「面板自己的 Canvas」去写 <c>sortingOrder</c>，
    /// 而不能误改到层 Canvas（<c>Canvas_Background…Canvas_Overlay</c>）。
    /// </para>
    /// </summary>
    internal sealed class PanelSortTarget : MonoBehaviour
    {
        [SerializeField] private Canvas _canvas;

        public Canvas Canvas => _canvas;

        /// <summary>由 PanelFactory 在实例化后调用：确保根上有 Canvas + GraphicRaycaster。</summary>
        public void Ensure(int sortingOrder)
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

            SetOrder(sortingOrder);

            // 嵌套 Canvas 需要自己的 GraphicRaycaster，否则点击命中顺序可能与显示不一致
            if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        /// <summary>
        /// 改本面板的层内排序。⚠️ 不要退化成「只写 <c>sortingOrder</c>」——
        /// 嵌套 Canvas 在 <c>overrideSorting == false</c> 时会丢弃它，且 <c>SetParent</c> 会把
        /// <c>overrideSorting</c> 重置掉，所以必须走 <see cref="NestedCanvas.SetOrder"/> 成对写。
        /// </summary>
        public void SetOrder(int sortingOrder) => NestedCanvas.SetOrder(_canvas, sortingOrder);
    }
}
